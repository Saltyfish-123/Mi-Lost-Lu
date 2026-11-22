using System.Collections.Generic;
using UnityEngine;

namespace Lost
{
    /// <summary>
    /// 能接住环伤害的东西实现这个接口。D1 只有空接口声明，敌人 D2 实现它。
    /// 用接口而不是找具体组件，是为了让 D1 的环不依赖任何还没写的类，单独就能跑。
    /// </summary>
    public interface IRingHitReceiver
    {
        void ReceiveRingHit(float damage, Vector2 direction);
    }

    /// <summary>
    /// 环本体：飞出 → 回旋 → 在手边悬停，等玩家按 J 接回。
    /// 撞墙提前返航（策划案 12.5 的风险点：环不能卡墙里）。
    /// 自己处理好自己的组件：刚体设 Kinematic、碰撞盒设 Trigger，你在 Inspector 里不用配。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
    public class RingProjectile : MonoBehaviour
    {
        [Header("外观")]
        public float SpinSpeed = 720f;
        public float BaseColliderRadius = 0.25f;

        [Header("图层")]
        [Tooltip("留空自动取 Ground + OneWay")] public LayerMask WallMask;
        [Tooltip("留空自动取 Enemy")] public LayerMask EnemyMask;
        [Tooltip("返航时到这里就停住悬停（u）")] public float ParkRadius = 0.6f;

        public bool IsReturning { get { return _returning; } }
        public bool IsParked { get { return _parked; } }
        public bool IsActive { get { return _active; } }
        /// <summary>环到手的剩余时间，接环判定的核心数值</summary>
        public float TimeToHand { get { return _retSpeed <= 0f ? 0f : DistanceToHand / _retSpeed; } }
        public float DistanceToHand
        {
            get { return _hand == null ? 0f : Vector2.Distance(transform.position, _hand.position); }
        }

        Rigidbody2D _rb;
        CircleCollider2D _col;
        Transform _hand;
        RingSystem _owner;

        Vector2 _dir;
        float _range, _outSpeed, _retSpeed, _damage;
        float _travel;
        bool _pierce, _active, _returning, _parked;
        readonly HashSet<int> _hitIds = new HashSet<int>();

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.gravityScale = 0f;

            _col = GetComponent<CircleCollider2D>();
            _col.isTrigger = true;
            _col.radius = BaseColliderRadius;

            if (WallMask.value == 0) WallMask = LayerMask.GetMask("Ground", "OneWay");
            if (EnemyMask.value == 0) EnemyMask = LayerMask.GetMask("Enemy");

            // 环在待机时是玩家的子物体，这里先把归属方缓存下来，飞出后 parent 就断了
            _owner = GetComponentInParent<RingSystem>();

            _active = false;
        }

        // ---------- 生命周期 ----------

        /// <summary>投出：由 RingSystem 调用</summary>
        public void Play(Vector2 origin, Vector2 direction, float range, float outSpeed, float retSpeed,
                         float sizeMultiplier, bool pierce, float damage)
        {
            if (transform.parent != null) _hand = transform.parent;   // 投出前挂在 RingPivot 上
            transform.SetParent(null, true);
            transform.position = origin;

            _dir = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right;
            _range = Mathf.Max(0.5f, range);
            _outSpeed = Mathf.Max(0.1f, outSpeed);
            _retSpeed = Mathf.Max(0.1f, retSpeed);
            _damage = damage;
            _pierce = pierce;

            _travel = 0f;
            _returning = false;
            _parked = false;
            _active = true;
            _hitIds.Clear();

            _col.radius = BaseColliderRadius * Mathf.Max(1f, sizeMultiplier);
            transform.rotation = Quaternion.identity;
        }

        /// <summary>接回手里：由 RingSystem 调用</summary>
        public void Attach(Transform hand)
        {
            _hand = hand;
            _active = false;
            _parked = false;
            _returning = false;
            _hitIds.Clear();

            transform.SetParent(hand, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            _col.radius = BaseColliderRadius;
        }

        /// <summary>由 RingSystem 在投出前记录手的位置（Play 内部也会兜底处理）</summary>
        public void Detach(Vector2 origin)
        {
            if (_hand == null && transform.parent != null) _hand = transform.parent;
            transform.SetParent(null, true);
            transform.position = origin;
        }

        void FixedUpdate()
        {
            if (!_active || _parked || _hand == null) return;

            float dt = Time.fixedDeltaTime;

            if (!_returning)
            {
                float stepLength = _outSpeed * dt;
                Vector2 step = _dir * stepLength;

                // 提前撞墙检测：14u/s 跑 60 帧物理，单靠 Trigger 会穿墙
                RaycastHit2D wall = Physics2D.Raycast(transform.position, _dir, stepLength + 0.1f, WallMask);
                if (wall.collider != null)
                {
                    _returning = true;
                    transform.position = wall.point - _dir * 0.1f;
                }
                else
                {
                    transform.position += (Vector3)step;
                    _travel += stepLength;
                    if (_travel >= _range) _returning = true;
                }
            }
            else
            {
                Vector2 toHand = (Vector2)_hand.position - (Vector2)transform.position;
                if (toHand.magnitude <= ParkRadius)
                {
                    _parked = true;   // 停住等玩家按 J，不自动接
                    return;
                }
                transform.position += (Vector3)(toHand.normalized * (_retSpeed * dt));
            }

            transform.Rotate(0f, 0f, -SpinSpeed * dt);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (!_active || _parked) return;
            if ((EnemyMask.value & (1 << other.gameObject.layer)) == 0) return;

            // 普通环每命中一个目标只结算一次；穿透环也一样，多段伤害是环纹的事
            if (!_hitIds.Add(other.GetInstanceID())) return;

            IRingHitReceiver receiver = other.GetComponentInParent<IRingHitReceiver>();
            if (receiver != null) receiver.ReceiveRingHit(_damage, _dir);

            if (HitFeedback.I != null) HitFeedback.I.Hit();

            if (_owner != null) _owner.NotifyHit();

            if (!_pierce) _returning = true;
        }
    }
}
