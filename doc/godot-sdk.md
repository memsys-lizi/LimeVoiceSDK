# LimeVoice Godot SDK 使用文档 V1.0.0

## 目录

1. [简介](#1-简介)
2. [环境要求](#2-环境要求)
3. [安装](#3-安装)
4. [快速开始](#4-快速开始)
5. [Token 获取](#5-token-获取)
6. [API 参考](#6-api-参考)
7. [事件说明](#7-事件说明)
8. [说话检测](#8-说话检测)
9. [错误码说明](#9-错误码说明)
10. [平台注意事项](#10-平台注意事项)
11. [常见问题](#11-常见问题)

---

## 1. 简介

LimeVoice Godot SDK 为 Godot 4 游戏提供开箱即用的实时语音聊天功能。SDK 基于 LimeVoice 服务端的自定义 UDP 协议，内置麦克风采集、Opus 音频编解码、远端音频播放和说话活动检测，开发者**无需关心底层音频处理**，几行代码即可完成接入。

**SDK 内容：**

```
LimeVoice/
├── Runtime/                    ← SDK 核心代码
│   ├── LimeVoiceClient.cs      ← 主入口（Autoload 单例）
│   ├── Protocol/
│   ├── Network/
│   └── Audio/
└── Samples/
    └── BasicVoiceChat/         ← 示例场景与脚本
        ├── BasicVoiceChat.tscn
        └── VoiceChatSample.cs
```

---

## 2. 环境要求

| 项目 | 要求 |
|---|---|
| Godot 版本 | 4.2 及以上（推荐 4.6） |
| 脚本语言 | **C#**（必须，SDK 为纯 C# 实现） |
| .NET 版本 | .NET 8 |
| 支持平台 | Windows、macOS、Linux、Android、iOS |
| 服务端 | LimeVoice 服务（AppID + AppKey 由官方分配） |
| 麦克风权限 | Android / iOS 需在平台设置中开启 |

> **注意**：Godot 必须使用 Mono（.NET）版本，标准版（无 C# 支持）无法使用本 SDK。

---

## 3. 安装

### 3.1 复制 SDK 文件

将 `LimeVoice/Runtime/` 目录完整复制到你的 Godot 项目中（建议放在 `res://LimeVoice/Runtime/`）。

### 3.2 添加 Concentus NuGet 包

SDK 使用 [Concentus](https://www.nuget.org/packages/Concentus) 进行 Opus 音频编解码。编辑项目根目录的 `.csproj` 文件，在 `<ItemGroup>` 中添加：

```xml
<ItemGroup>
  <PackageReference Include="Concentus" Version="2.2.2" />
</ItemGroup>
```

完整 `.csproj` 示例：

```xml
<Project Sdk="Godot.NET.Sdk/4.6.1">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Concentus" Version="2.2.2" />
  </ItemGroup>
</Project>
```

保存后在 Godot 编辑器中执行 **Project → Build Project**，编辑器会自动下载 Concentus 包。

### 3.3 开启音频输入

在 Godot 编辑器中：

**Project → Project Settings → Audio → Enable Input** → 勾选

或直接在 `project.godot` 文件中添加：

```ini
[audio]
driver/enable_input=true
```

> 不开启此选项，麦克风将无法工作。

### 3.4 注册 Autoload

`LimeVoiceClient` 必须以 Autoload 方式运行，以确保跨场景持久存在。

在 Godot 编辑器中：

1. **Project → Project Settings → Autoload**
2. 点击右侧文件夹图标，选择 `res://LimeVoice/Runtime/LimeVoiceClient.cs`
3. Node Name 填写 `LimeVoiceClient`
4. 点击 **Add**

或在 `project.godot` 中添加：

```ini
[autoload]
LimeVoiceClient="*res://LimeVoice/Runtime/LimeVoiceClient.cs"
```

---

## 4. 快速开始

### 4.1 最简接入

```csharp
using Godot;
using LimeVoice;

public partial class GameVoice : Node
{
    private LimeVoiceClient _client;

    public override void _Ready()
    {
        _client = LimeVoiceClient.Instance!;

        // 监听事件
        _client.OnJoined += () => GD.Print("语音已连接");
        _client.OnError  += (code, msg) => GD.PrintErr($"错误 {code}: {msg}");

        // 连接服务器（Token 需从你的游戏后端获取）
        _client.ConnectToServer("45.207.215.167", 7848);
        _client.JoinRoom("appId", "roomId", "userId", "token");
    }

    public override void _ExitTree()
    {
        _client?.Disconnect();
    }
}
```

### 4.2 完整流程

推荐流程：**连接 → 获取 Token → 加入房间 → 使用语音 → 离开房间**

```csharp
using Godot;
using LimeVoice;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SysHttpClient = System.Net.Http.HttpClient;

public partial class GameVoice : Node
{
    private LimeVoiceClient _client = null!;

    public override void _Ready()
    {
        _client = LimeVoiceClient.Instance!;

        _client.OnConnected    += ()         => GD.Print("已连接到服务器");
        _client.OnJoined       += ()         => GD.Print("已加入房间，语音聊天开始");
        _client.OnDisconnected += ()         => GD.Print("已断开连接");
        _client.OnUserJoined   += uid        => GD.Print($"玩家 {uid} 加入了房间");
        _client.OnUserLeft     += uid        => GD.Print($"玩家 {uid} 离开了房间");
        _client.OnError        += (c, m)     => GD.PrintErr($"错误 {c}: {m}");

        // 步骤 1：连接服务器
        _client.ConnectToServer("45.207.215.167", 7848);

        // 步骤 2：从你的游戏后端获取 Token，然后加入房间
        _ = GetTokenAndJoin();
    }

    private async Task GetTokenAndJoin()
    {
        string token = await FetchTokenFromYourServer("appId", "roomId", "userId");

        // 步骤 3：加入房间（需回到主线程）
        CallDeferred(nameof(JoinDeferred), "appId", "roomId", "userId", token);
    }

    private void JoinDeferred(string appId, string roomId, string userId, string token)
    {
        _client.JoinRoom(appId, roomId, userId, token);
    }

    // 退出房间（保持连接）
    public void OnLeaveRoomClick()
    {
        _client.LeaveRoom();
    }

    // 完全断开
    public override void _ExitTree()
    {
        _client?.Disconnect();
    }
}
```

> **注意**：`async/await` 运行在后台线程，加入房间等操作必须用 `CallDeferred` 切回主线程再调用。

---

## 5. Token 获取

Token 是用户进入语音房间的凭证，由 LimeVoice 服务端签发，包含 AppID、RoomID、UserID 和过期时间。

### 5.1 正确做法（生产环境）

Token 应由**你自己的游戏后端服务器**向 LimeVoice 服务端请求，再下发给客户端。**不要在 Godot 客户端代码中写入 AppKey。**

```
Godot 客户端 → 你的游戏服务器 → LimeVoice 服务端
                    ↑ 用 AppKey 请求 Token
               ↓ 返回 Token
Godot 客户端 使用 Token 加入房间
```

游戏后端请求示例：

```
POST http://45.207.215.167:8848/api/v1/apps/{appId}/token
X-App-Key: your-app-key
Content-Type: application/json

{
  "user_id": "player_001",
  "room_id":  "room_1",
  "ttl_seconds": 3600
}
```

响应：

```json
{
  "token":   "xxxxx.yyyyy",
  "app_id":  "a1b2c3d4",
  "room_id": "room_1",
  "user_id": "player_001"
}
```

### 5.2 简化做法（仅用于开发调试）

示例场景 `VoiceChatSample.cs` 中提供了直接从客户端请求 Token 的方式，仅供本地测试：

```csharp
// 仅供开发测试，生产环境请勿使用
private async Task FetchTokenForDebug(string appId, string roomId, string userId)
{
    string url  = $"http://45.207.215.167:8848/api/v1/apps/{appId}/token";
    string body = $"{{\"user_id\":\"{userId}\",\"room_id\":\"{roomId}\",\"ttl_seconds\":3600}}";

    using var http    = new SysHttpClient();
    using var content = new StringContent(body, Encoding.UTF8, "application/json");
    http.DefaultRequestHeaders.Add("X-App-Key", "your-app-key"); // 不要在生产环境这样做

    var response = await http.PostAsync(url, content);
    string json  = await response.Content.ReadAsStringAsync();

    using var doc = JsonDocument.Parse(json);
    string token  = doc.RootElement.GetProperty("token").GetString() ?? string.Empty;

    // 切回主线程再调用 JoinRoom
    CallDeferred(nameof(JoinDeferred), appId, roomId, userId, token);
}
```

> **注意**：`Godot.HttpClient` 与 `System.Net.Http.HttpClient` 存在命名冲突，需用别名：
> ```csharp
> using SysHttpClient = System.Net.Http.HttpClient;
> ```

### 5.3 Token 有效期

Token 包含过期时间（`ttl_seconds`），过期后服务端会拒绝加入，客户端收到错误码 `4011`。建议在 Token 过期前重新获取并调用 `JoinRoom`。

---

## 6. API 参考

### 6.1 LimeVoiceClient

SDK 的核心类，以 **Autoload** 方式运行，通过 `LimeVoiceClient.Instance` 全局访问。

#### 静态属性

| 属性 | 类型 | 说明 |
|---|---|---|
| `Instance` | `LimeVoiceClient` | 单例实例，全局唯一 |

#### 实例属性

| 属性 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `IsMuted` | `bool` | `false` | 是否静音本地麦克风。静音时仍发送静音帧，不影响连接心跳 |
| `SpeakingThreshold` | `float` | `0.01f` | 说话检测灵敏度（RMS 阈值）。值越小越灵敏，值越大需要越大声才触发 |

#### 方法

---

##### `ConnectToServer(string host, int port)`

连接到 LimeVoice 服务器，建立 UDP 通道。

| 参数 | 类型 | 说明 |
|---|---|---|
| `host` | `string` | 服务器 IP 地址或域名 |
| `port` | `int` | UDP 端口，默认 `7848` |

```csharp
_client.ConnectToServer("45.207.215.167", 7848);
```

> 调用后立即触发 `OnConnected`，此时尚未加入任何房间。

> **与 Unity SDK 的区别**：方法名为 `ConnectToServer` 而非 `Connect`，以避免与 Godot 内置的 `Node.Connect(signal, callable)` 方法冲突。

---

##### `JoinRoom(string appId, string roomId, string userId, string token)`

加入语音房间，成功后自动开始采集麦克风并发送/接收语音。

| 参数 | 类型 | 说明 |
|---|---|---|
| `appId` | `string` | 应用 ID，由 LimeVoice 服务端创建 App 时分配（8 位十六进制） |
| `roomId` | `string` | 房间 ID，任意字符串，同 ID 的用户进入同一房间 |
| `userId` | `string` | 当前玩家的用户 ID，房间内唯一 |
| `token` | `string` | 从服务端获取的认证 Token |

```csharp
_client.JoinRoom("a1b2c3d4", "battle_room_1", "player_001", token);
```

> 必须先调用 `ConnectToServer()` 才能调用此方法。服务端验证通过后触发 `OnJoined`，此后语音自动工作。

---

##### `LeaveRoom()`

离开当前房间，停止语音采集和播放，但保持与服务器的连接。

```csharp
_client.LeaveRoom();
```

> 离开后可再次调用 `JoinRoom()` 加入其他房间，无需重新 `ConnectToServer()`。

---

##### `Disconnect()`

断开与服务器的连接，同时离开当前房间（如在房间中），释放所有资源。

```csharp
_client.Disconnect();
```

> 通常在 `_ExitTree()` 中调用，确保退出时正确清理。

---

### 6.2 属性使用示例

```csharp
// 静音 / 取消静音
_client.IsMuted = true;   // 静音（发送静音帧）
_client.IsMuted = false;  // 取消静音

// 调整说话检测灵敏度（加入房间前后均可设置）
_client.SpeakingThreshold = 0.005f;  // 更灵敏（轻声也能检测）
_client.SpeakingThreshold = 0.02f;   // 更迟钝（需要较大声才触发）
```

---

## 7. 事件说明

所有事件均在 Godot **主线程**触发（通过 `_Process` 中的包队列消费），可安全操作场景节点和 UI。

### 7.1 连接与房间事件

#### `OnConnected`

**签名：** `Action`

调用 `ConnectToServer()` 后立即触发，表示 UDP 通道已建立。

```csharp
_client.OnConnected += () => {
    statusLabel.Text = "已连接到服务器";
};
```

---

#### `OnJoined`

**签名：** `Action`

服务端确认加入房间后触发，此后语音采集和播放正式开始。

```csharp
_client.OnJoined += () => {
    voiceIcon.Visible = true;
    statusLabel.Text  = "语音聊天已开始";
};
```

---

#### `OnDisconnected`

**签名：** `Action`

连接断开时触发，包括主动调用 `Disconnect()` 或网络超时（30 秒无响应）。

```csharp
_client.OnDisconnected += () => {
    voiceIcon.Visible = false;
    ShowReconnectUI();
};
```

---

#### `OnUserJoined`

**签名：** `Action<string userId>`

房间内有其他玩家加入时触发。

```csharp
_client.OnUserJoined += (userId) => {
    GD.Print($"玩家 {userId} 加入了房间");
    ShowPlayerInRoom(userId);
};
```

---

#### `OnUserLeft`

**签名：** `Action<string userId>`

房间内有玩家离开时触发，包括正常离开和超时断线。

```csharp
_client.OnUserLeft += (userId) => {
    GD.Print($"玩家 {userId} 离开了房间");
    RemovePlayerFromRoom(userId);
};
```

---

#### `OnError`

**签名：** `Action<int code, string message>`

发生错误时触发，通常是加入房间失败。错误码含义见[第 9 节](#9-错误码说明)。

```csharp
_client.OnError += (code, message) => {
    GD.PrintErr($"LimeVoice 错误 {code}: {message}");
    if (code == 4011) ShowTokenExpiredUI();
};
```

---

### 7.2 说话检测事件

#### `OnUserSpeaking`

**签名：** `Action<string userId, bool isSpeaking>`

远端玩家说话状态变化时触发。`isSpeaking = true` 表示开始说话，`false` 表示停止说话（300ms 静音冷却后触发）。

```csharp
_client.OnUserSpeaking += (userId, isSpeaking) => {
    if (_playerIcons.TryGetValue(userId, out var icon))
        icon.Visible = isSpeaking;
};
```

---

#### `OnLocalSpeaking`

**签名：** `Action<bool isSpeaking>`

本地玩家（自己）说话状态变化时触发，可用于显示本地麦克风活动指示器。

```csharp
_client.OnLocalSpeaking += (isSpeaking) => {
    localMicIcon.Modulate = isSpeaking ? Colors.Green : Colors.Gray;
};
```

---

## 8. 说话检测

SDK 内置基于 RMS（均方根）音量的实时说话检测，**无需额外配置**，加入房间后自动工作。

### 8.1 工作原理

每收到一个 20ms 的音频帧，SDK 计算该帧的 RMS 音量值：
- RMS 超过 `SpeakingThreshold` → 判断为"正在说话"，立即触发 `isSpeaking = true`
- 连续 300ms 低于阈值 → 判断为"停止说话"，触发 `isSpeaking = false`（避免瞬间抖动）

### 8.2 阈值调整建议

| 场景 | 推荐阈值 |
|---|---|
| 安静环境 | `0.008f` ~ `0.015f`（默认 `0.01f`） |
| 嘈杂环境（游戏音效大） | `0.02f` ~ `0.04f` |
| 高灵敏度（耳麦） | `0.005f` |

```csharp
// 在 _Ready() 或任意时刻调整
_client.SpeakingThreshold = 0.015f;
```

### 8.3 与玩家 UI 结合的完整示例

```csharp
private Dictionary<string, Label> _speakingLabels = new();

private void SetupVoice()
{
    _client.OnUserJoined += (userId) => {
        var label = new Label { Text = $"[{userId}]" };
        AddChild(label);
        _speakingLabels[userId] = label;
    };

    _client.OnUserLeft += (userId) => {
        if (_speakingLabels.TryGetValue(userId, out var label))
        {
            label.QueueFree();
            _speakingLabels.Remove(userId);
        }
    };

    _client.OnUserSpeaking += (userId, isSpeaking) => {
        if (_speakingLabels.TryGetValue(userId, out var label))
            label.Modulate = isSpeaking ? Colors.Green : Colors.White;
    };

    _client.OnLocalSpeaking += (isSpeaking) => {
        localMicIndicator.Visible = isSpeaking;
    };
}
```

---

## 9. 错误码说明

错误码通过 `OnError` 事件返回。

| 错误码 | 含义 | 解决方法 |
|---|---|---|
| `4000` | 加入请求格式错误 | SDK 内部错误，升级 SDK 版本 |
| `4010` | AppID 不存在 | 检查 `appId` 参数是否正确 |
| `4011` | Token 无效或已过期 | 重新从服务端获取 Token 后再次调用 `JoinRoom()` |
| `4012` | Token 内容与请求不匹配 | `token` 中的 appId/roomId/userId 必须与 `JoinRoom()` 参数一致 |

### 处理 Token 过期

```csharp
_client.OnError += async (code, message) => {
    if (code == 4011)
    {
        string newToken = await FetchNewToken();
        // 切回主线程调用 JoinRoom
        CallDeferred(nameof(JoinDeferred), appId, roomId, userId, newToken);
    }
};
```

---

## 10. 平台注意事项

### 10.1 Android

在 **Project → Export → Android** 导出配置中，确认勾选了：
- `RECORD_AUDIO` 权限

或在 `AndroidManifest.xml` 中手动声明：

```xml
<uses-permission android:name="android.permission.RECORD_AUDIO" />
<uses-permission android:name="android.permission.INTERNET" />
```

首次运行时系统会弹出麦克风权限请求，建议在调用 `ConnectToServer()` 前先主动请求权限（可使用 Godot 的 `OS.request_permissions()` 或平台插件）。

### 10.2 iOS

在 **Project → Export → iOS** 中填写：
- `Privacy - Microphone Usage Description`：例如 `"游戏语音聊天需要使用麦克风"`

未填写描述将导致 App Store 审核被拒。

### 10.3 WebGL

Godot WebGL 导出不支持自定义 UDP Socket（浏览器安全限制），本 SDK **不支持 WebGL 平台**。

---

## 11. 常见问题

**Q：调用 JoinRoom 后迟迟没有触发 OnJoined？**

A：检查以下几点：
1. `host` 填写 `45.207.215.167`，`port` 填写 `7848`
2. Token 是否有效，可通过 `OnError` 事件查看具体错误码
3. 防火墙是否放行了 UDP `7848` 端口
4. 手机端测试时需确保客户端和服务端网络互通

---

**Q：麦克风没有工作，没有采集到声音？**

A：检查以下几点：
1. 确认 `Project Settings → Audio → Enable Input` 已勾选
2. 确认已授予麦克风权限（Android / iOS）
3. 检查是否有其他应用占用了麦克风

---

**Q：能听到自己的声音（回声）？**

A：SDK 不做回声消除处理，这是麦克风采集到扬声器播放声音后又被麦克风采集到导致的。建议：
- 测试时使用耳机
- 正式项目中可在 Godot 的音频总线上添加 `AudioEffectEQ` 等效果插件处理回声

---

**Q：async/await 方法里调用 JoinRoom 没有效果？**

A：`async/await` 续体默认在线程池执行，而 Godot API（包括 SDK 方法）必须在主线程调用。解决方法是使用 `CallDeferred`：

```csharp
private async Task FetchAndJoin()
{
    string token = await FetchToken(); // 可在后台线程执行
    CallDeferred(nameof(JoinDeferred), appId, roomId, userId, token);
}

private void JoinDeferred(string appId, string roomId, string userId, string token)
{
    LimeVoiceClient.Instance!.JoinRoom(appId, roomId, userId, token);
}
```

---

**Q：出现 HttpClient 命名冲突编译错误？**

A：Godot 命名空间中也有 `HttpClient` 类，与 `System.Net.Http.HttpClient` 冲突。用别名解决：

```csharp
using SysHttpClient = System.Net.Http.HttpClient;

// 使用时
using var http = new SysHttpClient();
```

---

**Q：声音有卡顿或延迟？**

A：
- 确保服务器与客户端网络延迟较低
- 如果帧率较低（< 30 fps），可能影响音频推帧节奏，建议在实际 Build 上测试
- 可适当调大 `RemoteAudioPlayer` 的 `BufferLength`（默认 100ms）来换取更平滑的播放

---

**Q：OnUserSpeaking 触发太频繁或不触发？**

A：调整 `SpeakingThreshold`：
- 触发太频繁（环境噪音也触发）→ 调大阈值，如 `0.02f`
- 说话了不触发 → 调小阈值，如 `0.005f`

---

**Q：同一个玩家用多个客户端加入同一房间会怎样？**

A：每个客户端 UDP 地址不同，服务端会视为独立会话，房间内会出现重复的音频流。建议业务层保证同一 `userId` 同时只有一个连接。
