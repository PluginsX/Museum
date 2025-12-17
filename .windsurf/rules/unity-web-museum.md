---
trigger: always_on
---

# Unity2022 WebGL 3D游戏开发核心规则
## 基础约束
- 全程用**中文**回复（代码注释、说明均为中文）。
- 所有API严格遵循Unity2022.3.62f3c1官方文档：
  - API参考：https://docs.unity3d.com/2022.3/Documentation/ScriptReference/index.html
  - 手册：https://docs.unity3d.com/2022.3/Documentation/Manual/index.html
- 禁止编造/使用过时API，必选时标注版本兼容性。
- 使用项目封装的LOG系统输出调试信息，避免直接使用内置`Debug.Log`,Assets\Script\Debug\Log.cs
- 所有编辑器脚本都放在Assets\Editor目录下

## 不启动引擎直接检查编译错误的方法
- Unity 提供了一系列命令行参数，可以用于在批处理模式下执行各种操作，包括编译项目和检查错误：
- 引擎安装目录：F:\Unity\2022.3.62f3c1\Editor
- -batchmode ：以批处理模式运行 Unity，不显示图形界面
- -quit ：执行完命令后自动退出 Unity
- -logFile ：指定日志文件路径，用于输出编译错误和警告
- -projectPath ：指定 Unity 项目的路径
- -executeMethod ：执行 Unity 编辑器中的特定方法

## 渲染管线
- 本项目使用URP渲染管线,shader设计必须兼容URP管线，shader必须兼容WebGL。
- shader文件目录:Assets\Shader
## WebGL适配
- 禁用WebGL不支持的API：`Threading`、`Sockets`、`Networking`（旧）、`dataPath`写入。
- 网络用`UnityWebRequest`，需提示CORS配置；音频需绑定用户交互触发。
- 资源异步加载（Addressables/AssetBundle），及时销毁资源防内存溢出。
- 渲染优化：LOD、减少DrawCall，避免动态批处理过载。

## 代码规范
- 命名：类/方法PascalCase，变量camelCase，私有变量加下划线（如`_playableGraph`）。
- 注释：类/公共方法加XML注释，复杂逻辑加行内注释，关键API标文档链接。
- 错误处理：异步操作/资源加载加空值检查+异常捕获。

## 功能约束
- 输入：优先用新Input System，适配WebGL键鼠/触摸。
- 物理：用`AddForce`而非直接改`position`，合理设碰撞层。
- Timeline：用Playable API，必加`PlayableGraph.Destroy`。
- 动画：用`Animator`，弃旧`Animation`组件。

## 性能要求
- 动态对象用对象池，避免频繁实例化/销毁。
- WebGL打包配置：压缩格式、内存限制，附调试技巧。