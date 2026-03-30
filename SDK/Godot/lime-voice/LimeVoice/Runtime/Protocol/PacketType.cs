namespace LimeVoice.Protocol
{
    public static class PacketType
    {
        public const byte Ping       = 0x01;
        public const byte Pong       = 0x02;
        public const byte Join       = 0x10;
        public const byte JoinAck    = 0x11;
        public const byte Leave      = 0x12;
        public const byte VoiceData  = 0x20;
        public const byte VoiceFwd   = 0x21;
        public const byte UserJoined = 0x30;
        public const byte UserLeft   = 0x31;
        public const byte Error      = 0x40;
    }

    public static class ErrorCode
    {
        public const ushort BadJoinPayload  = 4000;
        public const ushort AppNotFound     = 4010;
        public const ushort TokenInvalid    = 4011;
        public const ushort TokenMismatch   = 4012;
    }
}
