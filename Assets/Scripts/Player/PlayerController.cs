using UnityEngine;

namespace Lost
{
    /// <summary>
    /// 环的状态（策划案第 6.2 节）。这个名字以后 HUD 和环纹都要读，别改。
    /// </summary>
    public enum RingState
    {
        InHand,     // 在手：可投、可蓄力、可举盾、有光
        Flying,     // 飞出：不能防御，视野随环走
        Returning,  // 返航：可以做接环判定
        Hovering    // 悬停：已飞回手边但没接住，环一直飘在旁边
    }

    public enum PlayerState
    {
        Idle, Run, Jump, Fall, Shield, Stun, Hurt, Dead
    }

    /// <summary>
    /// 玩家状态机 + 输入分派。它不实现任何玩法，只决定「这一帧谁说了算」。
    /// 玩法在 PlayerMotor（运动）和 RingSystem（环）里。
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        [Header("引用（必须全部拖满，缺一个就空引用）")]
        public PlayerInput Input;
        public PlayerMotor Motor;
        public RingSystem Ring;
        public Transform RingPivot;
        public Animator Animator;

        [Header("表现")]
        [Tooltip("受击后锁输入的时长（秒）")] public float HurtLockTime = 0.15f;

        public PlayerState State { get; private set; }
        public bool IsInputLocked { get { return _stunTimer > 0f || State == PlayerState.Dead; } }
        /// <summary>1 = 朝右，-1 = 朝左，环的投掷方向用它</summary>
        public float FacingSign { get; private set; }

        /// <summary>按 E 的一瞬间触发（拾取、篝火、树洞商店都挂这个）</summary>
        public event System.Action OnInteract;
        /// <summary>按 Q 的一瞬间触发（宠物嗅探）</summary>
        public event System.Action OnSniff;

        float _stunTimer;

        void Awake()
        {
            if (Input == null) Input = GetComponent<PlayerInput>();
            if (Motor == null) Motor = GetComponent<PlayerMotor>();
            FacingSign = 1f;
        }

        void Update()
        {
            if (Input == null || Motor == null) return;

            if (_stunTimer > 0f) _stunTimer -= Time.deltaTime;

            bool locked = IsInputLocked;
            float moveX = locked ? 0f : Input.MoveX;

            Motor.SetInput(moveX, !locked && Input.JumpPressed, !locked && Input.JumpHeld);

            if (Mathf.Abs(moveX) > 0.1f) FacingSign = Mathf.Sign(moveX);

            // 蓄力时不能移动（策划案 6.3），这里统一算移速倍率
            float multiplier = 1f;
            if (Ring != null && Ring.IsCharging) multiplier = 0f;
            Motor.SetSpeedMultiplier(multiplier);

            if (!locked)
            {
                if (Input.InteractPressed && OnInteract != null) OnInteract();
                if (Input.SniffPressed && OnSniff != null) OnSniff();
            }

            UpdateState(moveX);
        }

        void UpdateState(float moveX)
        {
            if (State == PlayerState.Dead) return;

            if (_stunTimer > 0f)
            {
                // 有击退速度时算 Hurt，纯硬直算 Stun，动画不一样
                State = Mathf.Abs(Motor.VerticalSpeed) > 1f ? PlayerState.Hurt : PlayerState.Stun;
                return;
            }

            if (!Motor.IsGrounded)
            {
                State = Motor.IsRising ? PlayerState.Jump : PlayerState.Fall;
                return;
            }

            State = Mathf.Abs(Motor.HorizontalSpeed) > 0.3f ? PlayerState.Run : PlayerState.Idle;
        }

        /// <summary>硬直：接空环、受击都用它。取较大值，不会被短硬直打断长硬直。</summary>
        public void Stun(float duration)
        {
            _stunTimer = Mathf.Max(_stunTimer, duration);
        }

        /// <summary>受击：硬直 + 击退。D2 由 PlayerHealth 调用。</summary>
        public void Knockback(Vector2 direction, float strength)
        {
            Stun(HurtLockTime);
            Motor.ApplyKnockback(direction.normalized * strength);
            if (Ring != null) Ring.NotifyPlayerHit();   // 连击层清零（策划案 6.2）
        }

        public void Die()
        {
            State = PlayerState.Dead;
            Motor.SetInput(0f, false, false);
            Motor.SetVelocity(Vector2.zero);
        }

        /// <summary>动画触发器的安全封装：没有 Animator 时静默跳过，D1 不挂动画也能跑</summary>
        public void Trigger(string param)
        {
            if (Animator != null) Animator.SetTrigger(param);
        }
    }
}
