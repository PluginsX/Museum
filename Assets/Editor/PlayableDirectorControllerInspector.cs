using UnityEngine;
using UnityEditor;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Museum.Component.Animation;

[CustomEditor(typeof(PlayableDirectorController))]
public class PlayableDirectorControllerInspector : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        PlayableDirectorController control = (PlayableDirectorController)target;

        // 显示Timeline配置字段
        EditorGUILayout.LabelField("Timeline 配置", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("playableDirector"), new GUIContent("Playable Director"));

        EditorGUILayout.Space();

        // 显示ManualControl复选框
        EditorGUILayout.PropertyField(serializedObject.FindProperty("ManualControl"), new GUIContent("手动控制模式"));
        bool manualControl = control.ManualControl;

        // 当ManualControl为true时，显示CurrentRatio滑动条
        if (manualControl)
        {
            EditorGUILayout.Slider(serializedObject.FindProperty("CurrentRatio"), 0f, 1f, new GUIContent("当前时间比例"));
            
            // 如果CurrentRatio值改变，调用JumpToTimeByRatio
            if (GUI.changed)
            {
                control.JumpToTimeByRatio(control.CurrentRatio);
            }
        }
        else
        {
            // 播放控制按钮
            EditorGUILayout.LabelField("播放控制", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();

            // 从开始播放
            if (GUILayout.Button("|>", GUILayout.Width(30), GUILayout.Height(25)))
            {
                control.PlayFromStart();
            }

            // 播放/暂停切换按钮
            bool isPlaying = control.IsPlaying;
            if (GUILayout.Button(isPlaying && control.Speed > 0 ? "‖" : "▷", GUILayout.Width(30), GUILayout.Height(25)))
            {
                if (isPlaying && control.Speed > 0)
                {
                    control.Pause();
                }
                else
                {
                    control.Play();
                }
            }

            // 停止按钮
            if (GUILayout.Button("■", GUILayout.Width(30), GUILayout.Height(25)))
            {
                control.Stop();
            }

            // 反向播放
            if (GUILayout.Button(isPlaying && control.Speed < 0 ? "‖" : "◁", GUILayout.Width(30), GUILayout.Height(25)))
            {
                if (isPlaying && control.Speed < 0)
                {
                    control.Pause();
                }
                else
                {
                    control.Reverse();
                }
            }

            // 从末尾播放
            if (GUILayout.Button("<|", GUILayout.Width(30), GUILayout.Height(25)))
            {
                control.PlayFromEnd();
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space();

        // 播放器参数配置
        EditorGUILayout.LabelField("播放器设置", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("playOnAwake"), new GUIContent("唤醒时播放"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("initialTime"), new GUIContent("初始时间"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("loopType"), new GUIContent("循环类型"));

        EditorGUILayout.Space();

        // 显示当前播放次数
        var loopCountField = typeof(PlayableDirectorController).GetField("loopCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (loopCountField != null)
        {
            int loopCount = (int)loopCountField.GetValue(control);
            EditorGUILayout.LabelField($"当前播放次数: {loopCount}", EditorStyles.boldLabel);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("轨道设置（自动从Timeline读取）", EditorStyles.boldLabel);

        // 显示轨道列表
        var trackSettingsProp = serializedObject.FindProperty("trackSettings");
        if (trackSettingsProp != null && control.trackSettings != null)
        {
            for (int i = 0; i < trackSettingsProp.arraySize && i < control.trackSettings.Count; i++)
            {
                var settingProp = trackSettingsProp.GetArrayElementAtIndex(i);
                var trackProp = settingProp.FindPropertyRelative("track");
                var playCountProp = settingProp.FindPropertyRelative("playCount");

                if (trackProp.objectReferenceValue != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(trackProp.objectReferenceValue.name, GUILayout.Width(200));
                    EditorGUILayout.LabelField("循环次数", GUILayout.Width(60));
                    playCountProp.intValue = EditorGUILayout.IntField(playCountProp.intValue);
                    EditorGUILayout.LabelField("(0=无限)", GUILayout.Width(60));
                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}
