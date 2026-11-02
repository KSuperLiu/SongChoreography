# New Jazz 素材状态与接入

## 当前状态

- 完整蒙皮角色 2 个：Quaternius Universal Base Characters 免费 Standard 包的男性／女性模型，已配置有效 Unity Humanoid Avatar，制作训练服材质与发型；详情见 [角色来源与镜头](CHARACTERS_AND_CAMERA.md)。运行时已不再使用旧 `DiagnosticAvatar` 几何体人形。
- 原创诊断动作 8 个：由 `PrototypeBuilder` 在实际角色上烘焙 Animator muscle 曲线，用于测试重定向、节拍采样、混合与编辑。
- **个人学习版：8 个真人八拍，20 条经过复查的连接。** 来自 FineDance `001`，原作者标签为 `Street / Jazz`；经动作画面复查选作 New Jazz／流行爵士练习改编，不能声称作者标签就是 New Jazz。52 关节四元数轨道适配两个内置角色。许可仅供本机非商业学习，动作、转换数据、复查图片及含动作的应用不公开分发。见[个人版准备](PERSONAL_LEARNING.md)和[验证记录](NEW_JAZZ_050_VERIFICATION.md)。
- **公共版 New Jazz：0 个。** 可分发演示库仍为准确标注的 Charleston：四段 CMU 动捕裁剪及四个左右变体，共八个八拍片段，20 条有向连接。详见 [动作复查](CHARLESTON_REVIEW.md)。两套库的技术与 Codex 画面复查均不代表舞蹈教师认证。
- 角色源素材按作者 Standard 包内的 CC0-1.0 许可使用。原创诊断动画和合成节拍音轨按 CC0-1.0 提供；内置三首真实音乐使用独立的 CC BY 4.0 授权，其他代码不因此自动改变许可。

## 候选来源核实（2026-11-02）

Adobe 的 [Mixamo FAQ](https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html) 说明可用 Adobe ID 免费使用，角色和动画可用于项目，同时注明中国地区账户等限制。当前没有登录 Mixamo 或下载其素材，也没有通过实际动作观看确认 New Jazz 风格。未采用第三方镜像素材包或将传统爵士直接当作 New Jazz。

补充检索后，FineDance 提供了可用于非商业教育的真人 Street/Jazz 动捕，已用于本机个人学习适配。其许可禁止未经授权向第三方提供；尚未取得可随公共应用免费分发的 New Jazz 库。AIST、Ready Player Me、Reallusion 等候选的限制见[个人版来源核对](PERSONAL_LEARNING.md)。

## 开发阶段导入

1. 取得有明确使用许可的 New Jazz 原始动作与人形角色；记录创作者、来源 URL、许可、获取日期、是否允许随应用发布。素材用于本地项目不代表可以将原始素材作为素材包分发。
2. Unity 导入 FBX，Rig 设为 Humanoid，配置并检查 Avatar；根节点朝向统一为 +Z，上方向 +Y，地面 Y=0。提取原地动作，处理移动与转身，避免角色漂出练习区。
3. 以完整 4/8 拍裁剪片段，移除多余前后空白；将提取的 `AnimationClip` 放入 `Assets/Resources/Motions`。四拍片段在八拍槽内重复时，要明确审核自连接。
4. 在 `Assets/Resources/MotionCatalog.json` 添加元数据，`style: "new-jazz"`、`difficulty: 1`、`beats: 4` 或 `8`；填写唯一 id、中文名称、Resources 路径（不含扩展名）、强弱 0～1、来源、许可。
5. 逐个组合验证前后片段的身体朝向、重心、支持脚、接地、根位移和节奏。只有通过人工观看的组合写入前一个动作的 `safeNext`；不要全连接。
6. 经舞蹈风格、难度和连接审核后填写 `reviewer`、`reviewNotes`，并设置 `approved: true`。`reviewNotes` 应说明支持脚、衔接与节奏检查结果；字段存在不代表审核已被自动完成。
7. 先连接两个动作，再组成 30～60 秒序列，检查突跳、滑脚、穿插与可跟学性；至少八个有效片段及足够连接形成完整序列后，New Jazz 入口才会启用。
8. 执行 `Tools > Song Choreography > Validate Motion Assets`，再构建并进行真人跟学验收。程序检查版本、引用及 Humanoid 兼容性，无法代替专业舞蹈审核。

JSON 示例（仅字段示例，不可作为已审核素材提交）：

```json
{
  "id": "new-jazz-001",
  "name": "实际动作名称",
  "resource": "Motions/ActualClip",
  "style": "new-jazz",
  "beats": 8,
  "difficulty": 1,
  "energy": 0.5,
  "approved": false,
  "source": "填写实际创作者与来源",
  "license": "填写实际许可",
  "reviewer": "",
  "reviewNotes": "",
  "safeNext": []
}
```

## 0.5.1 关节验收

动作批准 schema 2 必须绑定当前曲线、男女角色轨道及过渡参数。旧 schema 1 不可继承。构建前强制进行 120 Hz 单动作范围和肘膝、指节弯曲方向检查；正式使用的连接需在实际 DancePlayback 中复查。共享骨骼名称不等于共享局部关节轴。方法与限制见 [反关节修复验证](ANATOMY_051_VERIFICATION.md)。
