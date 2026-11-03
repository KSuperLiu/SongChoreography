# Mixamo 本地素材池

已完成下载的搜索范围是 `hip hop` 的 186 个结果；这是搜索结果清单，包含其他舞种、动作包和日常动作。

| 项目 | 数量 |
|---|---:|
| 单独 FBX 结果 | 183 |
| 动作包 | 3 |
| 包内 FBX | 60 |
| 本地 FBX 合计 | 243 |
| 文件总大小（解包后） | 192.7 MiB |
| 哈希不匹配或缺失文件 | 0 |

原有三段已保留。所有下载结果的来源记录在本机 `.generation/mixamo-batch.json`，文件检查记录在 `.generation/mixamo-verification.json`。243 个 FBX 均有动画曲线，检查到的帧率设置为 30 FPS，未包含网格或 Skin deformer。

来源标题和描述中标记 Hip Hop 的文件有 32 个，Jazz 有 5 个；其余还包括 Breakdance、Salsa、Samba、Swing 等。姿势、走跑、手势、测试及未分类条目共 71 个。分类是来源元数据整理，不是艺术质量评定；Jazz 不等同于 New Jazz。动作包也可能与单条下载在动作内容上重叠，文件哈希不同不代表动作必然不同。

批量登记命令：

```powershell
Runtime/Python/python.exe -I -X utf8 Tools/prepare_mixamo_library.py
```

脚本先校验文件路径、文件头和哈希，再登记到 `Generation/library.json`。重跑不会重复登记，也不会改写已有的角色、动作复查和拍点记录。首次登记前的清单保存在本机 `.generation/library-before-mixamo-registration.json`。

在 Unity 中执行 `Tools > CHOREO AI > Import generated assets`，然后打开 `Generated asset workbench`。导入成功的片段仍为待复查候选；BPM 120 和全片均分拍点只用于初始预览。需要裁剪真正的一组八拍、校正拍点、复查目标人物关节和接地，再复查需要的有向连接，才能参与编排。

后续验证直接使用 Unity 编辑器；交付独立 Windows 程序时再构建。

当前已在 Tripo 人物上复查 8 种动作及 10 条有向连接。运行时通过“动作库设置”浏览全部 243 段，按分类勾选下次编排的范围，或使用 New Jazz 基础练习推荐。详见 [扩充与设置记录](NEW_JAZZ_EXPANSION_REVIEW.md)。

后续已修复 Snake 手部穿模并重新验收，当前为 **8 种可编排动作、10 条连接**。243 段完整原片已完成技术初筛，其中 66 段轨迹变化较大的候选标为待修复；全部候选保留预览。详见 [手臂修复与 Unity 验证](ARM_REPAIR_REVIEW.md)。
