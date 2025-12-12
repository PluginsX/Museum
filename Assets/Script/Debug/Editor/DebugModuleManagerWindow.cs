using UnityEngine;
using UnityEditor;
using Museum.Debug;

namespace Museum.Debug.Editor
{
    /// <summary>
    /// Debug模块管理编辑器窗口
    /// 提供可视化界面来控制各个Debug模块的开关
    /// </summary>
    public class DebugModuleManagerWindow : EditorWindow
    {
        private DebugModuleConfig config;
        private Vector2 scrollPosition = Vector2.zero;
        private bool configChanged = false;

        [MenuItem("Window/Museum/Debug Module Manager")]
        public static void ShowWindow()
        {
            GetWindow<DebugModuleManagerWindow>("Debug Manager");
        }

        private void OnEnable()
        {
            // 窗口启用时自动加载现有 JSON 配置
            config = DebugModuleConfig.Instance;
            configChanged = false;
        }

        private void OnGUI()
        {
            GUILayout.Label("Debug Module Manager", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // 加载配置
            if (config == null)
            {
                config = DebugModuleConfig.Instance;
            }

            if (config == null)
            {
                EditorGUILayout.HelpBox("DebugModuleConfig could not be loaded!\nCheck the Assets/Script/Debug/DebugModuleConfig.json file.", MessageType.Error);
                return;
            }

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
                if (EditorUtility.DisplayDialog("Clear All Modules", "Are you sure you want to clear all modules?", "Yes", "No"))
                {
                    config.ClearAll();
                    configChanged = true;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.Separator();
            EditorGUILayout.Space();

            // 显示模块列表
            GUILayout.Label("Debug Modules:", EditorStyles.boldLabel);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            var modules = config.GetAllModules();

            if (modules.Count == 0)
            {
                EditorGUILayout.HelpBox("No debug modules configured yet.", MessageType.Info);
            }
            else
            {
                for (int i = 0; i < modules.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal("box");

                    // 模块名称
                    GUILayout.Label(modules[i].moduleName, GUILayout.Width(150));

                    // Toggle开关
                    bool oldEnabled = modules[i].isEnabled;
                    modules[i].isEnabled = EditorGUILayout.Toggle(modules[i].isEnabled, GUILayout.Width(50));
                    if (modules[i].isEnabled != oldEnabled)
                    {
                        configChanged = true;
                    }

                    // 删除按钮
                    if (GUILayout.Button("×", GUILayout.Width(30), GUILayout.Height(18)))
                    {
                        config.RemoveModule(modules[i].moduleName);
                        configChanged = true;
                        break;
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.Separator();

            // 添加新模块
            GUILayout.Label("Add New Module:", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();

            newModuleName = EditorGUILayout.TextField("Module Name:", newModuleName);

            if (GUILayout.Button("Add", GUILayout.Width(50)))
            {
                if (!string.IsNullOrEmpty(newModuleName))
                {
                    config.AddModule(newModuleName);
                    configChanged = true;
                    newModuleName = "";
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // 配置文件显示
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Config File:", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel("Assets/Script/Debug/DebugModuleConfig.json", EditorStyles.boldLabel, GUILayout.Height(18));
            EditorGUILayout.EndHorizontal();

            // 如果配置有变化，提示保存
            if (configChanged)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("Configuration has been modified. Changes will be saved automatically.", MessageType.Info);

                if (GUILayout.Button("Manual Save"))
                {
                    SaveConfig();
                }
            }
        }

        private void OnFocus()
        {
            // 窗口获得焦点时重新加载配置
            config = null;
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

        private static string newModuleName = "";

        private void SaveConfig()
        {
            DebugModuleConfig.Save();
        }
    }
}
