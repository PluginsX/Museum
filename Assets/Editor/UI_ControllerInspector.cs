#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(UI_Controller))]
[CanEditMultipleObjects]
public class UI_ControllerInspector : Editor
{
    private ReorderableList _uiItemList;
    private SerializedProperty _isolationMode;
    private SerializedProperty _uiItems;
    private UI_Controller _targetController;

    private void OnEnable()
    {
        // 获取序列化属性（与UI_Controller中的字段名对应）
        _isolationMode = serializedObject.FindProperty("_isolationMode");
        _uiItems = serializedObject.FindProperty("_uiItems");

        // 初始化可重排列表（优化Inspector显示）
        InitReorderableList();
    }

    /// <summary>
    /// 初始化可重排列表，自定义列布局
    /// </summary>
    private void InitReorderableList()
    {
        _uiItemList = new ReorderableList(serializedObject, _uiItems, true, true, true, true)
        {
            // 绘制列表头部（分栏标题）
            drawHeaderCallback = rect =>
            {
                // 分割列头区域：键名(25%) + 目标UI(55%) + 初始启用(20%)
                var keyRect = new Rect(rect.x, rect.y, rect.width * 0.25f, rect.height);
                var uiRect = new Rect(rect.x + rect.width * 0.25f, rect.y, rect.width * 0.55f, rect.height);
                var enableRect = new Rect(rect.x + rect.width * 0.8f, rect.y, rect.width * 0.2f, rect.height);
                
                EditorGUI.LabelField(keyRect, "Name", EditorStyles.boldLabel);
                EditorGUI.LabelField(uiRect, "Target", EditorStyles.boldLabel);
                EditorGUI.LabelField(enableRect, "Enable", EditorStyles.boldLabel);
            },

            // 绘制列表项内容
            drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                var element = _uiItems.GetArrayElementAtIndex(index);
                
                // 调整行高和偏移，避免重叠
                rect.height = EditorGUIUtility.singleLineHeight;
                rect.y += 2;

                // 1. 键名列（25%宽度，减去间距）
                var keyProp = element.FindPropertyRelative("keyName");
                var keyRect = new Rect(rect.x, rect.y, rect.width * 0.25f - 2, rect.height);
                EditorGUI.PropertyField(keyRect, keyProp, GUIContent.none);

                // 2. 目标UI列（55%宽度，减去间距）
                var uiProp = element.FindPropertyRelative("targetUI");
                var uiRect = new Rect(rect.x + rect.width * 0.25f, rect.y, rect.width * 0.55f - 2, rect.height);
                EditorGUI.PropertyField(uiRect, uiProp, GUIContent.none);

                // 3. 初始启用列（20%宽度）始终靠右
                var enableProp = element.FindPropertyRelative("isEnable");
                var enableRect = new Rect(rect.x + rect.width * 0.8f, rect.y, rect.width * 0.2f, rect.height);

                // 记录修改前的值，用于检测变化
                bool oldValue = enableProp.boolValue;

                // 绘制复选框
                EditorGUI.PropertyField(enableRect, enableProp, GUIContent.none);

                // 检测值是否发生变化，如果变化则立即应用到对应的GameObject
                if (enableProp.boolValue != oldValue)
                {
                    // 处理孤立模式：如果启用了孤立模式且正在勾选新项，自动禁用其他项
                    if (enableProp.boolValue && _isolationMode.boolValue)
                    {
                        // 遍历所有其他项，禁用已启用的项
                        for (int i = 0; i < _uiItems.arraySize; i++)
                        {
                            if (i == index) continue; // 跳过当前项

                            var otherElement = _uiItems.GetArrayElementAtIndex(i);
                            var otherEnableProp = otherElement.FindPropertyRelative("isEnable");
                            var otherTargetUIProp = otherElement.FindPropertyRelative("targetUI");

                            // 如果其他项之前是启用的，现在禁用它
                            if (otherEnableProp.boolValue)
                            {
                                otherEnableProp.boolValue = false;

                            // 设置对应GameObject的激活状态
                            if (otherTargetUIProp.objectReferenceValue is GameObject otherGO)
                            {
                                otherGO.SetActive(false);

                                // 只在非运行时标记场景已修改
                                if (!Application.isPlaying)
                                {
                                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                                        otherGO.scene);
                                }
                            }
                            }
                        }
                    }

                    // 设置当前项对应GameObject的激活状态
                    var targetUIProp = element.FindPropertyRelative("targetUI");
                    if (targetUIProp.objectReferenceValue is GameObject targetGO)
                    {
                        // 在编辑器模式下立即设置GameObject的激活状态
                        targetGO.SetActive(enableProp.boolValue);

                        // 只在非运行时标记场景已修改（运行时不能调用此API）
                        if (!Application.isPlaying)
                        {
                            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                                targetGO.scene);
                        }
                    }
                }
            },

            // 自定义列表项高度
            elementHeightCallback = index => EditorGUIUtility.singleLineHeight + 4,
            
            // 空列表时的提示文本
            drawNoneElementCallback = rect =>
            {
                EditorGUI.LabelField(rect, "暂无UI配置项，点击 + 按钮添加", EditorStyles.centeredGreyMiniLabel);
            },
            
            // 列表项添加回调（初始化新项）
            onAddCallback = list =>
            {
                // 先扩展数组大小
                list.serializedProperty.arraySize++;
                // 获取新添加的最后一个元素
                var newElement = list.serializedProperty.GetArrayElementAtIndex(list.serializedProperty.arraySize - 1);
                // 设置默认值
                newElement.FindPropertyRelative("keyName").stringValue = $"UI_{list.serializedProperty.arraySize}";
                newElement.FindPropertyRelative("isEnable").boolValue = false;
                serializedObject.ApplyModifiedProperties();
            }
        };
    }

    /// <summary>
    /// 绘制自定义Inspector面板
    /// </summary>
    public override void OnInspectorGUI()
    {
        // 更新序列化对象，同步最新数据
        serializedObject.Update();

        // 1. 绘制孤立模式开关
        EditorGUILayout.PropertyField(_isolationMode);
        EditorGUILayout.Space(5);

        // 2. 绘制可重排UI列表
        _uiItemList.DoLayoutList();

        // 3. 应用序列化对象的修改
        serializedObject.ApplyModifiedProperties();

        // 4. 绘制帮助信息
        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "使用说明：\n" +
            "1. 键名需唯一，为空时自动填充默认值（UI_1/UI_2...）\n" +
            "2. 孤立模式开启时，仅允许一个UI处于激活状态\n" +
            "3. 调用 SetActivity(string key, bool visible) 控制UI显隐\n" +
            "4. 重复键名/空UI对象会在运行时触发警告并忽略",
            MessageType.Info);
    }
}
#endif
