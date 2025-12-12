using UnityEngine;
using System.Collections.Generic;
using System.IO;

namespace Museum.Debug
{
    /// <summary>
    /// Debug模块配置
    /// 统一管理各个模块的Debug开关
    /// </summary>
    [System.Serializable]
    public class DebugModuleConfig
    {
        [System.Serializable]
        public class DebugModule
        {
            public string moduleName;
            public bool isEnabled = true;

            public DebugModule(string name, bool enabled = true)
            {
                moduleName = name;
                isEnabled = enabled;
            }
        }

        public List<DebugModule> debugModules = new List<DebugModule>();

        private static DebugModuleConfig instance;
        private static readonly string configPath = "Assets/Script/Debug/DebugModuleConfig.json";

        /// <summary>
        /// 获取单例实例
        /// </summary>
        public static DebugModuleConfig Instance
        {
            get
            {
                if (instance == null)
                {
                    LoadConfig();
                }
                return instance;
            }
        }

        /// <summary>
        /// 获取所有Debug模块列表
        /// </summary>
        public List<DebugModule> GetAllModules()
        {
            return debugModules;
        }

        /// <summary>
        /// 检查指定模块是否启用Debug
        /// </summary>
        public static bool IsModuleEnabled(string moduleName)
        {
            if (Instance == null)
                return false;

            var module = Instance.debugModules.Find(m => m.moduleName == moduleName);
            return module != null && module.isEnabled;
        }

        /// <summary>
        /// 手动保存配置文件
        /// </summary>
        public static void Save()
        {
            SaveConfig();
        }

        /// <summary>
        /// 确保指定模块存在并启用（如果不存在则自动添加为启用状态）
        /// </summary>
        public static void EnsureModuleEnabled(string moduleName)
        {
            if (Instance != null && !Instance.debugModules.Exists(m => m.moduleName == moduleName))
            {
                Instance.debugModules.Add(new DebugModule(moduleName, true));
                SaveConfig();
            }
        }

        /// <summary>
        /// 设置模块Debug状态
        /// </summary>
        public static void SetModuleEnabled(string moduleName, bool enabled)
        {
            if (Instance == null)
                return;

            var module = Instance.debugModules.Find(m => m.moduleName == moduleName);
            if (module != null)
            {
                module.isEnabled = enabled;
            }
            else
            {
                Instance.debugModules.Add(new DebugModule(moduleName, enabled));
            }
            SaveConfig();
        }

        /// <summary>
        /// 添加新的Debug模块
        /// </summary>
        public void AddModule(string moduleName)
        {
            if (!debugModules.Exists(m => m.moduleName == moduleName))
            {
                debugModules.Add(new DebugModule(moduleName, true));
            }
            SaveConfig();
        }

        /// <summary>
        /// 移除Debug模块
        /// </summary>
        public void RemoveModule(string moduleName)
        {
            debugModules.RemoveAll(m => m.moduleName == moduleName);
            SaveConfig();
        }

        /// <summary>
        /// 全部启用
        /// </summary>
        public void EnableAll()
        {
            foreach (var module in debugModules)
            {
                module.isEnabled = true;
            }
        }

        /// <summary>
        /// 全部禁用
        /// </summary>
        public void DisableAll()
        {
            foreach (var module in debugModules)
            {
                module.isEnabled = false;
            }
        }

        /// <summary>
        /// 清空所有模块
        /// </summary>
        public void ClearAll()
        {
            debugModules.Clear();
            SaveConfig();
        }

        /// <summary>
        /// 加载配置文件
        /// </summary>
        private static void LoadConfig()
        {
            if (File.Exists(configPath))
            {
                string json = File.ReadAllText(configPath);
                instance = JsonUtility.FromJson<DebugModuleConfig>(json);
            }
            else
            {
                instance = new DebugModuleConfig();
                // 添加默认模块
                instance.debugModules.Add(new DebugModule("Default", true));
                instance.debugModules.Add(new DebugModule("UI", true));
                SaveConfig();
            }
        }

        /// <summary>
        /// 保存配置文件
        /// </summary>
        private static void SaveConfig()
        {
            string json = JsonUtility.ToJson(instance, true);
            File.WriteAllText(configPath, json);
        }
    }
}
