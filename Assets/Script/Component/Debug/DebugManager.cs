using UnityEngine;

namespace Museum.Debug
{
    /// <summary>
    /// Debug管理器
    /// 提供统一的Debug输出接口，根据配置决定是否输出
    /// </summary>
    public static class DebugManager
    {
        /// <summary>
        /// 输出Debug信息（如果对应模块启用）
        /// </summary>
        public static void Log(string MarkName, string message)
        {
            if (DebugMarkConfig.IsMarkEnabled(MarkName))
            {
                UnityEngine.Debug.Log($"[{MarkName}] {message}");
            }
        }

        /// <summary>
        /// 输出Warning信息（如果对应模块启用）
        /// </summary>
        public static void LogWarning(string MarkName, string message)
        {
            if (DebugMarkConfig.IsMarkEnabled(MarkName))
            {
                UnityEngine.Debug.LogWarning($"[{MarkName}] {message}");
            }
        }

        /// <summary>
        /// 输出Error信息（如果对应模块启用）
        /// </summary>
        public static void LogErrorMsg(string MarkName, string message)
        {
            if (DebugMarkConfig.IsMarkEnabled(MarkName))
            {
                UnityEngine.Debug.LogError($"[{MarkName}] {message}");
            }
        }

        /// <summary>
        /// 无条件输出（总是输出，用于关键信息）
        /// </summary>
        public static void LogAlways(string MarkName, string message)
        {
            UnityEngine.Debug.Log($"[{MarkName}] {message}");
        }

        /// <summary>
        /// 检查模块是否启用
        /// </summary>
        public static bool IsMarkEnabled(string MarkName)
        {
            return DebugMarkConfig.IsMarkEnabled(MarkName);
        }
    }
}
