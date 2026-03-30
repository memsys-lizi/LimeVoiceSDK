using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using LimeVoice.Audio;
using LimeVoice.Network;
using LimeVoice.Protocol;

namespace LimeVoice
{
    // Singleton Node — add to Autoload in Project Settings (Project → Project Settings → Autoload).
    // Call ConnectToServer() then JoinRoom(); the SDK handles everything else.
    public partial class LimeVoiceClient : Node
    {
        public static LimeVoiceClient? Instance { get; private set; }

        // --- Events ---
        public event Action?               OnConnected;
        public event Action?               OnJoined;
        public event Action?               OnDisconnected;
        public event Action<string>?       OnUserJoined;        // userId
        public event Action<string>?       OnUserLeft;          // userId
        public event Action<int, string>?  OnError;             // code, message
        public event Action<string, bool>? OnUserSpeaking;      // userId, isSpeaking (remote)
        public event Action<bool>?         OnLocalSpeaking;     // isSpeaking (local mic)

        // --- Public properties ---
        public bool IsMuted
        {
            get => _mic != null && _mic.Muted;
            set { if (_mic != null) _mic.Muted = value; }
        }

        public float SpeakingThreshold
        {
            get => _speakingThreshold;
            set
            {
                _speakingThreshold = value;
                if (_mic != null) _mic.SpeakingThreshold = value;
                foreach (var p in _players.Values) p.SpeakingThreshold = value;
            }
        }
        private float _speakingThreshold = 0.01f;

        // --- Private state ---
        private UdpTransport?      _transport;
        private HeartbeatManager?  _heartbeat;
        private MicrophoneCapture? _mic;

        private uint   _appId;
        private uint   _roomId;
        private ulong  _userId;
        private string _appIdStr  = string.Empty;
        private string _roomIdStr = string.Empty;
        private string _userIdStr = string.Empty;

        private readonly Dictionary<string, RemoteAudioPlayer> _players =
            new Dictionary<string, RemoteAudioPlayer>();

        private bool _connected;
        private bool _inRoom;

        // ---------------------------------------------------------------
        // Godot lifecycle
        // ---------------------------------------------------------------

        public override void _Ready()
        {
            Instance = this;
        }

        public override void _Process(double delta)
        {
            if (_transport == null) return;

            float now = Time.GetTicksMsec() * 0.001f;
            _heartbeat?.Tick(now);

            while (_transport.InboundQueue.TryDequeue(out var packet))
                HandlePacket(packet);
        }

        public override void _ExitTree() => CleanUp();

        // ---------------------------------------------------------------
        // Public API
        // ConnectToServer avoids collision with Godot's Node.Connect(signal, callable).
        // ---------------------------------------------------------------

        public void ConnectToServer(string host, int port)
        {
            if (_connected) return;
            _transport = new UdpTransport();
            _transport.Connect(host, port);
            _heartbeat = new HeartbeatManager(_transport);
            _heartbeat.OnTimeout += HandleTimeout;
            _connected = true;
            OnConnected?.Invoke();
        }

        public void JoinRoom(string appId, string roomId, string userId, string token)
        {
            if (!_connected || _inRoom) return;

            _appIdStr  = appId;
            _roomIdStr = roomId;
            _userIdStr = userId;

            _appId  = (uint)StableHash(appId);
            _roomId = (uint)StableHash(roomId);
            _userId = StableHash(userId);

            _transport!.Send(
                PacketBuilder.BuildJoin(_appId, _roomId, _userId, appId, roomId, userId, token));
        }

        public void LeaveRoom()
        {
            if (!_inRoom) return;
            _transport!.Send(PacketBuilder.BuildLeave(_appId, _roomId, _userId));
            TearDownRoom();
        }

        public void Disconnect()
        {
            if (_inRoom) LeaveRoom();
            CleanUp();
            OnDisconnected?.Invoke();
        }

        // ---------------------------------------------------------------
        // Packet handling (called on main thread via queue)
        // ---------------------------------------------------------------

        private void HandlePacket(ParsedPacket packet)
        {
            float now = Time.GetTicksMsec() * 0.001f;
            _heartbeat?.RecordActivity(now);

            switch (packet.Header.Type)
            {
                case PacketType.JoinAck:
                    _inRoom = true;
                    _heartbeat!.Start(_appId, _roomId, _userId, now);
                    StartMicrophone();
                    OnJoined?.Invoke();
                    break;

                case PacketType.VoiceFwd:
                    HandleVoiceFwd(packet.Payload);
                    break;

                case PacketType.UserJoined:
                    HandleUserJoined(packet.Header.UserId);
                    break;

                case PacketType.UserLeft:
                    HandleUserLeft(packet.Header.UserId);
                    break;

                case PacketType.Pong:
                    break;

                case PacketType.Error:
                    HandleError(packet.Payload);
                    break;
            }
        }

        private void HandleVoiceFwd(byte[] payload)
        {
            if (payload.Length < 8) return;
            ulong srcUserId = ReadBEU64(payload, 0);
            byte[] opusData = new byte[payload.Length - 8];
            Array.Copy(payload, 8, opusData, 0, opusData.Length);

            string key = srcUserId.ToString();
            if (!_players.TryGetValue(key, out var player))
            {
                player        = CreatePlayer(key);
                _players[key] = player;
            }
            player.Feed(opusData);
        }

        private void HandleUserJoined(ulong userId)
        {
            string key = userId.ToString();
            if (!_players.ContainsKey(key))
                _players[key] = CreatePlayer(key);
            OnUserJoined?.Invoke(key);
        }

        private void HandleUserLeft(ulong userId)
        {
            string key = userId.ToString();
            if (_players.TryGetValue(key, out var player))
            {
                player.QueueFree();
                _players.Remove(key);
            }
            OnUserLeft?.Invoke(key);
        }

        private void HandleError(byte[] payload)
        {
            if (payload.Length < 2) { OnError?.Invoke(0, "Unknown error"); return; }
            int    code = (payload[0] << 8) | payload[1];
            string msg  = payload.Length > 2
                ? Encoding.UTF8.GetString(payload, 2, payload.Length - 2)
                : string.Empty;
            OnError?.Invoke(code, msg);
        }

        private void HandleTimeout()
        {
            GD.PushWarning("[LimeVoice] Connection timed out.");
            TearDownRoom();
            OnDisconnected?.Invoke();
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private void StartMicrophone()
        {
            _mic = new MicrophoneCapture();
            AddChild(_mic);
            _mic.OnFrame           += opusFrame =>
                _transport?.Send(PacketBuilder.BuildVoiceData(_appId, _roomId, _userId, opusFrame));
            _mic.OnSpeakingChanged += isSpeaking => OnLocalSpeaking?.Invoke(isSpeaking);
            _mic.StartCapture();
        }

        private RemoteAudioPlayer CreatePlayer(string userId)
        {
            var player = new RemoteAudioPlayer();
            AddChild(player);
            player.Init(userId);
            player.SpeakingThreshold  = _speakingThreshold;
            player.OnSpeakingChanged += isSpeaking => OnUserSpeaking?.Invoke(userId, isSpeaking);
            return player;
        }

        private void TearDownRoom()
        {
            _inRoom = false;
            _heartbeat?.Stop();

            if (_mic != null)
            {
                _mic.StopCapture();
                _mic.QueueFree();
                _mic = null;
            }

            foreach (var p in _players.Values)
                p?.QueueFree();
            _players.Clear();
        }

        private void CleanUp()
        {
            TearDownRoom();
            _transport?.Dispose();
            _transport = null;
            _connected = false;
        }

        private static ulong StableHash(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
            return h;
        }

        private static ulong ReadBEU64(byte[] b, int o)
        {
            ulong hi = ((ulong)b[o] << 24) | ((ulong)b[o+1] << 16) | ((ulong)b[o+2] << 8) | b[o+3];
            ulong lo = ((ulong)b[o+4] << 24) | ((ulong)b[o+5] << 16) | ((ulong)b[o+6] << 8) | b[o+7];
            return (hi << 32) | lo;
        }
    }
}
