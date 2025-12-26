using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// UI Toggle事件组件
/// 该组件强制依赖Toggle组件，用于处理Toggle的开关状态变化事件。
/// 提供两个事件：开关开启时触发的事件和开关关闭时触发的事件。
/// </summary>
[RequireComponent(typeof(Toggle))]
public class UI_ToggleEvent : MonoBehaviour
{
    /// <summary>
    /// 开关开启时触发的事件
    /// </summary>
    [SerializeField]
    private UnityEvent _onToggleOn = new UnityEvent();

    /// <summary>
    /// 开关关闭时触发的事件
    /// </summary>
    [SerializeField]
    private UnityEvent _onToggleOff = new UnityEvent();

    /// <summary>
    /// 获取开关开启时的事件
    /// </summary>
    public UnityEvent OnToggleOn => _onToggleOn;

    /// <summary>
    /// 获取开关关闭时的事件
    /// </summary>
    public UnityEvent OnToggleOff => _onToggleOff;

    /// <summary>
    /// Toggle值改变时触发的方法
    /// 根据布尔值触发对应的事件
    /// </summary>
    /// <param name="isOn">Toggle的开关状态</param>
    public void OnToggleValueChanged(bool isOn)
    {
        if (isOn)
        {
            _onToggleOn?.Invoke();
        }
        else
        {
            _onToggleOff?.Invoke();
        }
    }

    /// <summary>
    /// 自动绑定到自身的Toggle组件（在Awake中调用）
    /// </summary>
    private void Awake()
    {
        Toggle toggle = GetComponent<Toggle>();
        if (toggle != null)
        {
            toggle.onValueChanged.AddListener(OnToggleValueChanged);
        }
    }
}
