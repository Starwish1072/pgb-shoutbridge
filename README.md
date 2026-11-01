# 批改邦喊话桥接 · ClassIsland 插件

把「批改邦」教师端的**教室远程喊话**接到 **ClassIsland 2.1**：老师一喊话，教室大屏上的岛会飞下来变成一张液态玻璃悬浮卡片，带涟漪特效、提示音和语音播报。

> 不弹批改邦窗口、不打断课堂，全自动。

## 功能

- **喊话接收**：批改邦页面内的 hook 拦截喊话请求，转发到本地桥接服务（默认 `127.0.0.1:18123`）
- **飞行动画**：岛按真实窗口矩形隐藏后，卡片从岛的位置飞下，落到屏幕中央（460ms 进场 / 360ms 退场）
- **液态玻璃**：半透明深色底 + 高光渐变 + 玻璃描边 + 大投影
- **涟漪特效**：卡片中心向四周扩散的三圈光环 + 径向光晕（60fps，全屏透明图层、鼠标穿透）
- **提示音**：可调音量的提醒音
- **语音播报（四种引擎）**
  - 系统讲述人（SAPI）—— 兜底，永远可用
  - **Edge TTS（官方参考实现 rany2/edge-tts）** —— 通过本机 Python 调用，`edge_tts` 缺失时自动 `pip install`
  - **Edge TTS（HTTP 接口）** —— 可指向自建的 Cloudflare Worker 等 `/v1/audio/speech`
  - **Mimo TTS** —— `https://tokenpapa.ai/v1/audio/speech`，填 API Key 即可
  - 朗读角色：下拉框直接从 `edge-tts --list-voices` 拉取（实测 300+ 音色），可一键试听
- **语音喊话转文字（可选 ASR）**：只有语音没有文字时，把音频 POST 到 `/v1/audio/transcriptions`，取 `{"text"}` 当作喊话内容
- **历史记录**：`history.json`，设置页可定位打开
- **测试通道**：设置页输入任意文字，一键复现完整效果（弹窗 + 提示音 + 语音），立即生效无需重启
- **自动化接入**
  - 行动：`批改邦喊话`（可主动发一条喊话）
  - 触发器：`收到批改邦喊话`
  - 规则：`正在接收批改邦喊话`（收到后 30 秒内为真）

## 安装

1. 到 Releases 下载 `PgbShoutBridge-plugin.zip`，解压到
   `ClassIsland 数据目录\Plugins\pgb.shoutbridge\`
   （一般是 `C:\classisland\data\Plugins\pgb.shoutbridge\`）
2. 重启 ClassIsland，设置里会出现「批改邦喊话」页
3. 给批改邦打 hook（见下）

## 批改邦 hook

`hook/pgb-hook.js` 需要在批改邦页面上下文里运行，才能拿到它自己的 WebSocket 流量。

```powershell
# 把 hook 注入到批改邦的 app.asar（会在 preload 里追加一行）
node patch-asar.js <批改邦 resources 目录>
```

或者用自带的重注入脚本 `repatch.ps1`（批改邦升级会覆盖 asar，建议注册成计划任务每天跑一次）。

## 配置项

设置页所有项都真实生效：

| 项 | 说明 |
|---|---|
| 监听端口 | 桥接服务端口，默认 18123 |
| 弹窗时长 | 卡片停留秒数 |
| 历史保留条数 | `history.json` 最多保留多少条 |
| 提示音 / 音量 | 播报前的一声提示音 |
| 语音播报 / 引擎 | SAPI / Mimo / Edge(HTTP) / Edge(参考实现) |
| 朗读角色 | Edge 音色，可从 300+ 音色里挑，支持试听 |
| 语速 / 语音音量 | Edge 的 rate、SAPI 的 Rate、整体音量 |
| Mimo API Key / 音色 / 语速 | Mimo 通道参数 |
| Edge 接口地址 | 填了就走 HTTP 接口，留空走本机 Python 参考实现 |
| ASR 转写接口 / Token | 语音喊话转文字 |
| Python 路径 | 留空自动查找 |

## 接口

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/health` | 健康检查：`{ok, shoutCount, historyCount, hookAttached}` |
| GET | `/history` | 喊话历史 |
| POST | `/shout` | 接收喊话：`{text, sender, imageBase64, imageUrl, voiceUrl, speech}` |
| POST | `/hello` | hook 握手 |
| POST | `/test` | 测试通道：`{text, sender, sound, speak, duration}` |

```bash
curl -X POST http://127.0.0.1:18123/test \
  -H "Content-Type: application/json" \
  -d '{"text":"同学们注意，这是一条测试喊话","sound":true,"speak":true}'
```

## 依赖

- ClassIsland 2.1.x（.NET 8 / Avalonia 11）
- 可选：Python 3 + `edge-tts`（使用 Edge 参考实现时）

## 已知限制

- 批改邦升级会覆盖 `app.asar`，需要用 `repatch.ps1` 重新注入
- Edge 直连微软 WS 可能被 403（DRM 校验/时钟），推荐走本机 Python 参考实现或自建 Worker
- 语音播报依赖系统有可用的 TTS；SAPI 始终可用

## 许可

MIT
