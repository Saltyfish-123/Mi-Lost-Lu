using UnityEngine;

namespace Lost
{
    /// <summary>
    /// 纯运动和跳跃手感层：不读输入、不管状态、不碰战斗。
    /// 输入由 PlayerController 每帧调 SetInput 灌进来，物理统一在 FixedUpdate 处理。
    /// 数值全部照抄策划案第 15.1 节，改手感只改 Inspector，不改代码。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("水平移动")]
        [Tooltip("最高水平速度（u/s）")] public float MoveSpeed = 6f;
        [Tooltip("从静止加速到最高速的时间（秒），策划案 0.08")] public float AccelerationTime = 0.08f;
        [Tooltip("空中操控力，1 = 和地面一样灵活")] [Range(0f, 1f)] public float AirControl = 0.85f;

        [Header("跳跃")]
        [Tooltip("满跳高度（u）")] public float JumpHeight = 3.2f;
        [Tooltip("提前松开跳键后的目标高度（u）")] public float ShortJumpHeight = 1.8f;
        [Tooltip("土狼时间：走出平台边缘后仍可起跳的宽限（秒）")] public float CoyoteTime = 0.1f;
        [Tooltip("跳跃缓冲：落地前提前按键仍然生效的窗口（秒）")] public float JumpBufferTime = 0.1f;
        [Tooltip("顶点滞空：在最高点附近降低重力的时长（秒）")] public float ApexHangTime = 0.05f;
        [Range(0f, 1f)] [Tooltip("顶点滞空期间的重力比例，越小越飘")] public float ApexGravityScale = 0.55f;
        [Tooltip("下落时重力倍率，略大于 1 会落地更利落")] public float FallGravityMultiplier = 1.15f;

        [Header("地面检测")]
        [Tooltip("检测圆半径，约等于角色脚掌宽度的一半")] public float GroundCheckRadius = 0.35f;
        [Tooltip("检测点在角色原点下方多少（u）")] public float GroundCheckOffsetY = -0.78f;
        [Tooltip("留空会自动取 Ground + OneWay 两个图层")] public LayerMask GroundMask;

        public bool IsGrounded { get; private set; }
        /// <summary>给需要读速度的模块用（击退、相机前瞻）</summary>
        public Rigidbody2D Body { get { return _rb; } }
        public bool IsRising { get { return _rb != null && _rb.velocity.y > 0.01f; } }
        public float HorizontalSpeed { get { return _rb != null ? _rb.velocity.x : 0f; } }
        public float VerticalSpeed { get { return _rb != null ? _rb.velocity.y : 0f; } }

        /// <summary>起跳瞬间触发，动画层用它播 jump</summary>
        public event System.Action OnJump;
        /// <summary>落地瞬间触发，参数是落地时的竖直速度（正数），用来做尘雪粒子强度</summary>
        public event System.Action<float> OnLand;

        Rigidbody2D _rb;
        float _baseGravityScale;
        float _speedMultiplier = 1f;
        float _moveInput;
        bool _jumpRequested;
        bool _jumpHeld;
        float _coyoteTimer;
        float _bufferTimer;
        float _apexTimer;
        float _knockTimer;
        bool _isJumping;
        bool _jumpCutDone;
        bool _apexUsed;

        // 落地瞬间物理已经把竖直速度清零了，所以要持续记录「离地时的下落速度」
        float _airFallSpeed;

        /// <summary>由 Rigidbody 的重力设置反推出来的真实加速度（默认约 18.1 u/s²）</summary>
        float GravityAccel { get { return Mathf.Abs(Physics2D.gravity.y) * _baseGravityScale; } }

        /// <summary>满跳需要的初速，改重力或改跳高之后自动跟着变</summary>
        public float JumpVelocity { get { return Mathf.Sqrt(2f * Mathf.Max(1f, GravityAccel) * JumpHeight); } }

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.freezeRotation = true;
            // 允许角色站在斜坡上不滑，但惯性仍然保留（摩擦交给 PhysicsMaterial2D 处理）
            _baseGravityScale = _rb.gravityScale > 0f ? _rb.gravityScale : 1.85f;
            if (GroundMask.value == 0) GroundMask = LayerMask.GetMask("Ground", "OneWay");
        }

        /// <summary>每帧由 PlayerController 灌输入</summary>
        public void SetInput(float moveX, bool jumpPressed, bool jumpHeld)
        {
            _moveInput = Mathf.Clamp(moveX, -1f, 1f);
            if (jumpPressed) _jumpRequested = true;
            _jumpHeld = jumpHeld;
        }

        /// <summary>移速倍率：蓄力时 0，举盾时 0.5，其它情况 1</summary>
        public void SetSpeedMultiplier(float multiplier)
        {
            _speedMultiplier = Mathf.Clamp01(multiplier);
        }

        /// <summary>受击击退：直接改速度并短暂锁住水平操控，否则会被减速度抹掉</summary>
        public void ApplyKnockback(Vector2 velocity, float lockTime = 0.12f)
        {
            _rb.velocity = velocity;
            _knockTimer = lockTime;
        }

        public void SetVelocity(Vector2 velocity)
        {
            _rb.velocity = velocity;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (!IsGrounded) _airFallSpeed = Mathf.Max(_airFallSpeed, -_rb.velocity.y);

            UpdateGroundAndJump(dt);
            UpdateHorizontal(dt);
            UpdateJumpRelease();
            UpdateGravity(dt);
        }

        // ---------- 地面、土狼时间、跳跃缓冲 ----------
        void UpdateGroundAndJump(float dt)
        {
            bool grounded = CheckGround() && _rb.velocity.y <= 0.05f;
            bool wasGrounded = IsGrounded;
            IsGrounded = grounded;

            if (grounded)
            {
                _coyoteTimer = CoyoteTime;
                if (!wasGrounded)
                {
                    _isJumping = false;
                    _apexUsed = false;
                    if (_airFallSpeed > 0.5f && OnLand != null) OnLand(_airFallSpeed);
                    _airFallSpeed = 0f;
                }
            }
            else
            {
                _coyoteTimer -= dt;
            }

            if (_jumpRequested)
            {
                _bufferTimer = JumpBufferTime;
                _jumpRequested = false;
            }
            else
            {
                _bufferTimer -= dt;
            }

            if (_bufferTimer > 0f && (IsGrounded || _coyoteTimer > 0f))
            {
                Jump();
            }
        }

        void Jump()
        {
            _rb.velocity = new Vector2(_rb.velocity.x, JumpVelocity);
            IsGrounded = false;
            _coyoteTimer = 0f;
            _bufferTimer = 0f;
            _isJumping = true;
            _jumpCutDone = false;
            _apexUsed = false;
            _apexTimer = 0f;
            _airFallSpeed = 0f;
            if (OnJump != null) OnJump();
        }

        bool CheckGround()
        {
            Vector2 point = (Vector2)transform.position + Vector2.up * GroundCheckOffsetY;
            return Physics2D.OverlapCircle(point, GroundCheckRadius, GroundMask) != null;
        }

        // ---------- 水平速度 ----------
        void UpdateHorizontal(float dt)
        {
            if (_knockTimer > 0f)
            {
                _knockTimer -= dt;
                return;
            }

            float target = _moveInput * MoveSpeed * _speedMultiplier;
            float accel = MoveSpeed / Mathf.Max(0.01f, AccelerationTime);
            if (!IsGrounded) accel *= AirControl;

            float vx = Mathf.MoveTowards(_rb.velocity.x, target, accel * dt);
            _rb.velocity = new Vector2(vx, _rb.velocity.y);
        }

        // ---------- 可变跳跃：提前松键就砍高度 ----------
        void UpdateJumpRelease()
        {
            if (!_isJumping || _jumpCutDone || _jumpHeld) return;
            if (_rb.velocity.y <= 0f) return;

            // 高度与速度平方成正比，所以砍速度要开根号：sqrt(1.8 / 3.2) ≈ 0.75
            float cut = Mathf.Sqrt(Mathf.Clamp(ShortJumpHeight, 0.1f, JumpHeight) / Mathf.Max(0.1f, JumpHeight));
            _rb.velocity = new Vector2(_rb.velocity.x, _rb.velocity.y * cut);
            _jumpCutDone = true;
        }

        // ---------- 重力微调：顶点滞空 + 下落加重 ----------
        void UpdateGravity(float dt)
        {
            if (_isJumping && !_apexUsed && Mathf.Abs(_rb.velocity.y) < 0.6f)
            {
                _apexTimer = ApexHangTime;
                _apexUsed = true;
            }

            float scale = _baseGravityScale;
            if (_apexTimer > 0f)
            {
                _apexTimer -= dt;
                scale *= ApexGravityScale;
            }
            else if (_rb.velocity.y < -0.2f)
            {
                scale *= FallGravityMultiplier;
            }

            _rb.gravityScale = scale;
        }
    }
}
