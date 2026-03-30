using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Godot;
using LimeVoice.Protocol;

namespace LimeVoice.Network
{
    public class UdpTransport : IDisposable
    {
        public ConcurrentQueue<ParsedPacket> InboundQueue { get; } = new ConcurrentQueue<ParsedPacket>();

        private UdpClient?  _client;
        private IPEndPoint? _remote;
        private Thread?     _recvThread;
        private bool        _running;

        public void Connect(string host, int port)
        {
            var addresses = Dns.GetHostAddresses(host);
            // Prefer IPv4 to avoid protocol mismatch on dual-stack systems.
            var addr = Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetwork)
                       ?? addresses[0];
            _remote = new IPEndPoint(addr, port);
            _client = new UdpClient(addr.AddressFamily);
            _client.Connect(_remote);
            _running    = true;
            _recvThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "LimeVoice-Recv" };
            _recvThread.Start();
        }

        public void Send(byte[] data)
        {
            if (_client == null || !_running) return;
            try { _client.Send(data, data.Length); }
            catch (Exception e) { GD.PushWarning($"[LimeVoice] Send error: {e.Message}"); }
        }

        private void ReceiveLoop()
        {
            while (_running)
            {
                try
                {
                    IPEndPoint ep  = new IPEndPoint(IPAddress.Any, 0);
                    byte[]     buf = _client!.Receive(ref ep);
                    if (PacketParser.TryParse(buf, out var packet) && packet != null)
                        InboundQueue.Enqueue(packet);
                }
                catch (SocketException e)
                {
                    if (_running)
                        GD.PushWarning($"[LimeVoice] Recv error: {e.Message}");
                }
                catch (Exception e)
                {
                    if (_running)
                        GD.PushWarning($"[LimeVoice] Recv unexpected: {e.Message}");
                }
            }
        }

        public void Dispose()
        {
            _running = false;
            _client?.Close();
            _client = null;
        }
    }
}
