using UnityEngine;

namespace Lost
{
    /// <summary>
    /// 环戒系统：全游戏的核心动词。投出、蓄力、接回、连击层、环力。
    /// 设计约束（策划案 6.1）：环只有一枚。在手时安全有光，离手时输出在外、自己缴械。
    /// 环纹不直接改这里的字段，只订阅 OnThrow / OnCatch / OnHit 三个事件（D4 实现）。
    /// </summary>
    public class RingSystem : MonoBehaviour
    {
        [Header("引用")]
        public PlayerInput Input;
        public PlayerController Player;
        public RingProjectile Ring;
        public Transform Hand;

        [Header("投掷（基础）")]
        [Tooltip("true = 按下立刻投出（最灵敏，但没法蓄力）；false = 松手投出（可蓄力，有约 0.1s 延迟）。D1 手感就靠这个开关调")]
        public bool InstantThrow = false;
        public float ThrowRange = 4f;
        public float ThrowSpeed = 14f;
        public float ReturnSpeed = 10f;
        public float BaseDamage = 10f;
        public float BaseSize = 1f;

        [Header("蓄力")]
        public float ChargeTime = 0.4f;
        public float ChargeCost = 20f;
        public float ChargedRange = 7f;
        public float ChargedDamage = 18f;
        public float ChargedSize = 2f;

        [Header("接环判定")]
        [Tooltip("正常接环的窗口（秒）：环到手的剩余时间小于它就能接住")] public float CatchWindow = 0.2f;
        [Tooltip("完美接环窗口（秒）")] public float PerfectWindow = 0.12f;
        [Tooltip("接空的硬直（秒）")] public float MissStunTime = 0.4f;

        [Header("环力与连击")]
        public float MaxEnergy = 100f;
        public float EnergyRegen = 8f;
        public float CatchEnergyGain = 15f;
        public int MaxCombo = 3;
        [Tooltip("每一层连击给下一击加多少伤害（0.3 = 30%）")] public float ComboDamageBonus = 0.3f;

        public RingState State { get; private set; }
        public float Energy { get; private set; }
        public int Combo { get; private set; }
        public bool IsCharging { get { return _holding && _holdTime > 0f; } }
        public float ChargeProgress { get { return ChargeTime <= 0f ? 0f : Mathf.Clamp01(_holdTime / ChargeTime); } }

        /// <summary>参数：这一投是否蓄力。霜纹/鸣纹/双环挂这里。</summary>
        public event System.Action<bool> OnThrow;
        /// <summary>参数：是否完美接环。焰纹/忆纹/磁纹挂这里。</summary>
        public event System.Action<bool> OnCatch;
        /// <summary>环命中敌人。棘纹/雪纹挂这里。</summary>
        public event System.Action OnHit;
        public event System.Action<int> OnComboChanged;

        float _holdTime;
        bool _holding;

        void Awake()
        {
            Energy = MaxEnergy;
            State = RingState.InHand;
            if (Ring != null && Hand != null) Ring.Attach(Hand);
        }

        void Update()
        {
            if (Input == null || Ring == null || Hand == null) return;

            Energy = Mathf.Min(MaxEnergy, Energy + EnergyRegen * Time.deltaTime);
            SyncState();

            // 硬直 / 死亡期间，环的输入直接吞掉（不取消蓄力状态，只忽略输入）
            bool locked = Player != null && Player.IsInputLocked;
            if (locked)
            {
                CancelCharge();
                return;
            }

            if (State == RingState.InHand)
            {
                if (InstantThrow)
                {
                    if (Input.RingPressed) Throw(false);
                }
                else
                {
                    // 按住蓄力，松手投出：按住不足 0.4s 就是普通环
                    if (Input.RingPressed)
                    {
                        _holding = true;
                        _holdTime = 0f;
                    }
                    if (_holding && Input.RingHeld) _holdTime += Time.deltaTime;
                    if (_holding && Input.RingReleased)
                    {
                        _holding = false;
                        Throw(_holdTime >= ChargeTime);
                    }
                }
            }
            else if (Input.RingPressed)
            {
                TryCatch();
            }
        }

        void SyncState()
        {
            if (State == RingState.InHand || State == RingState.Hovering) return;
            if (Ring.IsParked) State = RingState.Hovering;
            else if (Ring.IsReturning) State = RingState.Returning;
            else State = RingState.Flying;
        }

        // ---------- 投 ----------
        void Throw(bool charged)
        {
            if (charged && Energy < ChargeCost)
            {
                charged = false;   // 环力不够就降级成普通投，不吞输入
            }

            if (charged) Energy -= ChargeCost;

            float damage = BaseDamage * (1f + ComboDamageBonus * Combo);
            if (charged) damage = Mathf.Max(damage, ChargedDamage);

            float range = charged ? ChargedRange : ThrowRange;
            float size = charged ? ChargedSize : BaseSize;
            float sign = Player != null ? Player.FacingSign : 1f;
            Vector2 direction = new Vector2(sign, 0f);

            Ring.Play(Hand.position, direction, range, ThrowSpeed, ReturnSpeed, size, charged /* 蓄力穿透 */, damage);

            State = RingState.Flying;
            _holding = false;
            _holdTime = 0f;

            if (Player != null) Player.Trigger("throw");
            if (OnThrow != null) OnThrow(charged);
            // 投出的后坐位移：用短时间锁住水平操控的方式做，直接改一帧速度会被减速度抹掉
            if (Player != null && Player.Motor != null && Player.Motor.Body != null)
            {
                Player.Motor.ApplyKnockback(
                    new Vector2(-sign * 3f, Player.Motor.Body.velocity.y), 0.06f);  // 策划案 14.2：投环后坐 0.3u
            }
        }

        public void CancelCharge()
        {
            _holding = false;
            _holdTime = 0f;
        }

        // ---------- 接 ----------
        void TryCatch()
        {
            // 环已经飘到手边（悬停）时算普通接住，不给完美判定，否则「不接」反而没代价
            if (Ring.IsParked)
            {
                Catch(false);
                return;
            }

            // 环到手的剩余时间：距离 / 回程速度。这就是「时机」的数学定义
            float timeToHand = Ring.TimeToHand;

            bool canCatch = timeToHand <= CatchWindow;
            bool perfect = timeToHand > 0.02f && timeToHand <= PerfectWindow;

            if (canCatch)
            {
                Catch(perfect);
            }
            else
            {
                // 接空：硬直 + 环继续飞，手感上要明确「你抓早了」
                if (Player != null) Player.Stun(MissStunTime);
                if (HitFeedback.I != null) HitFeedback.I.FailFeedback();
            }
        }

        void Catch(bool perfect)
        {
            Ring.Attach(Hand);
            State = RingState.InHand;

            Energy = Mathf.Min(MaxEnergy, Energy + CatchEnergyGain);

            if (perfect)
            {
                Combo = Mathf.Min(MaxCombo, Combo + 1);
                if (OnComboChanged != null) OnComboChanged(Combo);
            }

            if (Player != null) Player.Trigger("catch");
            if (HitFeedback.I != null) HitFeedback.I.CatchFeedback();
            if (OnCatch != null) OnCatch(perfect);
        }

        /// <summary>环命中敌人时由 RingProjectile 回调，D4 的棘纹 / 雪纹靠它</summary>
        public void NotifyHit()
        {
            if (OnHit != null) OnHit();
        }

        /// <summary>玩家受击时由 PlayerController 调用：连击层清零</summary>
        public void NotifyPlayerHit()
        {
            if (Combo == 0) return;
            Combo = 0;
            if (OnComboChanged != null) OnComboChanged(0);
        }
    }
}
