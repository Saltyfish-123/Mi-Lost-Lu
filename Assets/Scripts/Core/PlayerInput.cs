using UnityEngine;

namespace Lost
{
    /// <summary>
    /// 集中读取旧版 Input Manager。任何键位调整只改这里，其他脚本不直接碰 Input。
    /// 挂在玩家预制体根节点上。执行顺序提前到 -100，保证同一帧内其他脚本读到的是本帧最新值。
    /// 为什么用旧输入：本工程没有安装 Input System 包，用旧 Input 可以零配置跑起来。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PlayerInput : MonoBehaviour
    {
        [Header("键位（策划案第 5 节）")]
        public KeyCode JumpKey = KeyCode.Space;
        public KeyCode RingKey = KeyCode.J;
        public KeyCode ShieldKey = KeyCode.K;
        public KeyCode InteractKey = KeyCode.E;
        public KeyCode SniffKey = KeyCode.Q;
        public KeyCode PauseKey = KeyCode.Escape;

        [Tooltip("勾上后鼠标左键等同 J，方便试手感")]
        public bool UseMouseForRing = true;

        // 水平输入：-1 / 0 / 1
        public float MoveX { get; private set; }
        public bool JumpPressed { get; private set; }      // 本帧按下（边沿）
        public bool JumpHeld { get; private set; }         // 持续按住
        public bool RingPressed { get; private set; }      // 本帧按下（边沿）
        public bool RingHeld { get; private set; }         // 持续按住（蓄力用）
        public bool RingReleased { get; private set; }     // 本帧松开（边沿）
        public bool ShieldHeld { get; private set; }       // 持续按住
        public bool InteractPressed { get; private set; }  // 本帧按下
        public bool SniffPressed { get; private set; }     // 本帧按下
        public bool PausePressed { get; private set; }     // 本帧按下

        void Update()
        {
            float x = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
            MoveX = x;

            JumpPressed = Input.GetKeyDown(JumpKey);
            JumpHeld = Input.GetKey(JumpKey);

            // 环：J 或鼠标左键，同一个键承担「投出」和「接回」
            bool ringDown = Input.GetKeyDown(RingKey) || (UseMouseForRing && Input.GetMouseButtonDown(0));
            bool ringUp = Input.GetKeyUp(RingKey) || (UseMouseForRing && Input.GetMouseButtonUp(0));
            bool ringNow = Input.GetKey(RingKey) || (UseMouseForRing && Input.GetMouseButton(0));

            RingPressed = ringDown;
            RingHeld = ringNow;
            RingReleased = ringUp;

            ShieldHeld = Input.GetKey(ShieldKey) || Input.GetMouseButton(1);
            InteractPressed = Input.GetKeyDown(InteractKey);
            SniffPressed = Input.GetKeyDown(SniffKey);
            PausePressed = Input.GetKeyDown(PauseKey);
        }
    }
}
