using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using Museum.Debug;

namespace Museum.Debug.Editor
{
    /// <summary>
    /// Debug模块管理编辑器窗口
    /// 提供可视化界面来控制各个Debug模块的开关
    /// </summary>
    public class DebugMarkManagerWindow : EditorWindow
    {
        private DebugMarkConfig config;
        private Vector2 scrollPosition = Vector2.zero;
        private bool configChanged = false;
        private List<System.Action> deferredActions = new List<System.Action>();

        [MenuItem("Window/Museum/Debug Mark Manager")]
        public static void ShowWindow()
        {
            GetWindow<DebugMarkManagerWindow>("Debug Manager");
        }

        private void OnEnable()
        {
            // 窗口启用时直接引用资产
            config = DebugMarkConfig.Instance;
            configChanged = false;
        }

        private void OnGUI()
        {
            GUILayout.Label("Debug Mark Manager", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // 加载配置
            if (config == null)
            {
                config = DebugMarkConfig.Instance;
            }

            if (config == null)
            {
                EditorGUILayout.HelpBox("DebugMarkConfig could not be loaded!\nPlease create a DebugMarkConfig asset in Assets/Resources/", MessageType.Error);
                return;
            }

            EditorGUILayout.Space();

            // 运行时设置

            bool oldAutoAdd = config.autoAddNewMarks;
            config.autoAddNewMarks = EditorGUILayout.Toggle(
                new GUIContent("自动添加新标签", "当代码中出现新的Debug模块时，是否自动添加到列表中"),
                config.autoAddNewMarks
            );

            if (config.autoAddNewMarks != oldAutoAdd)
            {
                configChanged = true;
            }

            EditorGUILayout.Space();
            EditorGUILayout.Separator();
            EditorGUILayout.Space();

            // 快速控制按钮
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Enable All", GUILayout.Height(30)))
            {
                config.EnableAll();
                configChanged = true;
            }

            if (GUILayout.Button("Disable All", GUILayout.Height(30)))
            {
                config.DisableAll();
                configChanged = true;
            }

            if (GUILayout.Button("Clear All", GUILayout.Height(30)))
            {
                if (EditorUtility.DisplayDialog("Clear All Marks", "Are you sure you want to clear all Marks?", "Yes", "No"))
                {
                    deferredActions.Add(() => config.ClearAll());
                    configChanged = true;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.Separator();
            EditorGUILayout.Space();

            // 显示模块列表
            GUILayout.Label("Debug Mark:", EditorStyles.boldLabel);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            var Marks = config.GetAllMarks();

            if (Marks == null || Marks.Count == 0)
            {
                EditorGUILayout.HelpBox("No debug Mark configured yet.", MessageType.Info);
            }
            else
            {
                // 创建一个临时列表来存储要显示的模块，避免在迭代时修改原列表
                var MarksToDisplay = new List<DebugMarkConfig.DebugMark>(Marks);
                string MarkToDelete = null;
                
                for (int i = 0; i < MarksToDisplay.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal("box");

                    // 模块名称 - 自适应宽度占满剩余空间
                    EditorGUILayout.LabelField(MarksToDisplay[i].MarkName, EditorStyles.boldLabel, GUILayout.ExpandWidth(true));

                    // Toggle开关
                    bool oldEnabled = MarksToDisplay[i].isEnabled;
                    MarksToDisplay[i].isEnabled = EditorGUILayout.Toggle(MarksToDisplay[i].isEnabled, GUILayout.Width(50));
                    if (MarksToDisplay[i].isEnabled != oldEnabled)
                    {
                        configChanged = true;
                    }

                    // 删除按钮
                    if (GUILayout.Button("×", GUILayout.Width(30), GUILayout.Height(18)))
                    {
                        MarkToDelete = MarksToDisplay[i].MarkName;
                        configChanged = true;
                    }

                    EditorGUILayout.EndHorizontal();
                }

                // 延迟到OnGUI结束时删除
                if (!string.IsNullOrEmpty(MarkToDelete))
                {
                    deferredActions.Add(() => config.RemoveMark(MarkToDelete));
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.Separator();

            // 添加新模块
            GUILayout.Label("Add New Mark:", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();

            newMarkName = EditorGUILayout.TextField("Mark Name:", newMarkName);

            if (GUILayout.Button("Add", GUILayout.Width(50)))
            {
                if (!string.IsNullOrEmpty(newMarkName))
                {
                    config.AddMark(newMarkName);
                    configChanged = true;
                    newMarkName = "";
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // 资产显示
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Asset:", EditorStyles.boldLabel);
            EditorGUILayout.ObjectField(config, typeof(ScriptableObject), false);
            EditorGUILayout.EndHorizontal();

            // 如果配置有变化，提示保存
            if (configChanged)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("Configuration has been modified and will be saved.", MessageType.Info);
            }

            // 执行延迟动作
            foreach (var action in deferredActions)
            {
                action?.Invoke();
            }
            deferredActions.Clear();
        }

        private void OnFocus()
        {
            // 窗口获得焦点时重新加载配置
            if (config == null)
            {
                config = DebugMarkConfig.Instance;
            }
            configChanged = false;
        }

        private void OnLostFocus()
        {
            // 窗口失去焦点时保存配置
            if (configChanged)
            {
                SaveConfig();
                configChanged = false;
            }
        }

        private static string newMarkName = "";

        private void SaveConfig()
        {
            DebugMarkConfig.MarkAsModified();
        }
    }
}
