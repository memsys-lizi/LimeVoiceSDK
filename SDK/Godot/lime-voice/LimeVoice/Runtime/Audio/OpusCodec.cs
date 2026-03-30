#pragma warning disable CS0618
using System;
using Concentus.Structs;
using Concentus.Enums;

namespace LimeVoice.Audio
{
    // Wraps Concentus encoder and decoder.
    // All audio: 48000 Hz, Mono, 20 ms frames (960 samples).
    public class OpusCodec : IDisposable
    {
        public const int SampleRate   = 48000;
        public const int Channels     = 1;
        public const int FrameSamples = 960; // 20 ms at 48000 Hz

        private readonly OpusEncoder _encoder;
        private readonly OpusDecoder _decoder;
        private readonly byte[]      _encodeBuf = new byte[4000];

        public OpusCodec()
        {
            _encoder         = new OpusEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_VOIP);
            _encoder.Bitrate = 24000;
            _decoder         = new OpusDecoder(SampleRate, Channels);
        }

        // Encodes one 20 ms PCM frame (960 shorts) → Opus bytes.
        public byte[] Encode(short[] pcm)
        {
            int len    = _encoder.Encode(pcm, 0, FrameSamples, _encodeBuf, 0, _encodeBuf.Length);
            var result = new byte[len];
            Buffer.BlockCopy(_encodeBuf, 0, result, 0, len);
            return result;
        }

        // Decodes Opus bytes → 960 shorts.
        public short[] Decode(byte[] opusData)
        {
            var pcm = new short[FrameSamples * Channels];
            _decoder.Decode(opusData, 0, opusData.Length, pcm, 0, FrameSamples);
            return pcm;
        }

        public void Dispose()
        {
            _encoder?.Dispose();
            _decoder?.Dispose();
        }
    }
}
