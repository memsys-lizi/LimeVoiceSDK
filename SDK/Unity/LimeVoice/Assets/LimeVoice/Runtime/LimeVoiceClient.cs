using System;
using System.Collections.Generic;
using System.Text;
using LimeVoice.Audio;
using LimeVoice.Network;
using LimeVoice.Protocol;
using UnityEngine;

namespace LimeVoice
{
    // Singleton MonoBehaviour — add to a persistent GameObject in your scene.
    // Call Connect() then JoinRoom(); the SDK handles everything else.
    public class LimeVoiceClient : MonoBehaviour
    {
        public static LimeVoiceClient Instance { get; private set; }

        // --- Events ---
        public event Action           OnConnected;
        public event Action           OnJoined;
        public event Action           OnDisconnected;
        public event Action<string>   OnUserJoined;           // userId
        public event Action<string>   OnUserLeft;             // userId
        public event Action<int, string> OnError;             // code, message
        public event Action<string, bool> OnUserSpeaking;     // userId, isSpeaking (remote)
        public event Action<bool>     OnLocalSpeaking;        // isSpeaking (local mic)

        // --- Public properties ---
        public bool IsMuted
        {
            get => _mic != null && _mic.Muted;
            set { if (_mic != null) _mic.Muted = value; }
        }

        // Speaking detection threshold (RMS). Default 0.01. Lower = more sensitive.
        // Takes effect immediately for new players; also updates existing ones.
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
        private UdpTransport     _transport;
        private HeartbeatManager _heartbeat;
        private MicrophoneCapture _mic;

        private uint  _appId;
        private uint  _roomId;
        private ulong _userId;
        private string _appIdStr;
        private string _roomIdStr;
        private string _userIdStr;

        private readonly Dictionary<string, RemoteAudioPlayer> _players =
            new Dictionary<string, RemoteAudioPlayer>();

        private bool _connected;
        private bool _inRoom;

        // ---------------------------------------------------------------
        // Unity lifecycle
        // ---------------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (_transport == null) return;

            _heartbeat?.Tick(Time.realtimeSinceStartup);

            while (_transport.InboundQueue.TryDequeue(out var packet))
                HandlePacket(packet);
        }

        private void OnDestroy() => CleanUp();

        // ---------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------

        public void Connect(string host, int port)
        {
            if (_connected) return;
            _transport = new UdpTransport();
            _transport.Connect(host, port);
            _heartbeat = new HeartbeatManager(_transport);
            _heartbeat.OnTimeout += HandleTimeout;
            _connected = true;
            OnConnected?.Invoke();
        }

        // appId / roomId / userId as the string forms used by your server.
        // Internally they are also stored as uint/ulong for the packet header.
        public void JoinRoom(string appId, string roomId, string userId, string token)
        {
            if (!_connected || _inRoom) return;

            _appIdStr  = appId;
            _roomIdStr = roomId;
            _userIdStr = userId;

            // Hash string IDs to numeric header fields (server only echoes these values back).
            _appId  = (uint)StableHash(appId);
            _roomId = (uint)StableHash(roomId);
            _userId = StableHash(userId);

            _transport.Send(
                PacketBuilder.BuildJoin(_appId, _roomId, _userId, appId, roomId, userId, token));
        }

        public void LeaveRoom()
        {
            if (!_inRoom) return;
            _transport.Send(PacketBuilder.BuildLeave(_appId, _roomId, _userId));
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
            _heartbeat?.RecordActivity(Time.realtimeSinceStartup);

            switch (packet.Header.Type)
            {
                case PacketType.JoinAck:
                    _inRoom = true;
                    _heartbeat.Start(_appId, _roomId, _userId, Time.realtimeSinceStartup);
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
                    // heartbeat activity already recorded above
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
                player = CreatePlayer(key);
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
                Destroy(player.gameObject);
                _players.Remove(key);
            }
            OnUserLeft?.Invoke(key);
        }

        private void HandleError(byte[] payload)
        {
            if (payload.Length < 2) { OnError?.Invoke(0, "Unknown error"); return; }
            int code   = (payload[0] << 8) | payload[1];
            string msg = payload.Length > 2
                ? Encoding.UTF8.GetString(payload, 2, payload.Length - 2)
                : string.Empty;
            OnError?.Invoke(code, msg);
        }

        private void HandleTimeout()
        {
            Debug.LogWarning("[LimeVoice] Connection timed out.");
            TearDownRoom();
            OnDisconnected?.Invoke();
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private void StartMicrophone()
        {
            var go = new GameObject("LimeVoice_Mic");
            go.transform.SetParent(transform);
            _mic = go.AddComponent<MicrophoneCapture>();
            _mic.OnFrame += opusFrame =>
                _transport?.Send(PacketBuilder.BuildVoiceData(_appId, _roomId, _userId, opusFrame));
            _mic.OnSpeakingChanged += isSpeaking => OnLocalSpeaking?.Invoke(isSpeaking);
            _mic.StartCapture();
        }

        private RemoteAudioPlayer CreatePlayer(string userId)
        {
            var go = new GameObject($"LimeVoice_Player_{userId}");
            go.transform.SetParent(transform);
            var player = go.AddComponent<RemoteAudioPlayer>();
            player.Init(userId);
            player.SpeakingThreshold = _speakingThreshold;
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
                Destroy(_mic.gameObject);
                _mic = null;
            }

            foreach (var p in _players.Values)
                if (p != null) Destroy(p.gameObject);
            _players.Clear();
        }

        private void CleanUp()
        {
            TearDownRoom();
            _transport?.Dispose();
            _transport = null;
            _connected = false;
        }

        // FNV-1a 64-bit hash — deterministic string → ulong for the packet header UserID field.
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
