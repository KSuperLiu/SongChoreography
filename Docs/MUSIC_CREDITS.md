# 内置测试音乐

以下曲目均由 **Kevin MacLeod (incompetech.com)** 创作。作者官网各曲目页标注采用 **Creative Commons Attribution 4.0 International（CC BY 4.0）**；本项目保留作者署名、来源链接、授权链接和修改说明。

| 曲目 | 作者标注类型 / BPM | 作者官网 |
| --- | --- | --- |
| Funkorama | Funk / 101 | https://incompetech.com/music/royalty-free/index.html?Search=Search&isrc=USUAN1100474 |
| Electrodoodle | Electronica / 120 | https://incompetech.com/music/royalty-free/index.html?Search=Search&isrc=USUAN1200079 |
| Cipher | Electronica / 150 | https://incompetech.com/music/royalty-free/index.html?Search=Search&isrc=USUAN1100844 |

每首统一署名：

> [曲目名称] — Kevin MacLeod (incompetech.com)  
> Licensed under Creative Commons: By Attribution 4.0 License  
> https://creativecommons.org/licenses/by/4.0/

修改说明：截取原曲 **15～55 秒** 的 40 秒片段，转换为 PCM WAV，并生成保持音高的 0.75 倍、0.5 倍速版本。自动检测的 BPM 与作者标注可能不同，应用显示实际分析结果并允许校正；作者标注不能替代逐拍人工验收。

作者与权利人未参与、审核或背书本应用。音乐可用不代表本项目已有真实 New Jazz 舞蹈动作；舞蹈素材的当前缺口在应用和素材文档中单独说明。

应用中可点击“音乐署名”查看来源与授权。每个内置曲目文件夹和复制产生的练习项目都包含 `CREDITS.txt`，原始音频的 SHA-256、下载地址及 ISRC 记录在 `Assets/StreamingAssets/MusicLibrary/catalog.json`。

音乐来源清单：`Samples/music-sources.json`。重新准备曲目：`python Scripts/prepare-music.py`；脚本只在开发阶段下载，应用运行时完全离线。原曲缓存位于 `Samples/MusicSources`，不会随应用打包；仅打包上述 40 秒示例及慢放音频。

授权说明：https://creativecommons.org/licenses/by/4.0/  
作者授权页：https://incompetech.com/music/royalty-free/licenses/  
核实与获取日期：2026-11-02。
