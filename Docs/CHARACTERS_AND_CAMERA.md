# 角色与镜头

## 实际素材

角色来自 [Quaternius Universal Base Characters](https://quaternius.com/packs/universalbasecharacters.html) 的免费 Standard 包，通过作者 [itch.io 页面](https://quaternius.itch.io/universal-base-characters) 的公开免费下载入口取得。只使用包内明确为 CC0-1.0 的素材，未取得付费 Pro 包。原始许可与文件哈希在 `Assets/CharacterArt/Quaternius/License_Standard.txt` 和 `SOURCE.json`，构建中副本在 `ChoreoStudio_Data/StreamingAssets/CharacterCredits`。

采用 Superhero Female／Male 完整身体网格，分别为 7364／7275 个顶点，保留眼睛、眉毛、头发及骨骼蒙皮。开发时导入为 Unity Humanoid，制作训练服遮罩、女性双发髻与男性短发适配、角色预制体。脸、手指及脚保留皮肤纹理，躯干和腿使用训练服；非运行时 FBX 导入。当前播放 CMU Charleston 八拍练习改编，New Jazz 素材仍有缺口。

## 操作

| 控件 | 行为 |
| --- | --- |
| 女角色／男角色 | 更换实际蒙皮模型，保持歌曲位置与播放状态 |
| 正面、背面、左侧、右侧 | 全身定向视角 |
| 上身、脚步 | 分别聚焦上半身与下肢 |
| 自由 | 使用保存的水平角度与俯仰 |
| 左键拖动画面 | 自由旋转；启用“拖动平移”时改为平移 |
| 右键／中键拖动 | 平移 |
| 滚轮 | 拉近／拉远，有距离限制 |
| 双击画面／复位 | 恢复正面全身及默认镜头参数 |
| 右侧“镜头”页 | 视野 30～60°、距离、水平角度、俯仰、水平／垂直平移、拖动方式 |
| 镜像 | 对预览图像做水平翻转，不修改骨骼与编舞 |

未选音乐时也能使用镜头和角色。加载片段后保存项目，会记录角色、视角、镜头参数与拖动方式；旧项目会补充默认值。镜头只改变 Camera，不修改角色根节点、动画时间或 DSP 音频时钟。摄像机高度有地板限制；大幅平移或放大仍可能主动把人物移出画面，点击复位即可恢复。

## 验证入口

运行 `ChoreoStudio.exe -acceptance`，以可见窗口生成 `Builds/Windows/Acceptance/report.html`。报告包括两个角色的七种视角及镜像共 15 张实际运行截图，并检查完整蒙皮、纹理、服装区域、骨骼动画、脚部高度、镜头取景、暂停姿势、保存恢复、极限参数、实时角色切换与编辑功能。其他音频、编舞测试与实测范围见 [验证记录](VERIFICATION.md)。
