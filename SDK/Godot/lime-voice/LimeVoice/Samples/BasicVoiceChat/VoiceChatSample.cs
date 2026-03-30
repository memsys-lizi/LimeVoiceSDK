using Godot;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SysHttpClient = System.Net.Http.HttpClient;

namespace LimeVoice.Samples
{
	// Basic voice chat sample — for DEBUG / development only.
	// WARNING: Embedding AdminKey in client code is insecure. In production,
	//          have your game server fetch and return the Token to the client.
	public partial class VoiceChatSample : Control
	{
		private LineEdit _serverIpEdit    = null!;
		private LineEdit _portEdit        = null!;
		private LineEdit _appIdEdit       = null!;
		private LineEdit _roomIdEdit      = null!;
		private LineEdit _userIdEdit      = null!;
		private LineEdit _appKeyEdit     = null!;
		private Button   _connectBtn      = null!;
		private Button   _joinBtn         = null!;
		private Button   _leaveBtn        = null!;
		private Button   _muteBtn         = null!;
		private Label    _statusLabel     = null!;
		private Label    _localMicLabel   = null!;

		private LimeVoiceClient _client = null!;
		private bool _muted;

		public override void _Ready()
		{
			_client = LimeVoiceClient.Instance!;

			_serverIpEdit  = GetNode<LineEdit>("VBox/ServerIP");
			_portEdit      = GetNode<LineEdit>("VBox/Port");
			_appIdEdit     = GetNode<LineEdit>("VBox/AppId");
			_roomIdEdit    = GetNode<LineEdit>("VBox/RoomId");
			_userIdEdit    = GetNode<LineEdit>("VBox/UserId");
			_appKeyEdit    = GetNode<LineEdit>("VBox/AppKey");
			_connectBtn    = GetNode<Button>("VBox/Buttons/ConnectBtn");
			_joinBtn       = GetNode<Button>("VBox/Buttons/JoinBtn");
			_leaveBtn      = GetNode<Button>("VBox/Buttons/LeaveBtn");
			_muteBtn       = GetNode<Button>("VBox/Buttons/MuteBtn");
			_statusLabel   = GetNode<Label>("VBox/StatusLabel");
			_localMicLabel = GetNode<Label>("VBox/LocalMicLabel");

			_connectBtn.Pressed += OnConnectPressed;
			_joinBtn.Pressed    += OnJoinPressed;
			_leaveBtn.Pressed   += OnLeavePressed;
			_muteBtn.Pressed    += OnMutePressed;

			_client.OnConnected    += ()         => SetStatus("已连接到服务器");
			_client.OnJoined       += ()         => SetStatus("已加入房间，语音聊天开始");
			_client.OnDisconnected += ()         => SetStatus("已断开连接");
			_client.OnUserJoined   += uid        => SetStatus($"玩家 {uid} 加入了房间");
			_client.OnUserLeft     += uid        => SetStatus($"玩家 {uid} 离开了房间");
			_client.OnError        += (code, msg) => SetStatus($"错误 {code}: {msg}");
			_client.OnLocalSpeaking += speaking  => _localMicLabel.Text = speaking ? "[麦克风] 说话中..." : "[麦克风] 待机";
			_client.OnUserSpeaking  += (uid, s)  => GD.Print($"[LimeVoice] 玩家 {uid} {(s ? "开始" : "停止")}说话");

			UpdateButtonStates();
		}

		private void OnConnectPressed()
		{
			string host = _serverIpEdit.Text.Trim();
			if (!int.TryParse(_portEdit.Text.Trim(), out int port)) port = 7848;
			_client.ConnectToServer(host, port);
			UpdateButtonStates();
		}

		private void OnJoinPressed()
		{
			string appId    = _appIdEdit.Text.Trim();
			string roomId   = _roomIdEdit.Text.Trim();
			string userId   = _userIdEdit.Text.Trim();
			string appKey   = _appKeyEdit.Text.Trim();
			string host     = _serverIpEdit.Text.Trim();
			if (!int.TryParse(_portEdit.Text.Trim(), out int port)) port = 7848;

			SetStatus("正在获取 Token...");
			// Fire-and-forget; errors are shown via SetStatus.
			_ = FetchTokenAndJoin(host, port, appId, roomId, userId, appKey);
		}

		private async Task FetchTokenAndJoin(string host, int port,
			string appId, string roomId, string userId, string appKey)
		{
			try
			{
				string url  = $"http://{host}:8848/api/v1/apps/{appId}/token";
				string body = $"{{\"user_id\":\"{userId}\",\"room_id\":\"{roomId}\",\"ttl_seconds\":3600}}";

				using var http    = new SysHttpClient();
				using var content = new StringContent(body, Encoding.UTF8, "application/json");
				http.DefaultRequestHeaders.Add("X-App-Key", appKey);

				var response = await http.PostAsync(url, content);
				string json  = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
				{
					CallDeferred(nameof(SetStatus), $"Token 请求失败: {response.StatusCode}");
					return;
				}

				using var doc = JsonDocument.Parse(json);
				string token  = doc.RootElement.GetProperty("token").GetString() ?? string.Empty;

				// Invoke on main thread via CallDeferred.
				CallDeferred(nameof(JoinRoomDeferred), appId, roomId, userId, token);
			}
			catch (Exception e)
			{
				CallDeferred(nameof(SetStatus), $"获取 Token 失败: {e.Message}");
			}
		}

		// Called on main thread via CallDeferred.
		private void JoinRoomDeferred(string appId, string roomId, string userId, string token)
		{
			_client.JoinRoom(appId, roomId, userId, token);
			UpdateButtonStates();
		}

		private void OnLeavePressed()
		{
			_client.LeaveRoom();
			UpdateButtonStates();
		}

		private void OnMutePressed()
		{
			_muted          = !_muted;
			_client.IsMuted = _muted;
			_muteBtn.Text   = _muted ? "取消静音" : "静音";
		}

		private void SetStatus(string text) => _statusLabel.Text = text;

		private void UpdateButtonStates()
		{
			// Simple enable/disable based on connection state isn't tracked here;
			// users can connect/join in any order for dev flexibility.
		}
	}
}
