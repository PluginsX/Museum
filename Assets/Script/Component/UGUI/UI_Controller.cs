using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("UI/UI Controller")]
public class UI_Controller : MonoBehaviour
{
    #region 枚举定义
    /// <summary>
    /// UI可见性枚举（提升类型安全）
    /// </summary>
    public enum UIVisibility
    {
        /// <summary>显示UI</summary>
        Show,
        /// <summary>隐藏UI</summary>
        Hide,
        /// <summary>切换状态</summary>
        Toggle
    }

    /// <summary>
    /// 孤立模式枚举
    /// </summary>
    public enum IsolationMode
    {
        /// <summary>禁用孤立模式</summary>
        Disabled,
        /// <summary>启用孤立模式</summary>
        Enabled
    }
    #endregion
    // 孤立模式：默认开启，仅允许一个UI对象激活
    [Header("核心配置")]
    [Tooltip("孤立模式：开启时每次仅允许激活一个UI对象")]
    [SerializeField] private bool _isolationMode = true;

    // UI对象配置列表（序列化用）
    [Header("UI对象列表")]
    [SerializeField] private List<UIItem> _uiItems = new List<UIItem>();

    // 运行时缓存：当前激活的UI键名（用于孤立模式快速查找）
    private string _currentActiveKey = string.Empty;
    // 运行时缓存：键名到UIItem的映射（提升查找效率）
    private Dictionary<string, UIItem> _uiItemDict;

    #region 序列化数据结构
    // 单个UI项的配置（用于Inspector显示和数据存储）
    [Serializable]
    public class UIItem
    {
        [Tooltip("UI唯一标识键名（不可重复）")]
        public string keyName = "UI_Key";
        
        [Tooltip("目标UGUI对象（需挂载RectTransform）")]
        public GameObject targetUI;
        
        [Tooltip("初始是否启用该UI对象")]
        public bool isEnable = false;
    }
    #endregion

    #region 生命周期
    private void Awake()
    {
        // 初始化字典映射
        InitUIDictionary();
        
        // 应用初始启用状态
        ApplyInitialState();
    }

    // 初始化键名到UIItem的字典（去重+校验）
    private void InitUIDictionary()
    {
        _uiItemDict = new Dictionary<string, UIItem>();
        
        foreach (var item in _uiItems)
        {
            // 跳过空键名或空UI对象
            if (string.IsNullOrEmpty(item.keyName) || item.targetUI == null)
            {
                Debug.LogWarning($"UI Controller: 无效的UI配置项 - 键名为空或目标UI未指定 | 索引：{_uiItems.IndexOf(item)}", this);
                continue;
            }
            
            // 检测重复键名
            if (_uiItemDict.ContainsKey(item.keyName))
            {
                Debug.LogWarning($"UI Controller: 重复的UI键名 [{item.keyName}]，已跳过重复项", this);
                continue;
            }
            
            _uiItemDict.Add(item.keyName, item);
        }
    }

    // 应用初始启用状态
    private void ApplyInitialState()
    {
        int activeCount = 0;
        foreach (var kvp in _uiItemDict)
        {
            var uiItem = kvp.Value;
            // 设置初始激活状态
            SetGameObjectActive(uiItem.targetUI, uiItem.isEnable);
            
            // 孤立模式下记录初始激活的UI（确保仅一个）
            if (_isolationMode && uiItem.isEnable)
            {
                activeCount++;
                if (activeCount > 1)
                {
                    Debug.LogWarning($"UI Controller: 孤立模式开启时，初始启用的UI数量超过1个，已自动禁用多余项 | 违规键名：{kvp.Key}", this);
                    SetGameObjectActive(uiItem.targetUI, false);
                    uiItem.isEnable = false;
                }
                else
                {
                    _currentActiveKey = kvp.Key;
                }
            }
        }
    }
    #endregion

    #region 核心API（开发版本，最优设计）
    /// <summary>
    /// 设置孤立模式
    /// </summary>
    /// <param name="mode">孤立模式</param>
    public void SetIsolationMode(IsolationMode mode)
    {
        _isolationMode = mode == IsolationMode.Enabled;

        // 切换为孤立模式时，自动确保仅激活一个UI
        if (_isolationMode && !string.IsNullOrEmpty(_currentActiveKey))
        {
            DisableAllExcept(_currentActiveKey);
        }

        Debug.Log($"UI Controller: 孤立模式已设置为 [{_isolationMode}]", this);
    }

    /// <summary>
    /// 设置UI可见性（核心API）
    /// </summary>
    /// <param name="keyName">UI键名</param>
    /// <param name="visibility">可见性设置</param>
    /// <returns>操作是否成功</returns>
    public bool SetVisibility(string keyName, UIVisibility visibility)
    {
        if (!_uiItemDict.TryGetValue(keyName, out var targetItem))
        {
            Debug.LogError($"UI Controller: 未找到UI对象 [{keyName}]", this);
            return false;
        }

        if (targetItem.targetUI == null)
        {
            Debug.LogError($"UI Controller: UI对象未指定 [{keyName}]", this);
            return false;
        }

        // 计算目标状态
        bool targetState = visibility switch
        {
            UIVisibility.Show => true,
            UIVisibility.Hide => false,
            UIVisibility.Toggle => !targetItem.targetUI.activeSelf,
            _ => false
        };

        // 孤立模式处理
        if (targetState && _isolationMode)
        {
            DisableAllExcept(keyName);
            _currentActiveKey = keyName;
        }
        else if (!targetState && _currentActiveKey == keyName)
        {
            _currentActiveKey = string.Empty;
        }

        // 设置UI状态
        SetGameObjectActive(targetItem.targetUI, targetState);
        targetItem.isEnable = targetState;

        return true;
    }

    /// <summary>
    /// 显示UI
    /// </summary>
    public bool Show(string keyName) => SetVisibility(keyName, UIVisibility.Show);

    /// <summary>
    /// 隐藏UI
    /// </summary>
    public bool Hide(string keyName) => SetVisibility(keyName, UIVisibility.Hide);

    /// <summary>
    /// 切换UI状态
    /// </summary>
    public bool Toggle(string keyName) => SetVisibility(keyName, UIVisibility.Toggle);
    #endregion

    #region 查询API（新增）
    /// <summary>
    /// 查询指定UI是否可见
    /// </summary>
    /// <param name="keyName">UI唯一键名</param>
    /// <returns>UI是否可见</returns>
    public bool IsVisible(string keyName)
    {
        if (_uiItemDict.TryGetValue(keyName, out var item) && item.targetUI != null)
        {
            return item.targetUI.activeSelf;
        }
        return false;
    }

    /// <summary>
    /// 获取所有UI键名列表
    /// </summary>
    /// <returns>所有UI键名列表</returns>
    public List<string> GetAllKeys()
    {
        return new List<string>(_uiItemDict.Keys);
    }

    /// <summary>
    /// 获取当前可见的UI键名列表
    /// </summary>
    /// <returns>可见UI键名列表</returns>
    public List<string> GetVisibleKeys()
    {
        var visibleKeys = new List<string>();
        foreach (var kvp in _uiItemDict)
        {
            if (kvp.Value.targetUI != null && kvp.Value.targetUI.activeSelf)
            {
                visibleKeys.Add(kvp.Key);
            }
        }
        return visibleKeys;
    }

    /// <summary>
    /// 获取当前激活的UI键名（孤立模式专用）
    /// </summary>
    /// <returns>当前激活的UI键名，无则返回空字符串</returns>
    public string GetCurrentVisibleKey()
    {
        return _currentActiveKey;
    }

    /// <summary>
    /// 获取孤立模式状态
    /// </summary>
    /// <returns>孤立模式是否启用</returns>
    public bool IsIsolationModeEnabled()
    {
        return _isolationMode;
    }
    #endregion

    #region 批量操作API
    /// <summary>
    /// 显示所有UI对象
    /// </summary>
    public void ShowAll()
    {
        foreach (var key in GetAllKeys())
        {
            Show(key);
        }
    }

    /// <summary>
    /// 隐藏所有UI对象
    /// </summary>
    public void HideAll()
    {
        foreach (var key in GetAllKeys())
        {
            Hide(key);
        }
    }

    /// <summary>
    /// 显示多个UI对象
    /// </summary>
    /// <param name="keys">UI键名数组</param>
    public void ShowMultiple(params string[] keys)
    {
        foreach (var key in keys)
        {
            Show(key);
        }
    }
    #endregion

    #region Unity事件系统兼容API（void版本）
    /// <summary>
    /// 显示UI对象（事件系统专用）
    /// </summary>
    /// <param name="keyName">UI键名</param>
    public void ShowUI(string keyName) => Show(keyName);

    /// <summary>
    /// 隐藏UI对象（事件系统专用）
    /// </summary>
    /// <param name="keyName">UI键名</param>
    public void HideUI(string keyName) => Hide(keyName);

    /// <summary>
    /// 切换UI对象显示状态（事件系统专用）
    /// </summary>
    /// <param name="keyName">UI键名</param>
    public void ToggleUI(string keyName) => Toggle(keyName);
    #endregion



    #region 辅助方法
    // 安全设置GameObject激活状态（避免空引用）
    private void SetGameObjectActive(GameObject go, bool active)
    {
        if (go == null) return;
        if (go.activeSelf != active)
        {
            go.SetActive(active);
        }
    }

    // 禁用除指定键名外的所有UI对象（孤立模式专用）
    private void DisableAllExcept(string keepKey)
    {
        foreach (var kvp in _uiItemDict)
        {
            if (kvp.Key != keepKey)
            {
                SetGameObjectActive(kvp.Value.targetUI, false);
                kvp.Value.isEnable = false;
            }
        }
    }


    #endregion

    #region 编辑器校验（可选）
    private void OnValidate()
    {
        // 编辑器模式下校验UI项配置
        for (int i = 0; i < _uiItems.Count; i++)
        {
            var item = _uiItems[i];
            // 自动填充默认键名
            if (string.IsNullOrEmpty(item.keyName))
            {
                item.keyName = $"UI_{i + 1}";
            }
        }
    }
    #endregion
}
