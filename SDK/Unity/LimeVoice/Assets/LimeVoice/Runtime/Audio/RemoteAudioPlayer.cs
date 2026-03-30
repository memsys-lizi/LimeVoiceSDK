using System;
using System.Collections.Generic;
using UnityEngine;

namespace LimeVoice.Audio
{
    [RequireComponent(typeof(AudioSource))]
    public class RemoteAudioPlayer : MonoBehaviour
    {
        private const int MaxQueueFrames = 20;

        private OpusCodec      _codec;
        private Queue<float[]> _queue;
        private float[]        _remainder;
        private int            _remainderOffset;
        private int            _outputSampleRate;

        public string UserId { get; private set; }

        public event Action<bool> OnSpeakingChanged; // true = started, false = stopped
        public float SpeakingThreshold = 0.01f;

        private bool  _isSpeaking;
        private float _lastSpeakingTime;
        private const float SpeakingCooldown = 0.3f;

        public void Init(string userId)
        {
            UserId            = userId;
            _outputSampleRate = AudioSettings.outputSampleRate;
            _codec            = new OpusCodec();
            _queue            = new Queue<float[]>();

            var src          = GetComponent<AudioSource>();
            src.spatialBlend = 0f;   // 2D audio — no positional processing
            src.loop         = true;
            src.clip         = AudioClip.Create("silence", _outputSampleRate, 1, _outputSampleRate, false);
            src.clip.SetData(new float[_outputSampleRate], 0);
            src.Play();

            if (_outputSampleRate != OpusCodec.SampleRate)
                Debug.LogWarning($"[LimeVoice] Audio system is {_outputSampleRate} Hz, Opus is {OpusCodec.SampleRate} Hz — resampling enabled.");
        }

        // Called from the main thread when a VOICE_FWD packet arrives.
        public void Feed(byte[] opusData)
        {
            short[] pcm    = _codec.Decode(opusData);
            float[] floats = new float[pcm.Length];
            for (int i = 0; i < pcm.Length; i++)
                floats[i] = pcm[i] / 32768f;

            // Resample from 48000 Hz to Unity's output sample rate if they differ.
            if (_outputSampleRate != OpusCodec.SampleRate)
                floats = Resample(floats, OpusCodec.SampleRate, _outputSampleRate);

            // Speaking detection — RMS on the resampled frame.
            float rms = CalculateRms(floats);
            if (rms > SpeakingThreshold)
            {
                _lastSpeakingTime = Time.realtimeSinceStartup;
                if (!_isSpeaking) { _isSpeaking = true; OnSpeakingChanged?.Invoke(true); }
            }

            lock (_queue)
            {
                if (_queue.Count >= MaxQueueFrames)
                    _queue.Dequeue();
                _queue.Enqueue(floats);
            }
        }

        private void Update()
        {
            if (_isSpeaking && Time.realtimeSinceStartup - _lastSpeakingTime > SpeakingCooldown)
            {
                _isSpeaking = false;
                OnSpeakingChanged?.Invoke(false);
            }
        }

        // Called by Unity's audio engine on the audio thread.
        private void OnAudioFilterRead(float[] data, int channels)
        {
            int needed = data.Length / channels;
            int filled = 0;

            while (filled < needed)
            {
                if (_remainder != null)
                {
                    int take = Math.Min(_remainder.Length - _remainderOffset, needed - filled);
                    for (int i = 0; i < take; i++)
                    {
                        float s = _remainder[_remainderOffset + i];
                        for (int c = 0; c < channels; c++)
                            data[(filled + i) * channels + c] = s;
                    }
                    filled           += take;
                    _remainderOffset += take;
                    if (_remainderOffset >= _remainder.Length)
                    {
                        _remainder       = null;
                        _remainderOffset = 0;
                    }
                    continue;
                }

                float[] frame = null;
                lock (_queue)
                {
                    if (_queue.Count > 0)
                        frame = _queue.Dequeue();
                }
                if (frame == null) break; // underrun — silence
                _remainder       = frame;
                _remainderOffset = 0;
            }
        }

        // Linear interpolation resampler.
        private static float[] Resample(float[] src, int srcRate, int dstRate)
        {
            int    dstLen = (int)((long)src.Length * dstRate / srcRate);
            float[] dst   = new float[dstLen];
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

        private void OnDestroy() => _codec?.Dispose();

        private static float CalculateRms(float[] samples)
        {
            float sum = 0f;
            for (int i = 0; i < samples.Length; i++) sum += samples[i] * samples[i];
            return Mathf.Sqrt(sum / samples.Length);
        }
    }
}