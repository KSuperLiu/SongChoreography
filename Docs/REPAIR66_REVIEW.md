本轮继续处理原来的 66 段待适配动作，直接在 Unity 2022.3.62f3 中验证。没有重新生成或替换原始 FBX。

完整原片结果：**53 / 66 段通过本轮技术指标，13 段仍有连续性风险并保留待修复标记。** 66 段的实际蒙皮抽样均未检出超过 2 毫米的手部/前臂穿入。未把这 13 段当作已修好动作放行。

修正轨迹同时保存手、肘位置和骨骼旋转，保留前臂扭转。通过居中平滑与碰撞投影生成连续轨迹；对旋转、地板支撑等 21 段另外在相对髋部的空间平滑，以保留手臂对躯干的反向运动。3 段效果较差的新轨迹已恢复到前一版较好的已测轨迹。运行时采用快速保守身体包络，独立复查使用实际蒙皮网格。原来的 8 段推荐八拍曲线指纹完全相同。

新增 New Jazz 基础练习素材如下，原始风格分类保留：

| 动作 | 已复查源时间（秒） | 用途 |
|---|---|---|
| 侧滑与手臂开合 | 0.17–3.63 | 横向重心与手臂开合 |
| 踢步与重心切换 | 0.33–4.00 | 踢步、提膝与重心 |
| 流动 Wave 组合 | 1.20–7.17 | 手臂波浪与躯干连接 |
| Boogaloo 肩胯律动 | 8.00–12.13 | 肩胯隔离与开合 |

推荐库 **8 → 12 段**，共 15 条已复查编排连接。打开“动作库设置 → New Jazz 推荐”即可预览；“使用 New Jazz 推荐 → 保存选择并返回”可选择这 12 段。原选择恰好为旧 8 段的本机设置已扩展到 12 段；其他自定义子集保持原样。

| 验证 | 结果 |
|---|---|
| 完整原片 | 66 段、19730 个 30 Hz 运动学样本；实际蒙皮每 3 帧检查一次，顶点步长 4 |
| 新增四段 | 源时间 30 Hz 共 523 帧，100 BPM 映射 60 Hz 共 1156 帧；手部/前臂高权重蒙皮点全查，穿入失败 0，新增急摆指标 0 |
| 连接与预览循环 | 60 Hz，19 项、1629 帧；穿入失败 0，新增急摆指标 0 |
| 实际 Unity Play Mode | 38.4 秒原创 100 BPM，8 段/8 种，覆盖全部四段新增动作；0.75×、0.5×、区间循环与跨段循环；240 次实时检查、72240 点，穿入失败 0，错误 0 |
| 循环交接 | 6 次回绕、6 次过渡结束；最大回绕帧手位移 2.24 厘米，最大交接帧位移 0.11 厘米 |
| 默认入口 | 原生 seed 42，8 种动作，包含 3 段新增动作；第二轮完整 Play Mode 验证错误 0 |
| 动作库设置 | 243 段可预览，12 段当前人物已复查推荐；8 项界面/保存/生成筛选检查通过 |

仍待修复的 13 段完整源片段如下；它们未进入推荐库，当前仍标记“轨迹待修复”。连续性指标是复查风险提示，不能据此断言每一项都是可见的瞬时跳变。

| 源动作 | 连续性异常采样帧 | ID |
|---|---:|---|
| breakdance footwork 2 | 1 | `mixamo-pack-breakdance-breakdance-footwork-2-ea7c3c04` |
| breakdance footwork 3 | 2 | `mixamo-pack-breakdance-breakdance-footwork-3-a9acfe07` |
| breakdance freezes | 1 | `mixamo-pack-breakdance-breakdance-freezes-a2dfbb54` |
| breakdance freeze var 3 | 1 | `mixamo-pack-breakdance-breakdance-freeze-var-3-fbda7bcc` |
| breakdance uprock to ground | 2 | `mixamo-pack-breakdance-breakdance-uprock-to-grou-d2985cfc` |
| Breakdance Freeze Combo | 1 | `mixamo-breakdance-freezes-121860901` |
| Breakdance Ground Footwork Var 3 | 2 | `mixamo-breakdance-footwork-3-121820901` |
| Breakdance Headspin End | 6 | `mixamo-headspin-end-116400903` |
| Breakdance Ground Footwork Var 2 | 1 | `mixamo-breakdance-footwork-2-121810901` |
| Breakdance Arch Freeze Var 3 | 1 | `mixamo-breakdance-freeze-var-3-121890901` |
| Northern Soul Dance Spin Dip And Splits | 1 | `mixamo-northern-soul-spin-combo-121420901` |
| Breakdance Uprock Transition To Ground Moves | 2 | `mixamo-breakdance-uprock-to-ground-122080901` |
| Northern Soul Dance Spin And Floor Work | 4 | `mixamo-northern-soul-floor-combo-121430901` |

范围说明：上述结论来自指定源时间、100 BPM 映射、已复查连接与实际播放采样，不等于对所有连续时刻或任意自定义连接的数学保证。新增素材作为 New Jazz 基础练习使用。

完整证据：[REPAIR66_VERIFICATION.json](ReleaseValidation/REPAIR66_VERIFICATION.json)。运行截图：[Repair66Live.png](Repair66Live.png)。正面/侧面八拍预览：[正面](Repair66NewJazz-front.jpg)、[侧面](Repair66NewJazz-side.jpg)。
