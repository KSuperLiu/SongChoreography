# 个人 New Jazz / 街爵士学习素材

源码 0.5.0 增加本机个人动作库；0.5.1 修正了旧重定向的反关节错误。公开仓库与公开发行包保留 Charleston 演示；受限制的学习动作、原音轨、转换姿态、动画、复查图及包含它们的个人版应用均不上传 GitHub。

## 使用已准备的个人版

双击 `Builds/PersonalWindows-0.5.2/ChoreoStudio.exe`，点击“一键演示”，或点击内置曲目的“使用”，即可观看 New Jazz 八拍练习组合。初学建议选择 0.5×／0.75×，使用背面或镜像跟学；舞台下方提供正面、背面、左右侧、上身、脚步和自由镜头，右侧“镜头”页可调整视角与构图。鼠标拖动旋转、右键平移、滚轮缩放、双击复位。

也可从仓库运行 `.\Scripts\run.ps1 -PersonalLearning -Demo`，直接载入暂停的练习组合。如果启动时恢复了旧 Charleston 项目，点击“一键演示”即可载入个人 New Jazz 组合；原项目仍保存在本机。

右侧片段可点击定位、锁定与替换；“换一版”保留锁定片段。导入自己的 WAV／MP3 后截取 30～60 秒，分析完成可在“节拍”页校正 BPM、首拍和八拍起点。项目在本机保存，恢复时暂停，防止突然播放音乐。当前动作难度没有经过教师评级，可慢速观察后自行选择适合的动作。

本目录中的个人应用含受限素材，保留在本机使用。公开分享请使用 GitHub 源码或不含个人素材的公共版。

## 素材选择与使用范围

[FineDance 原作者仓库](https://github.com/li-ronghui/FineDance)提供真人全身、手指动捕及原始舞种标签。[原始许可证](https://github.com/li-ronghui/FineDance/blob/main/LICENSE)列出非商业科研、教育和艺术用途，并禁止未经书面许可向第三方提供数据或软件。这里按用户的本机个人学习用途使用，不能据此将动作或包含动作的应用公开分发。应用代码的 MIT 许可不覆盖数据。

研究来源：Ronghui Li 等，*FineDance: A Fine-grained Choreography Dataset for 3D Full Body Dance Generation*，ICCV 2023，[项目与论文](https://li-ronghui.github.io/finedance.html)。

原始标签保持 `Street / Jazz`；本机练习版本使用作者的中性骨架校准关节轴，转换坐标手性，分别适配男女角色，再进行关节约束、手部简化、滤波、朝向调整、八拍裁剪、水平漂移修正和实际脚底接地。展示名为 New Jazz 练习改编，不把原作者的细分标签写成 New Jazz，也不声称通过舞蹈教师认证。

其他来源也进行了核对：[AIST Dance DB](https://aistdancedb.ongaaccel.jp/terms_of_use/)限学术研究并禁止未经授权的再分发；[Ready Player Me 动作](https://github.com/readyplayerme/animation-library/blob/master/LICENSE.md)限其角色；[Reallusion Girl Style](https://www.reallusion.com/contentstore/pack/iclone/3d-animation/street-dance-girl-style)明确含 New Jazz，但免费入口为试用；[Meshy Jazz Danc](https://www.meshy.ai/animation-library/dancing/dancing/jazz-danc)属于 Pro 动画。未从转载素材包取得这些资源。

## 本地准备

先按 README 准备公共角色、音乐、CMU 演示和便携 Python。使用者须阅读并遵守原作者许可；可用下列工具直接从原作者公开原包读取约 3.2 MB，而不必下载全部 4.4 GB。需要本机安装 7-Zip；工具校验原包片段、动作、标签和许可的 SHA-256，不接收转载数据。

```powershell
# 仅在原许可允许的个人非商业学习用途下运行；源码不附带这些数据。
.\Runtime\Python\python.exe -I Scripts\fetch-personal-finedance.py --personal-use
# 也可从自己取得的数据中指定一个 motion/*.npy 和对应 label_json/*.json。
# BPM 与起点应按对应原音轨核实；此命令不会自动判断舞种或批准动作。
.\Runtime\Python\python.exe -I Scripts\prepare-personal-finedance.py `
  --source PersonalAssets\FineDance\source\001.npy `
  --label PersonalAssets\FineDance\source\001.json `
  --bpm 114.98539850866635 --start 24.2121139914522 --personal-use

.\Scripts\build.ps1 -Isolated -PersonalLearning
```

输出在 `Builds/PersonalWindows-0.5.2`，包含便携分析引擎。源码中的技术复查清单只保存本项目编写的摘要、名称与连接批准记录，不含动作数组或复查画面。精确匹配已复查数据和烘焙结果时可继承该记录；其他候选最初均未批准，不能出现在普通 New Jazz 生成入口，需完成逐动作及连接复查，并保存本地 `reviewed.json`。更换数据、裁剪或重定向后旧批准会失效，失败的重新烘焙也会清除旧批准状态。

动作数据不会在应用运行时导入。当前角色专用关节轨道只针对已验证的两个内置角色，两者虽共用骨骼名称，但局部轴不同，必须分别烘焙，不能交叉应用。更换角色须重新适配和验收。公共 Charleston 动作继续使用 Humanoid Playables。个人轨道使用同一 DSP 歌曲时钟，通过四元数插值和实际脚底接地播放；原本连续的片段保留源运动，其他连接只使用复查通过的过渡。

## 验证与发布隔离

```powershell
dotnet run --project Tests/CoreTests.csproj -p:UseSharedCompilation=false
.\Runtime\Python\python.exe -I -m unittest discover -s Tests -p 'test_*.py' -v
.\Runtime\Python\python.exe -I Scripts\verify-distribution.py --git-index
.\Runtime\Python\python.exe -I Scripts\verify-distribution.py --build Builds\PersonalWindows-0.5.2 --personal

# 此构建会排除个人数据，单独生成公共版本。
.\Scripts\build.ps1 -Isolated -OutputDirectory Builds\PublicWindows
.\Runtime\Python\python.exe -I Scripts\verify-distribution.py --build Builds\PublicWindows
```

`PersonalAssets`、个人动作目录与文件已加入 Git 忽略；公开隔离构建排除这些输入。普通 Unity 公开构建发现个人素材会中止。构建写入 `DISTRIBUTION.json`，公开打包器要求分发记录，并拒绝个人使用标记或个人署名目录。

本机测试输出在 `Builds/…/MotionPreview`、`MotionPreview-male`、`TransitionReview`、`ProductAcceptance` 和 `Acceptance`。它们含受限动作的画面，也应保留在本机。技术阈值不能代替逐图检查舞风、穿插、滑脚与跟学难度。

0.5.1 必须重新获取作者中性骨架（获取工具会一并下载、校验 SHA-256，保存在忽略目录），并重新转换、烘焙。旧 schema 1 批准记录不再生效；新 schema 2 绑定男女两套轨道及过渡参数。构建强制执行 120 Hz 全片段关节范围及肘膝、指节弯曲方向检查。复测说明见 [关节修复验证](ANATOMY_051_VERIFICATION.md)。
