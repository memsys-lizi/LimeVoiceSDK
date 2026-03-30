# LimeVoice SDK

LimeVoice 是一款高性能实时游戏语音服务，支持 Unity 和 Godot 4 平台，基于自定义 UDP 协议实现，延迟约 20ms，内置 Opus 编解码、麦克风采集、远端音频播放和说话活动检测（VAD）。

> **当前处于内测阶段**，服务免费开放，欢迎接入体验。

---

## 申请 AppID / AppKey

接入 LimeVoice 需要 AppID 和 AppKey，请通过以下方式免费申请：

- **QQ**：1433185301
- **邮箱**：19100817974@163.com

申请时请注明游戏名称和使用平台（Unity / Godot），我们会在 1 个工作日内回复。

---

## 服务地址

| 用途 | 地址 |
|---|---|
| UDP 语音连接 | `69.165.65.28:7848` |
| Token 获取 API | `http://69.165.65.28:8848` |

---

## SDK 文档

| 平台 | 文档 |
|---|---|
| Unity | [Unity SDK 使用文档](doc/unity-sdk.md) |
| Godot 4 | [Godot SDK 使用文档](doc/godot-sdk.md) |

---

## 快速接入流程

1. 申请 AppID + AppKey
2. 将 SDK 文件导入项目（详见对应文档）
3. 游戏后端用 AppKey 调用 Token 接口，下发 Token 给客户端
4. 客户端连接 `69.165.65.28:7848`，用 Token 加入语音房间

---

## 平台支持

| 平台 | Unity SDK | Godot SDK |
|---|---|---|
| Windows | ✅ | ✅ |
| macOS | ✅ | ✅ |
| Linux | ✅ | ✅ |
| Android | ✅ | ✅ |
| iOS | ✅ | ✅ |
| WebGL | ❌ | ❌ |

---

## License

MIT
