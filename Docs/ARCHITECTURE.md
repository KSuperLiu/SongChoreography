# Tripo 人物与 Humanoid 动作库架构

## 数据流

`Generation plan → provider tasks → FBX + provenance → Unity Humanoid import → per-character motion/beat review → directed transition review → music-aligned composition → local playback`

现成动作库路径：`Mixamo / 其他授权 FBX → Import motion FBX → 同一 Humanoid 导入与复查流程`。本机重定向，无需上传 Tripo 人物；来源网页、条款、真实舞种、文件哈希和 Clip 名一起登记。已有素材不被覆盖，未复查动作不能进入编排。

人物独立于动作生成；所有源文件采用 SHA-256 校验。Unity 导入配置依赖哈希参与素材签名；拍点、裁剪变化参与动作签名。人物与动作的组合复查绑定两者签名，连接复查同时绑定人物、前后动作签名。没有通过复查的内容可预览但不可用于普通编排。

## 服务边界

Tripo 文生 3D → rig-check → 人形绑定（Unity 命名、FBX）。fal Hunyuan Motion 通过队列生成 FBX。当前 fal 接口使用文字、时长和种子，没有音乐或逐拍输入。请求时长按 `beats * 60 / bpm` 计算，只是初始长度，仍需逐拍核实。

所有 POST 在发送前写入 submitting；没有任务编号的未知提交不自动重试。可以关联控制台查得的 task/request ID。下载 CDN 不携带 API Key，fal 返回的轮询 URL 必须属于 queue.fal.run。Key 仅在本机 .env / 进程环境，日志和任务元数据不保存 Key。

## 编排与播放

编排读取复查后的八拍动作和有向连接，支持任意库存规模。先精确判断未来锁定片段的可达性，再用有界候选搜索比较强弱与最近四槽重复成本。失败不改写原项目。

音频 DSP 时钟驱动动作；动作时间从人工确认的拍点插值得到，最后一拍保持结束姿势。所有模型经过同一 Humanoid PlayableGraph 重定向，不再有 FineDance/CMU 分支或硬编码 52 关节数组。接地只修正网格穿地，不把腾空动作强制贴地。慢放使用离线准备的 0.75× / 0.5× 音频。

## 尚待真实素材验证

- Tripo 半写实人物的脸、手、贴图、蒙皮和关节变形。
- Hunyuan Motion 候选的 New Jazz 风格、手指完整性、脚底稳定性及踩拍。
- 源 FBX 是否可被 Unity 自动识别人形；必要时在 Rig / Configure 手动映射。
- 大量高面数角色的实际性能；当前没有对未生成角色宣称帧率。
- 镜像/不同速度下的教学适用性不等同于自动技术检查。

第一轮应仅生成一人物和少量动作，验证导入、复查和播放，再扩大候选规模。
