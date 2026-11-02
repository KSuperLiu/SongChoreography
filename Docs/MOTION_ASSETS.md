# New Jazz 素材状态与接入

## 当前状态

- 完整蒙皮角色 2 个：Quaternius Universal Base Characters 免费 Standard 包的男性／女性模型，已配置有效 Unity Humanoid Avatar，制作训练服材质与发型；详情见 [角色来源与镜头](CHARACTERS_AND_CAMERA.md)。运行时已不再使用旧 `DiagnosticAvatar` 几何体人形。
- 原创诊断动作 8 个：由 `PrototypeBuilder` 在实际角色上烘焙 Animator muscle 曲线，用于测试重定向、节拍采样、混合与编辑。
- **真实 New Jazz 动作：0 个。** 当前禁用 New Jazz 生成。可展示的正式库为准确标注的 Charleston 练习改编：四段 CMU 动捕裁剪及四个左右变体，共八个八拍片段，正式使用 20 条经过技术与 Codex 画面复查的有向连接。详见 [动作复查](CHARLESTON_REVIEW.md)，不代表舞蹈教师认证。
- 角色源素材按作者 Standard 包内的 CC0-1.0 许可使用。原创诊断动画和合成节拍音轨按 CC0-1.0 提供；内置三首真实音乐使用独立的 CC BY 4.0 授权，其他代码不因此自动改变许可。

## 候选来源核实（2026-11-02）

Adobe 的 [Mixamo FAQ](https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html) 说明可用 Adobe ID 免费使用，角色和动画可用于项目，同时注明中国地区账户等限制。当前没有登录 Mixamo 或下载其素材，也没有通过实际动作观看确认 New Jazz 风格。未采用第三方镜像素材包或将传统爵士直接当作 New Jazz。

搜索结果未提供已核实且满足本项目需要的免费 New Jazz 素材。此结论仅代表本次没有取得可用素材，不代表不存在。真实素材下载、来源与许可记录及动作审核仍待完成。

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
