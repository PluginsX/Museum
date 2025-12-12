using UnityEngine;

namespace Museum.Debug
{
    public static class Log
    {
        public static void Print(string category, string level, string message)
        {
            // 首先确保类别存在于配置中，如果不存在则自动添加并启用
            DebugModuleConfig.EnsureModuleEnabled(category);

            // 判断标签（类别）是否启用
            if (DebugModuleConfig.IsModuleEnabled(category))
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
        }
    }
}
