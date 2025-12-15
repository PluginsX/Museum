# Unity2022 WebGL 3D游戏开发核心规则
## 0.基础约束
- 全程用**中文**回复（代码注释、说明均为中文）。
- 所有API严格遵循Unity2022.3.62f3c1官方文档：
  - API参考：https://docs.unity3d.com/2022.3/Documentation/ScriptReference/index.html
  - 手册：https://docs.unity3d.com/2022.3/Documentation/Manual/index.html
- 禁止编造/使用过时API，必选时标注版本兼容性。

## 1.渲染管线
- 本项目使用URP渲染管线,shader设计必须兼容URP管线，shader必须兼容WebGL。
- shader文件目录:Assets\Shader
## 2. WebGL适配
- 禁用WebGL不支持的API：`Threading`、`Sockets`、`Networking`（旧）、`dataPath`写入。
- 网络用`UnityWebRequest`，需提示CORS配置；音频需绑定用户交互触发。
- 资源异步加载（Addressables/AssetBundle），及时销毁资源防内存溢出。
- 渲染优化：LOD、减少DrawCall，避免动态批处理过载。

## 3. 代码规范
- 命名：类/方法PascalCase，变量camelCase，私有变量加下划线（如`_playableGraph`）。
- 注释：类/公共方法加XML注释，复杂逻辑加行内注释，关键API标文档链接。
- 错误处理：异步操作/资源加载加空值检查+异常捕获。

## 4. 功能约束
- 输入：优先用新Input System，适配WebGL键鼠/触摸。
- 物理：用`AddForce`而非直接改`position`，合理设碰撞层。
- Timeline：用Playable API，必加`PlayableGraph.Destroy`。
- 动画：用`Animator`，弃旧`Animation`组件。

## 5. 性能要求
- 动态对象用对象池，避免频繁实例化/销毁。
- WebGL打包配置：压缩格式、内存限制，附调试技巧。