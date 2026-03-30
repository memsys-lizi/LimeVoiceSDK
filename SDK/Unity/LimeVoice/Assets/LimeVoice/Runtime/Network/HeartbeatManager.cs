using System;
using LimeVoice.Protocol;

namespace LimeVoice.Network
{
    // Sends PING every 10 s. Tracks last activity; fires OnTimeout after 30 s of silence.
    public class HeartbeatManager
    {
        public event Action OnTimeout;

        private const float PingInterval  = 10f;
        private const float TimeoutPeriod = 30f;

        private readonly UdpTransport _transport;
        private uint   _appId;
        private uint   _roomId;
        private ulong  _userId;

        private float _nextPingAt;
        private float _lastActivityAt;
        private bool  _active;

        public HeartbeatManager(UdpTransport transport)
        {
            _transport = transport;
        }

        public void Start(uint appId, uint roomId, ulong userId, float now)
        {
            _appId         = appId;
            _roomId        = roomId;
            _userId        = userId;
            _lastActivityAt = now;
            _nextPingAt    = now + PingInterval;
            _active        = true;
        }

        public void Stop() => _active = false;

        // Call from Unity Update() with Time.realtimeSinceStartup.
        public void Tick(float now)
        {
            if (!_active) return;

            if (now - _lastActivityAt > TimeoutPeriod)
            {
                _active = false;
                OnTimeout?.Invoke();
                return;
            }

            if (now >= _nextPingAt)
            {
                _transport.Send(PacketBuilder.BuildPing(_appId, _roomId, _userId));
                _nextPingAt = now + PingInterval;
            }
        }

        // Call whenever a packet is received from the server.
        public void RecordActivity(float now) => _lastActivityAt = now;
    }
}
