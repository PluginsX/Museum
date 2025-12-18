using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Museum.Debug;

/// <summary>
/// Web平台JS命令执行组件
/// </summary>
[AddComponentMenu("Web/Web Function Executor")]
public class WebFunctionExecutor : MonoBehaviour
{
    [Header("JS命令配置")]
    [Tooltip("要执行的JS代码，可输入多行内容")]
    [TextArea(4, 20)]
    public string command = string.Empty;

    [Tooltip("是否在Awake阶段自动执行命令")]
    public bool autoExecute = false;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void JS_ExecuteCommand(string command);
#endif

    private void Awake()
    {
        if (autoExecute)
        {
            Execute(command);
        }
    }

    /// <summary>
    /// 执行当前命令字段的JS代码。
    /// </summary>
    public void Execute()
    {
        Execute(command);
    }

    /// <summary>
    /// 执行传入的JS代码。
    /// </summary>
    /// <param name="com">要执行的命令字符串</param>
    public void Execute(string com)
    {
        string targetCommand = string.IsNullOrWhiteSpace(com) ? command : com;
        if (string.IsNullOrWhiteSpace(targetCommand))
        {
            Log.Print("Web", "warn", "JS命令为空，跳过执行。");
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        try
        {
            JS_ExecuteCommand(targetCommand);
            Log.Print("Web", "debug", $"执行JS命令，长度：{targetCommand.Length}");
        }
        catch (Exception ex)
        {
            Log.Print("Web", "error", $"JS命令执行失败：{ex.Message}");
        }
#else
        Log.Print("Web", "info", "当前非WebGL运行环境，跳过JS命令执行。内容如下：\n" + targetCommand);
#endif
    }
}
