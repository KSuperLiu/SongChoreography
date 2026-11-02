# 第三方资源与修改说明

仓库中的原创代码、脚本和文档采用根目录 MIT 许可证；以下第三方素材及依赖不适用该 MIT 授权。公开源码不包含 CMU 原始 ASF／AMC、编辑器姿态数据或烘焙后的动作文件，使用准备脚本从来源获取。音乐 WAV 和便携运行环境也在开发时生成或下载；完整 Windows 应用另见 GitHub Releases。

## 音乐

Funkorama、Electrodoodle、Cipher — **Kevin MacLeod (incompetech.com)**，Creative Commons Attribution 4.0 International。

授权：https://creativecommons.org/licenses/by/4.0/

每首截取原曲 15～55 秒，转为 PCM WAV，并产生保持音高的 0.75×／0.5×版本。各曲来源 URL、原文件 SHA-256、ISRC 与署名见 `ChoreoStudio_Data/StreamingAssets/MusicLibrary/catalog.json` 和各曲目的 `CREDITS.txt`。作者未参与或背书本应用。详见 `MUSIC_CREDITS.md`。

## 角色

**Quaternius — Universal Base Characters，Standard 免费包，CC0 1.0。**

来源：https://quaternius.com/packs/universalbasecharacters.html

采用男性／女性完整角色和头发，制作训练服材质、头发颜色与适配、Unity Humanoid 配置。来源记录和许可随包保留。

## 动作

**CMU Graphics Lab Motion Capture Database — subject 93，Charleston。**

来源：https://mocap.cs.cmu.edu/

The data used in this project was obtained from mocap.cs.cmu.edu.
The database was created with funding from NSF EIA-0196217.

数据库允许用于研究和产品，禁止直接转售动作数据，包括转换数据。本应用为编舞与练习工具，不作为动捕素材包分发；ASF／AMC 是开发素材，不随应用提供运行时导入。

四段来源裁剪 93_03、93_04、93_05、93_08，加四个左右变体。修改包括重定向、裁剪、朝向、原地循环、对称平滑、八拍重定时、脚踝和手臂调整。准确舞种为 Charleston，不是 New Jazz。复查范围和校验值见 `CHARLESTON_REVIEW.md` 与 `ChoreoStudio_Data/StreamingAssets/MotionCredits`。

## 个人学习动作（仅本机准备，不随公开源码或下载分发）

**Ronghui Li 等 — FineDance，ICCV 2023。** 原作者仓库：[FineDance](https://github.com/li-ronghui/FineDance)；许可：[LICENSE](https://github.com/li-ronghui/FineDance/blob/main/LICENSE)。允许非商业科研、教育和艺术用途，禁止未经书面许可向第三方提供数据或软件。应用代码 MIT 不覆盖该数据。

个人学习工具选取 `001` 的 8 个八拍；保留原始 `Street / Jazz` 标签，作为 New Jazz／流行爵士练习改编。修改包括零姿态校准、52 关节重定向、朝向、轻度时间滤波、裁剪、水平漂移修正和接地。没有附带原歌曲、SMPL 模型或网格。源数据、转换姿态、动画、复查画面与个人版应用均保留在本机。公开仓库只包含原创获取／转换／验证代码，以及本项目编写的摘要与复查批准记录。详见 [个人学习说明](PERSONAL_LEARNING.md)。

## 分析环境

**CPython 3.13.17 Windows x64** 来自 python.org，采用 Python Software Foundation License。解释器 `LICENSE.txt` 和依赖包许可保留于 `Runtime/Python`；版本与来源见 `RUNTIME.json`，锁定依赖见 `requirements-windows.lock.txt`。

librosa、NumPy、SciPy、SoundFile、soxr、numba 等依赖各自的许可位于对应 `.dist-info` 目录，本说明不替代原许可文本。

## 运行框架

使用 Unity 2022.3.62f3，保留 UnityPlayer、Mono 运行文件。原创程序诊断动作和合成节拍夹具按 CC0 1.0 提供；诊断夹具不是舞种教学素材。资源许可不自动改变其他应用代码的许可。

0.5.1 的个人转换还读取原作者 `smplx_neu_J_1.npy` 中性关节位置，用于坐标与关节轴校准。它与动作数据遵循相同的本机使用隔离，不提交仓库或公共发行包；源码仅记录固定来源及 SHA-256。转换后对两个角色分别进行关节约束、手部简化和滤波。
