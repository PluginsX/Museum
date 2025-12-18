using UnityEngine;
using Museum.Debug;

namespace Museum.Component.Animation
{
    /// <summary>
    /// AnimationClip 播放器组件
    /// 支持自定义速度、起始位置、播放控制
    /// </summary>
    public class ClipPlayer : MonoBehaviour
    {
        [Header("动画设置")]
        [Tooltip("要播放的动画剪辑")]
        [SerializeField]
        private AnimationClip clip;

        [Tooltip("播放速度 (-10到10)")]
        [Range(-10f, 10f)]
        [SerializeField]
        private float speed = 1f;

        [Tooltip("播放起始位置比例 (0-1)")]
        [Range(0f, 1f)]
        [SerializeField]
        private float startRatio = 0f;

        [Tooltip("自动开始播放")]
        [SerializeField]
        private bool autoPlay = false;

        [Tooltip("循环播放方式")]
        [SerializeField]
        private LoopType loopType = LoopType.Repeat;


        

        // 内部状态
        private float currentTime = 0f;
        private bool isPlaying = false;
        private bool isInitialized = false;

        /// <summary>
        /// 设置播放速度
        /// </summary>
        /// <param name="newSpeed">新速度 (-10到10)</param>
        public void SetSpeed(float newSpeed)
        {
            speed = Mathf.Clamp(newSpeed, -10f, 10f);
            Log.Print("Animation", "Debug", $"ClipPlayer 速度设置为: {speed}");
        }

        /// <summary>
        /// 设置当前播放比例
        /// </summary>
        /// <param name="ratio">播放比例 (0-1)</param>
        public void SetCurrentRatio(float ratio)
        {
            if (clip == null)
            {
                Log.Print("Animation", "Warning", "ClipPlayer 未设置 AnimationClip");
                return;
            }

            ratio = Mathf.Clamp01(ratio);
            currentTime = ratio * clip.length;

            // 立即应用到游戏对象
            ApplyCurrentTime();

            Log.Print("Animation", "Debug", $"ClipPlayer 当前比例设置为: {ratio:F2}, 时间: {currentTime:F2}s");
        }

        /// <summary>
        /// 从当前位置向后播放
        /// </summary>
        public void Play()
        {
            speed = Mathf.Abs(speed);
            isPlaying = true;
            Log.Print("Animation", "Debug", $"ClipPlayer 开始播放: 速度={speed}, 起始时间={currentTime:F2}s");
        }

        /// <summary>
        /// 暂停播放
        /// </summary>
        public void Pause()
        {
            isPlaying = false;
            Log.Print("Animation", "Debug", $"ClipPlayer 暂停播放: 当前时间={currentTime:F2}s");
        }

        /// <summary>
        /// 反转方向并播放（从当前位置向前播放）
        /// </summary>
        public void Reverse()
        {
            speed *= -1;
            isPlaying = true;
            Log.Print("Animation", "Debug", $"ClipPlayer 反转播放: 新速度={speed}");
        }

        /// <summary>
        /// 从开始向后播放
        /// </summary>
        public void PlayFromStart()
        {
            SetCurrentRatio(0f);
            SetSpeed(Mathf.Abs(speed)); // 确保正向播放
            isPlaying = true;
        }

        /// <summary>
        /// 从末尾开始向前播放
        /// </summary>
        public void PlayFromEnd()
        {
            SetCurrentRatio(1f);
            SetSpeed(-Mathf.Abs(speed)); // 确保反向播放
            isPlaying = true;
        }

        private void Start()
        {
            InitializePlayer();
            hasStarted = true;

            if (autoPlay)
            {
                Play();
                Log.Print("UI", "Info", "ClipPlayer 自动播放启动");
            }
        }

        private void OnEnable()
        {
            if (hasStarted && autoPlay && !isPlaying)
            {
                Play();
                Log.Print("UI", "Info", "ClipPlayer 组件重新启用，恢复自动播放");
            }
        }
        private void Update()
        {
            if (!isInitialized) return;

            if (isPlaying && speed != 0)
            {
                // 根据速度和时间推进时间
                currentTime += speed * Time.deltaTime;

                // 处理播放边界逻辑
                float clipLength = clip.length;
                if (speed > 0)
                {
                    // 正向播放
                    if (currentTime >= clipLength)
                    {
                        if (loopType != LoopType.None)
                        {
                            switch (loopType)
                            {
                                case LoopType.Repeat:
                                    currentTime = 0;
                                    Log.Print("Animation", "Debug", "ClipPlayer 循环播放: 重置到开始");
                                    break;
                                case LoopType.PingPong:
                                    currentTime = clipLength;
                                    speed *= -1;
                                    Log.Print("Animation", "Debug", "ClipPlayer 循环播放: 正向→反向");
                                    break;
                                default:
                                    break;
                            }
                        }
                        else
                        {
                            currentTime = clipLength;
                            Pause();
                            Log.Print("Animation", "Debug", "ClipPlayer 到达结束，停止播放");
                        }
                    }
                }
                else if (speed < 0)
                {
                    // 反向播放
                    if (currentTime < 0)
                    {
                        if (loopType != LoopType.None)
                        {
                            switch (loopType)
                            {
                                case LoopType.Repeat:
                                    currentTime = clipLength;
                                    Log.Print("Animation", "Debug", "ClipPlayer 循环播放: 重置到末尾");
                                    break;
                                case LoopType.PingPong:
                                    currentTime = 0;
                                    speed *= -1;
                                    Log.Print("Animation", "Debug", "ClipPlayer 循环播放: 反向→正向");
                                    break;
                                default:
                                    break;
                            }
                        }
                        else
                        {
                            currentTime = 0;
                            Pause();
                            Log.Print("Animation", "Debug", "ClipPlayer 到达开始，停止播放");
                        }
                    }
                }

                ApplyCurrentTime();
            }
        }

        private bool hasStarted = false;

        private void InitializePlayer()
        {
            if (clip == null)
            {
                Log.Print("Animation", "Warning", "ClipPlayer 未设置 AnimationClip");
                return;
            }

            // 根据 startRatio 初始化当前时间
            currentTime = startRatio * clip.length;

            // 应用初始时间
            ApplyCurrentTime();
            isInitialized = true;

            Log.Print("Animation", "Info", $"ClipPlayer 初始化完成: 剪辑长度={clip.length:F2}s, 起始比例={startRatio:F2}");
        }

        private void ApplyCurrentTime()
        {
            if (clip != null && gameObject != null)
            {
                try
                {
                    clip.SampleAnimation(gameObject, currentTime);
                }
                catch (System.Exception e)
                {
                    Log.Print("Animation", "Error", $"ClipPlayer SampleAnimation 失败: {e.Message}");
                }
            }
        }

        /// <summary>
        /// 获取当前播放进度 (0-1)
        /// </summary>
        public float GetCurrentRatio()
        {
            if (clip == null) return 0f;
            return currentTime / clip.length;
        }

        /// <summary>
        /// 获取当前时间 (秒)
        /// </summary>
        public float GetCurrentTime()
        {
            return currentTime;
        }
    }
}
