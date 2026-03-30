using System;
using Godot;

namespace LimeVoice.Audio
{
    // Captures microphone input using Godot's AudioEffectCapture on a dedicated bus.
    // Resamples from the system mix rate to 48000 Hz and encodes each 20 ms frame with Opus.
    public partial class MicrophoneCapture : Node
    {
        public event Action<byte[]>? OnFrame;
        public event Action<bool>?  OnSpeakingChanged;
        public float SpeakingThreshold = 0.01f;
        public bool  Muted = false;

        private const float SpeakingCooldown = 0.3f;
        private const string BusName = "LimeVoiceMic";

        private OpusCodec?           _codec;
        private AudioStreamPlayer?   _player;
        private AudioEffectCapture?  _capture;
        private int                  _busIdx = -1;
        private int                  _systemSampleRate;

        private bool  _isSpeaking;
        private float _lastSpeakingTime;

        // Accumulates raw samples (at system sample rate) until we have enough for one Opus frame.
        private float[] _pending = Array.Empty<float>();

        public void StartCapture()
        {
            _codec            = new OpusCodec();
            _systemSampleRate = (int)AudioServer.GetMixRate();

            // Create a dedicated audio bus for microphone capture with output muted.
            _busIdx = AudioServer.BusCount;
            AudioServer.AddBus(_busIdx);
            AudioServer.SetBusName(_busIdx, BusName);
            AudioServer.SetBusMute(_busIdx, true);

            var captureEffect = new AudioEffectCapture();
            AudioServer.AddBusEffect(_busIdx, captureEffect, 0);

            // AudioStreamPlayer streams the microphone into the bus.
            _player        = new AudioStreamPlayer();
            _player.Stream = new AudioStreamMicrophone();
            _player.Bus    = BusName;
            AddChild(_player);
            _player.Play();

            _capture = (AudioEffectCapture)AudioServer.GetBusEffect(_busIdx, 0);
        }

        public void StopCapture()
        {
            if (_player != null)
            {
                _player.Stop();
                _player.QueueFree();
                _player = null;
            }
            if (_busIdx >= 0)
            {
                AudioServer.RemoveBus(_busIdx);
                _busIdx = -1;
            }
            _capture = null;
            _codec?.Dispose();
            _codec   = null;
            _pending = Array.Empty<float>();
        }

        public override void _Process(double delta)
        {
            // Speaking cooldown.
            float now = Time.GetTicksMsec() * 0.001f;
            if (_isSpeaking && now - _lastSpeakingTime > SpeakingCooldown)
            {
                _isSpeaking = false;
                OnSpeakingChanged?.Invoke(false);
            }

            if (_capture == null || _codec == null) return;

            int available = _capture.GetFramesAvailable();
            if (available <= 0) return;

            // GetBuffer returns Vector2 stereo frames; use X channel for mono.
            var frames     = _capture.GetBuffer(available);
            var newSamples = new float[frames.Length];
            for (int i = 0; i < frames.Length; i++)
                newSamples[i] = frames[i].X;

            // Append to accumulation buffer.
            int oldLen = _pending.Length;
            Array.Resize(ref _pending, oldLen + newSamples.Length);
            Array.Copy(newSamples, 0, _pending, oldLen, newSamples.Length);

            // Each Opus frame = 960 samples @ 48000 Hz.
            // Determine how many system-rate samples correspond to one Opus frame.
            int srcFrameSize = (int)Math.Round((double)OpusCodec.FrameSamples * _systemSampleRate / OpusCodec.SampleRate);

            while (_pending.Length >= srcFrameSize)
            {
                // Extract one source-rate chunk.
                var chunk     = new float[srcFrameSize];
                Array.Copy(_pending, chunk, srcFrameSize);

                // Shift pending buffer.
                int remaining  = _pending.Length - srcFrameSize;
                var newPending = new float[remaining];
                Array.Copy(_pending, srcFrameSize, newPending, 0, remaining);
                _pending = newPending;

                // Resample to 48000 Hz if necessary.
                float[] pcmFloat = _systemSampleRate == OpusCodec.SampleRate
                    ? chunk
                    : Resample(chunk, _systemSampleRate, OpusCodec.SampleRate);

                // Speaking detection on raw float samples.
                if (!Muted)
                {
                    float rms = 0f;
                    for (int i = 0; i < pcmFloat.Length; i++) rms += pcmFloat[i] * pcmFloat[i];
                    rms = Mathf.Sqrt(rms / pcmFloat.Length);
                    if (rms > SpeakingThreshold)
                    {
                        _lastSpeakingTime = now;
                        if (!_isSpeaking) { _isSpeaking = true; OnSpeakingChanged?.Invoke(true); }
                    }
                }

                // Convert float → short[].
                var pcm = new short[OpusCodec.FrameSamples];
                for (int i = 0; i < OpusCodec.FrameSamples; i++)
                {
                    pcm[i] = Muted
                        ? (short)0
                        : (short)(Math.Clamp(pcmFloat[i], -1f, 1f) * 32767f);
                }

                OnFrame?.Invoke(_codec.Encode(pcm));
            }
        }

        // Linear interpolation resampler (same algorithm as RemoteAudioPlayer in the Unity SDK).
        private static float[] Resample(float[] src, int srcRate, int dstRate)
        {
            int dstLen = (int)((long)src.Length * dstRate / srcRate);
            var dst    = new float[dstLen];
            for (int i = 0; i < dstLen; i++)
            {
                float pos  = (float)i * srcRate / dstRate;
                int   idx  = (int)pos;
                float frac = pos - idx;
                float a    = idx     < src.Length ? src[idx]     : 0f;
                float b    = idx + 1 < src.Length ? src[idx + 1] : 0f;
                dst[i]     = a + frac * (b - a);
            }
            return dst;
        }

        public override void _ExitTree() => StopCapture();
    }
}
