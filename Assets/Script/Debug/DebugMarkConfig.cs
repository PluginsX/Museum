using UnityEngine;
using System.Collections.Generic;

namespace Museum.Debug
{
    /// <summary>
    /// Debug模块配置
    /// 统一管理各个模块的Debug开关
    /// </summary>
    [CreateAssetMenu(fileName = "DebugMarkConfig", menuName = "Museum/Debug/Debug Mark Config")]
    public class DebugMarkConfig : ScriptableObject
    {
        [System.Serializable]
        public class DebugMark
        {
            public string MarkName;
            public bool isEnabled = true;

            public DebugMark(string name, bool enabled = true)
            {
                MarkName = name;
                isEnabled = enabled;
            }
        }

        [Header("Debug Marks")]
        public List<DebugMark> debugMarks = new List<DebugMark>();

        [Header("Runtime Settings")]
        [Tooltip("运行时是否自动添加新的Debug模块")]
        public bool autoAddNewMarks = true;

        private static DebugMarkConfig instance;

        /// <summary>
        /// 获取单例实例
        /// </summary>
        public static DebugMarkConfig Instance
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
        public List<DebugMark> GetAllMarks()
        {
            if (debugMarks == null)
            {
                debugMarks = new List<DebugMark>();
            }
            return debugMarks;
        }

        /// <summary>
        /// 检查指定模块是否启用Debug
        /// </summary>
        public static bool IsMarkEnabled(string MarkName)
        {
            if (Instance == null)
                return false;

            var Mark = Instance.debugMarks.Find(m => m.MarkName == MarkName);
            return Mark != null && Mark.isEnabled;
        }

        /// <summary>
        /// 手动标记资产为已修改（用于编辑器撤销系统）
        /// </summary>
        public static void MarkAsModified()
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(instance);
#endif
        }

        /// <summary>
        /// 确保指定模块存在并启用（如果不存在则自动添加为启用状态）
        /// </summary>
        public static void EnsureMarkEnabled(string MarkName)
        {
            if (Instance == null)
                return;

            // 检查是否允许自动添加
            if (!Instance.autoAddNewMarks)
                return;

            if (!Instance.debugMarks.Exists(m => m.MarkName == MarkName))
            {
                Instance.debugMarks.Add(new DebugMark(MarkName, true));
                MarkAsModified();
            }
        }

        /// <summary>
        /// 设置模块Debug状态
        /// </summary>
        public static void SetMarkEnabled(string MarkName, bool enabled)
        {
            if (Instance == null)
                return;

            var Mark = Instance.debugMarks.Find(m => m.MarkName == MarkName);
            if (Mark != null)
            {
                Mark.isEnabled = enabled;
            }
            else if (Instance.autoAddNewMarks)
            {
                // 只有在允许自动添加时才创建新模块
                Instance.debugMarks.Add(new DebugMark(MarkName, enabled));
            }
            MarkAsModified();
        }

        /// <summary>
        /// 添加新的Debug模块
        /// </summary>
        public void AddMark(string MarkName)
        {
            if (!debugMarks.Exists(m => m.MarkName == MarkName))
            {
                debugMarks.Add(new DebugMark(MarkName, true));
                MarkAsModified();
            }
        }

        /// <summary>
        /// 移除Debug模块
        /// </summary>
        public void RemoveMark(string MarkName)
        {
            debugMarks.RemoveAll(m => m.MarkName == MarkName);
            MarkAsModified();
        }

        /// <summary>
        /// 全部启用
        /// </summary>
        public void EnableAll()
        {
            foreach (var Mark in debugMarks)
            {
                Mark.isEnabled = true;
            }
            MarkAsModified();
        }

        /// <summary>
        /// 全部禁用
        /// </summary>
        public void DisableAll()
        {
            foreach (var Mark in debugMarks)
            {
                Mark.isEnabled = false;
            }
            MarkAsModified();
        }

        /// <summary>
        /// 清空所有模块
        /// </summary>
        public void ClearAll()
        {
            debugMarks.Clear();
            MarkAsModified();
        }

        /// <summary>
        /// 添加默认模块
        /// </summary>
        private void EnsureDefaultMarks()
        {
            EnsureMarkExists("Default", true);
            EnsureMarkExists("UI", true);
            EnsureMarkExists("Animation", true);
        }

        /// <summary>
        /// 确保模块存在
        /// </summary>
        private void EnsureMarkExists(string MarkName, bool defaultEnabled)
        {
            if (!debugMarks.Exists(m => m.MarkName == MarkName))
            {
                debugMarks.Add(new DebugMark(MarkName, defaultEnabled));
            }
        }

        /// <summary>
        /// 加载配置文件
        /// </summary>
        private static void LoadConfig()
        {
            instance = Resources.Load<DebugMarkConfig>("DebugMarkConfig");

            if (instance == null)
            {
                // 如果没有找到资产，创建默认实例
                UnityEngine.Debug.LogWarning("DebugMarkConfig asset not found in Resources. Using default configuration.");
                instance = ScriptableObject.CreateInstance<DebugMarkConfig>();
                instance.EnsureDefaultMarks();
            }
            else
            {
                // 确保有默认模块
                instance.EnsureDefaultMarks();
            }
        }
    }
}
