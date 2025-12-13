using UnityEngine;

namespace Museum.Debug
{
    public static class Log
    {
        public static void Print(string category, string level, string message)
        {
            #if UNITY_EDITOR||UNITY_DEVELOPMENT_BUILD
            // 首先确保类别存在于配置中，如果不存在则自动添加并启用
            DebugMarkConfig.EnsureMarkEnabled(category);

            // 判断标签（类别）是否启用
            if (DebugMarkConfig.IsMarkEnabled(category))
            {
                // 根据等级输出不同类型的日志
                string logMessage = $"[{category}] {message}";
                switch (level.ToLower())
                {
                    case "debug":
                        UnityEngine.Debug.Log(logMessage);
                        break;
                    case "warning":
                        UnityEngine.Debug.LogWarning(logMessage);
                        break;
                    case "error":
                        UnityEngine.Debug.LogError(logMessage);
                        break;
                    default:
                        UnityEngine.Debug.Log(logMessage);
                        break;
                }
            }
            #endif
        }
    }
}
