using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class Texture2DArrayCreator : EditorWindow
{
    private List<Texture2D> textureList = new List<Texture2D>();
    private string outputPath = "Assets";
    private string fileName = "TEX2_";
    private Vector2 scrollPosition;
    private StringInputWindow inputWindow; // 输入窗口引用

    // 保存上一次的输出路径
    private const string LAST_OUTPUT_PATH_KEY = "Texture2DArrayCreator_LastOutputPath";

    [MenuItem("Assets/LNU数字艺术实验室/纹理数组生成器", false, -100)]
    [MenuItem("Window/LNU数字艺术实验室/纹理数组生成器", false, -100)]
    public static void ShowWindow()
    {
        GetWindow<Texture2DArrayCreator>("纹理数组生成器");
    }

    private void OnEnable()
    {
        // 加载上一次的输出路径
        if (EditorPrefs.HasKey(LAST_OUTPUT_PATH_KEY))
        {
            outputPath = EditorPrefs.GetString(LAST_OUTPUT_PATH_KEY);
        }

        // 检查选中的资产并添加到列表
        AddSelectedTextures();
    }

    private void AddSelectedTextures()
    {
        Object[] selectedObjects = Selection.objects;
        foreach (Object obj in selectedObjects)
        {
            //if (obj is Texture2D texture && !textureList.Contains(texture)) 是否允许重复
            if (obj is Texture2D texture)
            {
                textureList.Add(texture);
            }
        }
    }

    private void OnGUI()
    {
        // 检查输入窗口是否已关闭
        if (inputWindow != null && !inputWindow)
        {
            string result = inputWindow.inputText;
            inputWindow = null;
            
            // 如果是从ValidateOutputPath调用的，处理结果
            if (string.IsNullOrEmpty(fileName) || fileName == "TEX2_")
            {
                if (!string.IsNullOrEmpty(result))
                {
                    fileName = result;
                    // 重新尝试生成
                    GenerateTexture2DArray();
                }
            }
        }

        GUILayout.Label("纹理列表", EditorStyles.boldLabel);
        
        // 拖拽区域
        Rect dragArea = GUILayoutUtility.GetRect(0, 100, GUILayout.ExpandWidth(true));
        GUI.Box(dragArea, "", EditorStyles.helpBox); // 绘制一个灰色背景框

        // 准备提示文字样式
        GUIStyle dragTextStyle = new GUIStyle(EditorStyles.label);
        dragTextStyle.alignment = TextAnchor.MiddleCenter; // 文字居中
        dragTextStyle.fontSize = 14; // 增大字体
        dragTextStyle.normal.textColor = Color.white; // 文字颜色设为白色，与黑色背景对比

        // 实时显示已添加的纹理数量
        string dragText = textureList.Count == 0 
            ? "拖拽Texture2D文件到此处(支持多选)" 
            : $"已添加 {textureList.Count} 个纹理(可继续拖拽添加)";

        // 在拖拽区域中心绘制文字
        GUI.Label(dragArea, dragText, dragTextStyle);

        // 处理拖拽事件
        HandleDragAndDrop(dragArea);

        // 纹理列表
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        for (int i = 0; i < textureList.Count; i++)
        {
            // 每一项顶部添加间距（除了第一项）
            if (i > 0)
                GUILayout.Space(8);
            
            EditorGUILayout.BeginHorizontal();

            // 显示序号（从0开始）
            GUILayout.Label((i).ToString(), GUILayout.Width(20), GUILayout.Height(64));
            
            // 显示纹理预览
            GUILayout.Label(textureList[i], GUILayout.Width(64), GUILayout.Height(64));
            
            // 显示纹理名称和引用
            textureList[i] = (Texture2D)EditorGUILayout.ObjectField(textureList[i], typeof(Texture2D), false);
            
            // 删除按钮
            if (GUILayout.Button("X", GUILayout.Width(24)))
            {
                textureList.RemoveAt(i);
                i--; // 调整索引，因为列表已更改
            }

            EditorGUILayout.EndHorizontal();

            // 每一项底部添加间距（除了最后一项）
            if (i < textureList.Count - 1)
                GUILayout.Space(8);

        }
        EditorGUILayout.EndScrollView();

        // 添加和清空按钮
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("将选中资源加入列表"))
        {
            AddSelectedTextures();
        }
        if (GUILayout.Button("清空列表"))
        {
            textureList.Clear();
        }
        EditorGUILayout.EndHorizontal();

        // 输出路径选择
        EditorGUILayout.Space();
        GUILayout.Label("输出设置", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("输出位置:", GUILayout.Width(80));
        outputPath = EditorGUILayout.TextField(outputPath);
        if (GUILayout.Button("选择", GUILayout.Width(60)))
        {
            string selectedPath = EditorUtility.OpenFolderPanel("选择输出位置", outputPath, "");
            if (!string.IsNullOrEmpty(selectedPath) && selectedPath.Contains(Application.dataPath))
            {
                outputPath = "Assets" + selectedPath.Substring(Application.dataPath.Length);
                EditorPrefs.SetString(LAST_OUTPUT_PATH_KEY, outputPath);
            }
        }
        EditorGUILayout.EndHorizontal();

        // 文件名输入
        fileName = EditorGUILayout.TextField("文件名:", fileName);

        // 生成按钮
        EditorGUILayout.Space();
        if (GUILayout.Button("生成Texture2D数组", GUILayout.Height(30)))
        {
            GenerateTexture2DArray();
        }
    }

    private void HandleDragAndDrop(Rect dragArea)
    {
        Event evt = Event.current;
        if (dragArea.Contains(evt.mousePosition))
        {
            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    
                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        
                        foreach (Object draggedObject in DragAndDrop.objectReferences)
                        {
                            if (draggedObject is Texture2D texture && !textureList.Contains(texture))
                            {
                                textureList.Add(texture);
                            }
                        }
                    }
                    evt.Use();
                    break;
            }
        }
    }

    private void GenerateTexture2DArray()
    {
        // 如果输入窗口正在显示，不执行任何操作
        if (inputWindow != null) return;

        // 验证纹理列表
        if (textureList.Count == 0)
        {
            EditorUtility.DisplayDialog("错误操作!", "请在列表中添加至少一个Texture2D!", "我知道了~");
            return;
        }

        // 验证所有纹理具有相同的尺寸和格式
        Texture2D firstTexture = textureList[0];
        foreach (Texture2D tex in textureList)
        {
            if (tex.width != firstTexture.width || tex.height != firstTexture.height)
            {
                EditorUtility.DisplayDialog("错误操作!", "所有纹理必须具有相同的尺寸!", "我知道了~");
                return;
            }
            
            if (tex.format != firstTexture.format)
            {
                EditorUtility.DisplayDialog("错误操作!", "所有纹理必须具有相同的格式!", "我知道了~");
                return;
            }
        }

        // 验证输出路径
        if (!ValidateOutputPath())
        {
            return;
        }

        // 确保路径存在
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        // 确保文件名有效
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = "TEX2_";
        }

        // 检查是否存在同名文件
        string fullPath = Path.Combine(outputPath, fileName + ".asset");
        if (File.Exists(fullPath))
        {
            // 弹出确认对话框
            bool overwrite = EditorUtility.DisplayDialog(
                "File Exists", 
                $"文件 '{fileName}.asset' 已存在！你确定要覆盖它吗？", 
                "覆盖", 
                "取消"
            );

            // 如果用户选择取消，则终止操作
            if (!overwrite)
            {
                return;
            }
        }

        // 创建Texture2DArray
        Texture2DArray textureArray = new Texture2DArray(
            firstTexture.width, 
            firstTexture.height, 
            textureList.Count, 
            firstTexture.format, 
            firstTexture.mipmapCount > 1
        );
        
        // 设置纹理数组的属性
        textureArray.anisoLevel = firstTexture.anisoLevel;
        textureArray.filterMode = firstTexture.filterMode;
        textureArray.wrapMode = firstTexture.wrapMode;

        // 将纹理复制到数组中
        for (int i = 0; i < textureList.Count; i++)
        {
            Graphics.CopyTexture(textureList[i], 0, textureArray, i);
        }

        // 保存资产（如果文件已存在会自动覆盖）
        AssetDatabase.CreateAsset(textureArray, fullPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("操作成功", $"Texture2D数组创建成功!位置:\n{fullPath}", "OK");
        
        // 选中新创建的资产
        Object newAsset = AssetDatabase.LoadAssetAtPath<Object>(fullPath);
        Selection.activeObject = newAsset;
    }

    private bool ValidateOutputPath()
    {
        bool pathValid = !string.IsNullOrEmpty(outputPath) && Directory.Exists(outputPath);
        
        if (!pathValid)
        {
            string selectedPath = EditorUtility.OpenFolderPanel("选择输出文件夹", "Assets", "");
            if (string.IsNullOrEmpty(selectedPath) || !selectedPath.Contains(Application.dataPath))
            {
                EditorUtility.DisplayDialog("错误!", "选择的输出路径无效!", "OK");
                return false;
            }
            
            outputPath = "Assets" + selectedPath.Substring(Application.dataPath.Length);
            EditorPrefs.SetString(LAST_OUTPUT_PATH_KEY, outputPath);
            
            // 如果文件名为默认值或空，使用自定义输入框获取文件名
            if (fileName == "TEX2_" || string.IsNullOrEmpty(fileName))
            {
                ShowInputDialog("输入文件名", "请输入Texture2D资源的名称:", "TEX2_");
                return false; // 暂时返回false，等待输入完成后再继续
            }
        }
        
        return true;
    }
    
    // 显示自定义输入框对话框
    private void ShowInputDialog(string title, string message, string defaultValue)
    {
        inputWindow = ScriptableObject.CreateInstance<StringInputWindow>();
        inputWindow.position = new Rect(Screen.width / 2 - 150, Screen.height / 2 - 50, 300, 100);
        inputWindow.titleContent = new GUIContent(title);
        inputWindow.message = message;
        inputWindow.inputText = defaultValue;
        inputWindow.ShowPopup();
    }
    
    // 用于获取字符串输入的临时窗口
    private class StringInputWindow : EditorWindow
    {
        public string message;
        public string inputText;
        
        private void OnGUI()
        {
            GUILayout.Label(message);
            inputText = EditorGUILayout.TextField(inputText);
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("确定"))
            {
                Close();
            }
            if (GUILayout.Button("取消"))
            {
                inputText = null;
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    // 当选择改变时检查是否需要添加纹理
    private void OnSelectionChange()
    {
        Repaint();
    }
}