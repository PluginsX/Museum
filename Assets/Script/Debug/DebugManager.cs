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
        public static void Log(string moduleName, string message)
        {
            if (DebugModuleConfig.IsModuleEnabled(moduleName))
            {
                UnityEngine.Debug.Log($"[{moduleName}] {message}");
            }
        }

        /// <summary>
        /// 输出Warning信息（如果对应模块启用）
        /// </summary>
        public static void LogWarning(string moduleName, string message)
        {
            if (DebugModuleConfig.IsModuleEnabled(moduleName))
            {
                UnityEngine.Debug.LogWarning($"[{moduleName}] {message}");
            }
        }

        /// <summary>
        /// 输出Error信息（如果对应模块启用）
        /// </summary>
        public static void LogErrorMsg(string moduleName, string message)
        {
            if (DebugModuleConfig.IsModuleEnabled(moduleName))
            {
                UnityEngine.Debug.LogError($"[{moduleName}] {message}");
            }
        }

        /// <summary>
        /// 无条件输出（总是输出，用于关键信息）
        /// </summary>
        public static void LogAlways(string moduleName, string message)
        {
            UnityEngine.Debug.Log($"[{moduleName}] {message}");
        }

        /// <summary>
        /// 检查模块是否启用
        /// </summary>
        public static bool IsModuleEnabled(string moduleName)
        {
            return DebugModuleConfig.IsModuleEnabled(moduleName);
        }
    }
}
