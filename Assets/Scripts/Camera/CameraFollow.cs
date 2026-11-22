using UnityEngine;

namespace Lost
{
    /// <summary>
    /// 2D 跟随相机：缓跟随 + 速度前瞻 + 竖直单独缓动 + 屏震。
    /// 为什么不用 Cinemachine：工程没装，而且 30 行代码比它更可控，帧率也更省。
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [Header("目标")]
        public Transform Target;

        [Header("构图")]
        [Tooltip("相对目标的偏移，y 一般给 1.0~1.5，让角色偏下一点")] public Vector2 Offset = new Vector2(0f, 1.2f);
        [Tooltip("水平缓动时间（秒），越小越跟手")] public float HorizontalSmoothTime = 0.12f;
        [Tooltip("竖直缓动时间，比水平慢一点，避免跳跃时镜头上下抽")] public float VerticalSmoothTime = 0.18f;

        [Header("前瞻")]
        [Tooltip("按移动方向往前多看多少（u）")] public float LookAheadDistance = 1.2f;
        public float LookAheadSmoothTime = 0.35f;

        Rigidbody2D _targetRb;
        Vector3 _smoothPos;
        float _lookAhead;
        float _vy;

        float _shakeAmp, _shakeDur, _shakeTime;
        System.Random _rand = new System.Random(12345);

        void Start()
        {
            if (Target == null) return;
            _targetRb = Target.GetComponent<Rigidbody2D>();
            _smoothPos = new Vector3(Target.position.x + Offset.x, Target.position.y + Offset.y, transform.position.z);
            transform.position = _smoothPos;
        }

        void LateUpdate()
        {
            if (Target == null) return;

            float dt = Time.deltaTime;

            // 前瞻：只按水平速度方向推镜头，站着不动就回到正中
            float dir = 0f;
            if (_targetRb != null && Mathf.Abs(_targetRb.velocity.x) > 0.5f) dir = Mathf.Sign(_targetRb.velocity.x);
            _lookAhead = Mathf.Lerp(_lookAhead, dir * LookAheadDistance, dt / Mathf.Max(0.01f, LookAheadSmoothTime));

            Vector3 desired = Target.position + new Vector3(Offset.x + _lookAhead, Offset.y, 0f);

            _smoothPos.x = Mathf.SmoothDamp(_smoothPos.x, desired.x, ref _vy, HorizontalSmoothTime, Mathf.Infinity, dt);
            float vyTmp = 0f;
            _smoothPos.y = Mathf.SmoothDamp(_smoothPos.y, desired.y, ref vyTmp, VerticalSmoothTime, Mathf.Infinity, dt);

            Vector3 final = new Vector3(_smoothPos.x, _smoothPos.y, transform.position.z);

            // 屏震叠加在缓动结果上，不污染 _smoothPos，否则会抖出漂移
            if (_shakeTime > 0f)
            {
                _shakeTime -= dt;
                float k = _shakeDur > 0f ? Mathf.Clamp01(_shakeTime / _shakeDur) : 0f;
                float rx = ((float)_rand.NextDouble() * 2f - 1f) * _shakeAmp * k;
                float ry = ((float)_rand.NextDouble() * 2f - 1f) * _shakeAmp * k;
                final.x += rx;
                final.y += ry;
            }

            transform.position = final;
        }

        /// <summary>屏震。单位是世界单位：0.06 ≈ 1080p 下 2px 左右</summary>
        public void Shake(float amplitude, float duration)
        {
            // 已经在震的话取更强的那一次，避免叠成抽搐
            _shakeAmp = Mathf.Max(_shakeAmp, amplitude);
            _shakeDur = Mathf.Max(_shakeDur, duration);
            _shakeTime = _shakeDur;
        }
    }
}
