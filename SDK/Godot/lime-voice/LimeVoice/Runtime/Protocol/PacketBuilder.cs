using System;
using System.Text;

namespace LimeVoice.Protocol
{
    public static class PacketBuilder
    {
        private static uint _seq = 0;

        private static byte[] Build(byte type, uint appId, uint roomId, ulong userId, byte[]? payload)
        {
            int payLen    = payload?.Length ?? 0;
            int totalSize = PacketHeader.Size + payLen + 4; // 4 = CRC32
            byte[] buf    = new byte[totalSize];

            var header = new PacketHeader
            {
                Type       = type,
                AppId      = appId,
                RoomId     = roomId,
                UserId     = userId,
                Seq        = ++_seq,
                Timestamp  = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                PayloadLen = (ushort)payLen
            };
            header.WriteTo(buf, 0);

            if (payLen > 0)
                Array.Copy(payload!, 0, buf, PacketHeader.Size, payLen);

            uint crc = Crc32(buf, 0, PacketHeader.Size + payLen);
            int crcOffset = PacketHeader.Size + payLen;
            buf[crcOffset]     = (byte)(crc >> 24);
            buf[crcOffset + 1] = (byte)(crc >> 16);
            buf[crcOffset + 2] = (byte)(crc >>  8);
            buf[crcOffset + 3] = (byte) crc;

            return buf;
        }

        public static byte[] BuildPing(uint appId, uint roomId, ulong userId) =>
            Build(PacketType.Ping, appId, roomId, userId, null);

        public static byte[] BuildLeave(uint appId, uint roomId, ulong userId) =>
            Build(PacketType.Leave, appId, roomId, userId, null);

        public static byte[] BuildJoin(uint appId, uint roomId, ulong userId,
                                       string appIdStr, string roomIdStr, string userIdStr, string token)
        {
            string json = $"{{\"app_id\":\"{appIdStr}\",\"room_id\":\"{roomIdStr}\"," +
                          $"\"user_id\":\"{userIdStr}\",\"token\":\"{token}\"}}";
            byte[] payload = Encoding.UTF8.GetBytes(json);
            return Build(PacketType.Join, appId, roomId, userId, payload);
        }

        public static byte[] BuildVoiceData(uint appId, uint roomId, ulong userId, byte[] opusFrame) =>
            Build(PacketType.VoiceData, appId, roomId, userId, opusFrame);

        // IEEE CRC32 (polynomial 0xEDB88320)
        internal static uint Crc32(byte[] data, int offset, int length)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = offset; i < offset + length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return ~crc;
        }
    }
}
