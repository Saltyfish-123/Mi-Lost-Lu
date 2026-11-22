using System.Collections;
using UnityEngine;

namespace Lost
{
    /// <summary>
    /// 命中反馈的统一入口：顿帧 0.06s、屏震、受击闪白。
    /// 全场景唯一单例，谁打中谁都调它，保证反馈不会两处各打一次。
    /// 顿帧用 Time.timeScale 实现，所以必须处理「暂停时被打」这种边界。
    /// </summary>
    public class HitFeedback : MonoBehaviour
    {
        public static HitFeedback I { get; private set; }

        [Header("顿帧")]
        [Tooltip("命中冻结时长，策划案 14.2 是 0.06s")] public float HitStopTime = 0.06f;
        [Tooltip("冻结期间的时间缩放，0 为完全静止")] [Range(0f, 1f)] public float HitStopScale = 0f;

        [Header("屏震")]
        public CameraFollow CameraFollow;
        [Tooltip("世界单位：0.06 ≈ 1080p 下约 2px")] public float ShakeAmplitude = 0.06f;
        public float ShakeDuration = 0.1f;

        [Header("闪白")]
        public Color FlashColor = Color.white;
        public float FlashDuration = 0.1f;

        bool _freezing;

        void Awake()
        {
            if (I != null && I != this)
            {
                Destroy(this);
                return;
            }
            I = this;
        }

        /// <summary>完整命中反馈：顿帧 + 屏震</summary>
        public void Hit()
        {
            // 暂停或已经冻结时不叠顿帧，否则会把 timeScale 恢复错
            if (!_freezing && Time.timeScale > 0f)
            {
                StartCoroutine(FreezeRoutine());
            }
            Shake(ShakeAmplitude, ShakeDuration);
        }

        /// <summary>轻命中：只震一下，不给顿帧（小雪团这类杂兵）</summary>
        public void HitSmall()
        {
            Shake(ShakeAmplitude * 0.6f, ShakeDuration * 0.8f);
        }

        /// <summary>完美接环：清脆的“叮”，手感靠它确认</summary>
        public void CatchFeedback()
        {
            Shake(0.03f, 0.05f);
        }

        /// <summary>接空：短促的失败震，配合硬直</summary>
        public void FailFeedback()
        {
            Shake(0.05f, 0.12f);
        }

        public void Shake(float amplitude, float duration)
        {
            if (CameraFollow != null) CameraFollow.Shake(amplitude, duration);
        }

        /// <summary>受击闪白：把精灵染成 FlashColor，再按 duration 渐隐回原色</summary>
        public void Flash(SpriteRenderer sprite)
        {
            if (sprite == null) return;
            StartCoroutine(FlashRoutine(sprite, sprite.color));
        }

        IEnumerator FreezeRoutine()
        {
            _freezing = true;
            float restore = Time.timeScale;
            Time.timeScale = HitStopScale;

            float t = 0f;
            while (t < HitStopTime)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            Time.timeScale = restore <= 0f ? 1f : restore;
            _freezing = false;
        }

        IEnumerator FlashRoutine(SpriteRenderer sprite, Color original)
        {
            float duration = Mathf.Max(0.01f, FlashDuration);
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                sprite.color = Color.Lerp(FlashColor, original, t / duration);
                yield return null;
            }
            sprite.color = original;
        }
    }
}
