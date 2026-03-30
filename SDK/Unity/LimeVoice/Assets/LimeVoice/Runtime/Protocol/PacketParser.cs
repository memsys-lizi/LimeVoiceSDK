using System;

namespace LimeVoice.Protocol
{
    public class ParsedPacket
    {
        public PacketHeader Header;
        public byte[]       Payload;
    }

    public static class PacketParser
    {
        private const int MinSize = PacketHeader.Size + 4; // header + CRC

        public static bool TryParse(byte[] buf, out ParsedPacket packet)
        {
            packet = null;
            if (buf == null || buf.Length < MinSize) return false;

            if (!PacketHeader.TryRead(buf, 0, out var header)) return false;

            int expectedTotal = PacketHeader.Size + header.PayloadLen + 4;
            if (buf.Length < expectedTotal) return false;

            // Verify CRC32 over header + payload
            uint expected = PacketBuilder.Crc32(buf, 0, PacketHeader.Size + header.PayloadLen);
            int crcOffset = PacketHeader.Size + header.PayloadLen;
            uint received = ((uint)buf[crcOffset]     << 24)
                          | ((uint)buf[crcOffset + 1] << 16)
                          | ((uint)buf[crcOffset + 2] <<  8)
                          |        buf[crcOffset + 3];

            if (expected != received) return false;

            byte[] payload = new byte[header.PayloadLen];
            if (header.PayloadLen > 0)
                Array.Copy(buf, PacketHeader.Size, payload, 0, header.PayloadLen);

            packet = new ParsedPacket { Header = header, Payload = payload };
            return true;
        }
    }
}
