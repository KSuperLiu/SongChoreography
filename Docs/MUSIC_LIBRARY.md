# 内置跟练音乐

本批为个人舞蹈学习选择 10 首 Mixkit 免费音乐，保留原始 MP3。选择依据为曲目在官方页面中的风格及鼓、贝斯等标签；舞风匹配和逐拍拍点仍需试听校正。

| 曲名 | 作者 | 分类 | 官方标注时长 |
|---|---|---|---|
| Gimme that Groove! | Michael Ramir C. | Funk | 1:28 |
| Dirty Thinkin' | Michael Ramir C. | Jazz Funk | 1:29 |
| Funky Triplets | Michael Ramir C. | Funk / R&B | 1:49 |
| Light it Up Boy | Michael Ramir C. | Jazz Funk | 1:34 |
| Hip Hop 02 | Lily J | Hip Hop / R&B | 1:55 |
| C.B.P.D | Arulo | Hip Hop / Trap | 1:39 |
| Basketball Training | Arulo | Hip Hop / Pop | 1:52 |
| Deep Urban | Eugenio Mininni | Tech House | 4:49 |
| Cat Walk | Arulo | House / EDM | 2:04 |
| House 02 | Lily J | Deep House | 1:53 |

在 Unity 运行 Studio 后，从左侧“内置跟练音乐”滚动列表选择曲目，调整截取起点与长度，再点击“分析音乐与准备慢放”。默认起点为 0、长度为 40 秒；可用长度为 30～60 秒。分析器沿用本地运行时，生成原速、0.75× 和 0.5× 的 WAV，不改变原 MP3。自动 BPM 和拍点仍应人工校正，未复查的动作与连接仍不会进入正式编排。

## 文件与恢复

- `Generation/music-sources.json`：明确选择的十首曲目、官方源页面和下载 URL。
- `Assets/StreamingAssets/MusicLibrary/catalog.json`：供程序读取的列表，以及实际时长、SHA-256、音频格式和完整解码检查结果。
- 各曲目目录的 `CREDITS.txt` / `SOURCE.json`：来源、作者和授权链接。
- `.generation/music-downloads.json`：本次下载的可续查进度。
- `Tools/prepare_music_library.py`：下载与完整解码校验工具；使用 `Runtime/Python/python.exe -I -X utf8 Tools/prepare_music_library.py` 运行。

StreamingAssets 随构建自动复制，用户选曲和播放无需网站连接。首次分析和慢放准备仍需项目已有的本地 Python 音乐分析运行时。

## 来源

- [Mixkit Funk](https://mixkit.co/free-stock-music/funk/)
- [Mixkit Hip Hop](https://mixkit.co/free-stock-music/hip-hop/)
- [Mixkit House](https://mixkit.co/free-stock-music/house/)
- [Mixkit Stock Music Free License](https://mixkit.co/license/modal/musicFree/)，核对日期 2026-11-03。

这是个人学习选曲集合，不把音乐标为原创或无版权。保留逐曲来源记录；后续改变为公开发布或其他用途时，按具体用途重新核对许可范围。
