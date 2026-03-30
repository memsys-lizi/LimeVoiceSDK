using System;
using System.Collections;
using UnityEngine;

namespace LimeVoice.Audio
{
    public class MicrophoneCapture : MonoBehaviour
    {
        public event Action<byte[]> OnFrame;

        public event Action<bool> OnSpeakingChanged;
        public float SpeakingThreshold = 0.01f;

        private bool  _isSpeaking;
        private float _lastSpeakingTime;
        private const float SpeakingCooldown = 0.3f;

        private const int ClipLengthSec = 2;

        private OpusCodec _codec;
        private AudioClip _clip;
        private int       _lastReadPos;
        private bool      _capturing;
        public  bool      Muted;

        private readonly short[] _pcmBuf   = new short[OpusCodec.FrameSamples];
        private readonly float[] _floatBuf = new float[OpusCodec.FrameSamples];

        public void StartCapture()
        {
            if (_capturing) return;
            _codec = new OpusCodec();
            _clip  = Microphone.Start(null, true, ClipLengthSec, OpusCodec.SampleRate);
            StartCoroutine(WaitAndCapture());
        }

        public void StopCapture()
        {
            _capturing = false;
            StopAllCoroutines();
            Microphone.End(null);
            _codec?.Dispose();
            _codec = null;
        }

        private IEnumerator WaitAndCapture()
        {
            while (Microphone.GetPosition(null) <= 0)
                yield return null;

            _lastReadPos = 0;
            _capturing   = true;
        }

        // Poll every frame — more reliable than WaitForSecondsRealtime which drifts with framerate.
        private void Update()
        {
            if (_capturing && _clip != null)
            {
                int currentPos   = Microphone.GetPosition(null);
                int totalSamples = _clip.samples;
                int available    = (currentPos - _lastReadPos + totalSamples) % totalSamples;
                while (available >= OpusCodec.FrameSamples)
                {
                    SendFrame();
                    available -= OpusCodec.FrameSamples;
                }
            }

            // Speaking cooldown check.
            if (_isSpeaking && Time.realtimeSinceStartup - _lastSpeakingTime > SpeakingCooldown)
            {
                _isSpeaking = false;
                OnSpeakingChanged?.Invoke(false);
            }
        }

        private void SendFrame()
        {
            _clip.GetData(_floatBuf, _lastReadPos);
            _lastReadPos = (_lastReadPos + OpusCodec.FrameSamples) % _clip.samples;

            if (Muted)
            {
                Array.Clear(_pcmBuf, 0, _pcmBuf.Length);
            }
            else
            {
                for (int i = 0; i < OpusCodec.FrameSamples; i++)
                    _pcmBuf[i] = (short)(Math.Clamp(_floatBuf[i], -1f, 1f) * 32767f);

                // Speaking detection on raw float samples before encoding.
                float rms = 0f;
                for (int i = 0; i < _floatBuf.Length; i++) rms += _floatBuf[i] * _floatBuf[i];
                rms = Mathf.Sqrt(rms / _floatBuf.Length);
                if (rms > SpeakingThreshold)
                {
                    _lastSpeakingTime = Time.realtimeSinceStartup;
                    if (!_isSpeaking) { _isSpeaking = true; OnSpeakingChanged?.Invoke(true); }
                }
            }

            OnFrame?.Invoke(_codec.Encode(_pcmBuf));
        }

        private void OnDestroy() => StopCapture();
    }
}