using UnityEngine;
using Museum.Debug;

namespace Museum.Component.Animation
{
    /// <summary>
    /// UI 动画播放控制组件
    /// 根据指定进度直接设置 Animation 默认剪辑的播放位置
    /// </summary>
    public class PlayClipByRatio : MonoBehaviour
    {
        [Header("动画设置")]
        [Tooltip("要控制的 AnimationClip")]
        [SerializeField]
        private AnimationClip clip;

        [Header("调试设置")]
        [Tooltip("当前动画进度 (0-1)，修改立即应用用于调试")]
        [Range(0f, 1f)]
        [SerializeField]
        private float Progress = 0f;

        /// <summary>
        /// 按照指定的进度播放 AnimationClip
        /// </summary>
        /// <param name="progress">播放进度 (0-1)</param>
        public void PlayToProgress(float progress)
        {
            if (clip == null)
            {
                Log.Print("UI", "Warning", "PlayClipByRatio 未指定 AnimationClip");
                return;
            }

            // 限制进度在 0-1 范围内
            progress = Mathf.Clamp01(progress);

            // 计算对应的时间点
            float time = progress * clip.length;

            // 采样动画到指定时间
            clip.SampleAnimation(gameObject, time);

            Log.Print("UI", "Debug", $"播放进度设置: {progress:F2} -> 时间: {time:F2}s");
        }

        /// <summary>
        /// 获取动画长度 (秒)
        /// </summary>
        public float GetClipLength()
        {
            return clip != null ? clip.length : 0f;
        }

        /// <summary>
        /// 组件启用时初始化 Progress 为 0
        /// </summary>
        private void OnEnable()
        {
            Progress = 0f;
        }

        /// <summary>
        /// 编辑器中值改变时更新动画进度（仅编辑器模式）
        /// </summary>
        private void OnValidate()
        {
            #if UNITY_EDITOR
            if (!Application.isPlaying && enabled)
            {
                ApplyProgress();
            }
            #endif
        }

        /// <summary>
        /// 应用调试进度（编辑器专用）
        /// </summary>
        private void ApplyProgress()
        {
            if (clip != null)
            {
                PlayToProgress(Progress);
            }
        }
    }
}
