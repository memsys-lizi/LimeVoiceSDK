using System;
using System.Collections.Generic;
using Godot;

namespace LimeVoice.Audio
{
    // One instance per remote user. Decodes incoming Opus frames and plays them via
    // AudioStreamGenerator, which accepts real-time audio pushed from _Process.
    public partial class RemoteAudioPlayer : Node
    {
        private const int   MaxQueueFrames  = 20;
        private const float SpeakingCooldown = 0.3f;

        private OpusCodec?                    _codec;
        private AudioStreamPlayer?            _player;
        private AudioStreamGeneratorPlayback? _playback;

        private readonly Queue<float[]> _queue = new Queue<float[]>();
        private float[]? _remainder;
        private int      _remainderOffset;

        public string UserId { get; private set; } = string.Empty;

        public event Action<bool>? OnSpeakingChanged;
        public float SpeakingThreshold = 0.01f;

        private bool  _isSpeaking;
        private float _lastSpeakingTime;

        public void Init(string userId)
        {
            UserId = userId;
            _codec = new OpusCodec();

            // AudioStreamGenerator at 48000 Hz — matches Opus output, no resampling needed.
            var gen = new AudioStreamGenerator
            {
                MixRate     = OpusCodec.SampleRate,
                BufferLength = 0.1f   // 100 ms jitter buffer
            };

            _player        = new AudioStreamPlayer();
            _player.Stream = gen;
            AddChild(_player);
            _player.Play();

            _playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
        }

        // Called from the main thread when a VOICE_FWD packet arrives.
        public void Feed(byte[] opusData)
        {
            if (_codec == null) return;

            short[] pcm    = _codec.Decode(opusData);
            float[] floats = new float[pcm.Length];
            for (int i = 0; i < pcm.Length; i++)
                floats[i] = pcm[i] / 32768f;

            // Speaking detection — RMS on decoded frame.
            float rms = 0f;
            for (int i = 0; i < floats.Length; i++) rms += floats[i] * floats[i];
            rms = Mathf.Sqrt(rms / floats.Length);
            if (rms > SpeakingThreshold)
            {
                _lastSpeakingTime = Time.GetTicksMsec() * 0.001f;
                if (!_isSpeaking) { _isSpeaking = true; OnSpeakingChanged?.Invoke(true); }
            }

            lock (_queue)
            {
                if (_queue.Count >= MaxQueueFrames) _queue.Dequeue();
                _queue.Enqueue(floats);
            }
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

            if (_playback == null) return;

            int framesAvailable = _playback.GetFramesAvailable();
            int filled          = 0;

            while (filled < framesAvailable)
            {
                if (_remainder != null)
                {
                    while (_remainderOffset < _remainder.Length && filled < framesAvailable)
                    {
                        float s = _remainder[_remainderOffset++];
                        _playback.PushFrame(new Vector2(s, s)); // mono → stereo
                        filled++;
                    }
                    if (_remainderOffset >= _remainder.Length)
                    {
                        _remainder       = null;
                        _remainderOffset = 0;
                    }
                    continue;
                }

                float[]? frame = null;
                lock (_queue)
                {
                    if (_queue.Count > 0) frame = _queue.Dequeue();
                }
                if (frame == null) break; // underrun — silence
                _remainder       = frame;
                _remainderOffset = 0;
            }
        }

        public override void _ExitTree() => _codec?.Dispose();
    }
}
