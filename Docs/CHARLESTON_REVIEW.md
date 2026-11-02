# Charleston 练习动作复查

2026-11-02，成品候选的真实动作来自 CMU Graphics Lab Motion Capture Database 的 subject 93：93_03、93_04、93_05、93_08。舞种为 Charleston。四段裁剪与四个左右变体构成八个八拍练习片段，不把它们表述为八段独立动捕，也不将其标成 New Jazz。

改编包括原地循环、对称平滑、八拍重定时、接触相位选择、稳定脚踝及适度手臂反向摆动。保留来源髋部与膝部的舞步模式；这是练习改编，不能当成原始动作的无修改复刻。

## 复查范围

- 实际 Quaternius 女性和男性 Humanoid 角色；GTX 1050 Ti、Direct3D 11。
- 全循环按实际音乐播放时间 60 Hz 采样，测量脚底蒙皮、骨骼变化与循环边界。修复后的八个动作无 180° 翻转；最大单帧旋转约 6.13°，循环位置间隙小于 1 mm，最低脚底保持在约 10 mm。
- 44 条候选有向连接 × 两角色，共 88 项，以 120 Hz 音乐时间采样。全部通过设定的突跳、切换连续性及穿地检查；最大切换间隙约 0.42 mm，最大单帧旋转约 3.35°。
- 正式库仅保留 20 条有向连接，包括八个自循环。Codex 已实际查看其男女角色共 40 张六帧连接图，检查明显翻转、穿地、肢体穿插与接触姿态；未据技术数值自动全连接。
- 这些是技术及渲染画面复查，不是舞蹈教师认证。未进行真人教学效果或专业舞种评审。

## 可追溯记录

审核清单位于 `Assets/SongChoreography/Editor/MocapData/reviewed-charleston.json`，记录准确来源、改编、审核范围与每个动作允许的后继。构建计算动作曲线、支持高度与来源元数据的 SHA-256；内容改变后必须重新复查，不能沿用旧批准。

原始结果与实际图片归档在 `TestResults/CharlestonReview/report.html`。归档脚本只从已完成复查的固定清单提取证据，不执行舞种判断或自动批准。

公开源码不提交测试输出；已有的完整图片及报告随 [Windows 发行包](https://github.com/KSuperLiu/SongChoreography/releases/latest) 提供，解压后查看 `说明与验收/CharlestonReview/report.html`。

动作来源与条件见 `Samples/MotionSources/CMU93/SOURCE.json` 和 `CREDITS.txt`。发布包提供署名、来源及修改说明；不以素材包形式转售 CMU 原始动捕数据。
