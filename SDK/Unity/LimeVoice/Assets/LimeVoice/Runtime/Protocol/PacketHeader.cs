using System;

namespace LimeVoice.Protocol
{
    // Fixed 34-byte header, all multi-byte fields are Big-Endian.
    // Layout: Magic[2] | Version[1] | Type[1] | AppID[4] | RoomID[4] | UserID[8] | Seq[4] | Timestamp[8] | PayloadLen[2]
    public struct PacketHeader
    {
        public const int Size = 34;
        private static readonly byte[] Magic = { 0x4C, 0x56 }; // 'L', 'V'
        private const byte Version = 0x01;

        public byte   Type;
        public uint   AppId;
        public uint   RoomId;
        public ulong  UserId;
        public uint   Seq;
        public ulong  Timestamp;
        public ushort PayloadLen;

        public void WriteTo(byte[] buf, int offset = 0)
        {
            buf[offset + 0] = Magic[0];
            buf[offset + 1] = Magic[1];
            buf[offset + 2] = Version;
            buf[offset + 3] = Type;
            WriteBE(buf, offset + 4,  AppId);
            WriteBE(buf, offset + 8,  RoomId);
            WriteBE(buf, offset + 12, UserId);
            WriteBE(buf, offset + 20, Seq);
            WriteBE(buf, offset + 24, Timestamp);
            WriteBE(buf, offset + 32, PayloadLen);
        }

        public static bool TryRead(byte[] buf, int offset, out PacketHeader h)
        {
            h = default;
            if (buf.Length - offset < Size) return false;
            if (buf[offset] != Magic[0] || buf[offset + 1] != Magic[1]) return false;
            // Version byte at offset+2 is ignored for forward compatibility.
            h.Type       = buf[offset + 3];
            h.AppId      = ReadBEU32(buf, offset + 4);
            h.RoomId     = ReadBEU32(buf, offset + 8);
            h.UserId     = ReadBEU64(buf, offset + 12);
            h.Seq        = ReadBEU32(buf, offset + 20);
            h.Timestamp  = ReadBEU64(buf, offset + 24);
            h.PayloadLen = ReadBEU16(buf, offset + 32);
            return true;
        }

        // --- Big-Endian helpers ---
        private static void WriteBE(byte[] b, int o, uint v)
        {
            b[o]   = (byte)(v >> 24);
            b[o+1] = (byte)(v >> 16);
            b[o+2] = (byte)(v >>  8);
            b[o+3] = (byte) v;
        }
        private static void WriteBE(byte[] b, int o, ulong v)
        {
            b[o]   = (byte)(v >> 56);
            b[o+1] = (byte)(v >> 48);
            b[o+2] = (byte)(v >> 40);
            b[o+3] = (byte)(v >> 32);
            b[o+4] = (byte)(v >> 24);
            b[o+5] = (byte)(v >> 16);
            b[o+6] = (byte)(v >>  8);
            b[o+7] = (byte) v;
        }
        private static void WriteBE(byte[] b, int o, ushort v)
        {
            b[o]   = (byte)(v >> 8);
            b[o+1] = (byte) v;
        }
        private static uint  ReadBEU32(byte[] b, int o) =>
            ((uint)b[o] << 24) | ((uint)b[o+1] << 16) | ((uint)b[o+2] << 8) | b[o+3];
        private static ulong ReadBEU64(byte[] b, int o) =>
            ((ulong)ReadBEU32(b, o) << 32) | ReadBEU32(b, o + 4);
        private static ushort ReadBEU16(byte[] b, int o) =>
            (ushort)((b[o] << 8) | b[o+1]);
    }
}
