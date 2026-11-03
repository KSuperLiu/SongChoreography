# 发行内容与许可说明

项目原创代码及原创程序化练习伴奏按仓库 [MIT 许可](LICENSE) 提供。第三方引擎、运行时、模型与动作各自遵守其原许可，不因本仓库采用 MIT 而改变。

- **Unity 2022.3.62f3**：Windows 成品使用 Unity Player。引擎相关文件遵守 [Unity 条款](https://unity.com/legal/terms-of-service)。
- **Tripo 人物**：由项目账号通过 Tripo API 生成，用于本程序的舞蹈练习展示。来源、生成方案与校验记录保存在本机；原始 FBX 不作为公共素材包发布。参见 [Tripo API 条款](https://developers.tripo3d.ai/en/terms)。
- **Mixamo 动作**：仅作为练习程序的已编译嵌入内容使用；本程序不提供导出 FBX 或原始动作数据的功能。公开源码不包含原始 FBX 或可独立复用的动作缓存。参见 [Adobe FAQ](https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html) 与 [Adobe 社区许可 FAQ](https://community.adobe.com/questions-696/mixamo-faq-licensing-royalties-ownership-eula-and-tos-589400?lang=en)。
- **Mixkit 本机录音**：个人学习目录中的十首 MP3 不随公开 Windows 发行包分发，也不进入 Git 源码。参见 [Stock Music Free License](https://mixkit.co/license/modal/musicFree/)。公开包的内置列表只有原创 100 BPM 练习伴奏，仍可选择自己获得授权的 WAV/MP3。
- **便携 Python 与分析依赖**：发行包中的 `Runtime/Python` 保留 Python 原许可、各依赖的 `dist-info`/许可文件及版本清单。Python 使用 [PSF License](https://docs.python.org/3/license.html)，NumPy、SciPy、librosa、numba、soundfile 等依赖保留各自许可。发行包不在运行时下载或安装这些依赖。
- **Unity Skills**：仅为开发用 Unity 编辑器包，源码清单引用其上游仓库；不属于程序的运行时 API 服务。参见 [上游仓库](https://github.com/Besty0728/Unity-Skills)。

`BUILD.json` 标记构建版本、时间、素材数量、有效复查动作与连接数量。发行附件的 `SHA256SUMS.txt` 用于校验下载的 ZIP。API Key、`.env`、个人任务历史、编辑器缓存和本机恢复快照均不属于发行内容。
