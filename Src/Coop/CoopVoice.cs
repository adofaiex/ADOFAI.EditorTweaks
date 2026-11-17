#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using Steamworks;
using UnityEngine;

namespace ADOFAI.EditorTweaks.Coop
{
    /// <summary>
    /// 语音聊天（用 Steam 自带的语音：录音、压缩，并且只在有人说话时才产生数据）。
    /// <para>发送：麦克风开着时每帧取 Steam 压缩好的语音，客人发给房主，房主发给所有人（不可靠通道，丢包只是一点杂音）。</para>
    /// <para>播放：每个说话的人一个 AudioSource + 流式 AudioClip，收到的数据解压成 PCM 放进环形缓冲；
    /// 先攒 80ms 再开始放，避免网络抖动造成断断续续。</para>
    /// </summary>
    internal static class CoopVoice
    {
        private static readonly Dictionary<ulong, Speaker> Speakers = new Dictionary<ulong, Speaker>();
        private static readonly MemoryStream Decompressed = new MemoryStream();
        private static GameObject _host;
        private static int _sampleRate;
        private static float _lastSentAt = -10f;

        private static readonly HashSet<ulong> Muted = new HashSet<ulong>();

        public static bool MicOn => CoopSettings.Mic != MicMode.Off;

        public static float Volume
        {
            get => CoopSettings.Volume;
            set
            {
                CoopSettings.Volume = value;
                foreach (Speaker speaker in Speakers.Values) speaker.Source.volume = Mathf.Min(1f, CoopSettings.Volume);
            }
        }

        /// <summary>这个人被我静音了（只影响自己这边听不听得到）。</summary>
        public static bool IsMuted(ulong steamId)
        {
            return Muted.Contains(steamId);
        }

        public static void ToggleMute(ulong steamId)
        {
            if (!Muted.Remove(steamId)) Muted.Add(steamId);
            if (Speakers.TryGetValue(steamId, out Speaker speaker)) speaker.Source.mute = Muted.Contains(steamId);
        }

        /// <summary>自己刚刚在说话（0.3 秒内发出过语音）。</summary>
        public static bool SelfSpeaking => Time.unscaledTime - _lastSentAt < 0.3f;

        /// <summary>某人刚刚在说话（头像发绿光用）。</summary>
        public static bool IsSpeaking(ulong steamId)
        {
            if (SteamClient.IsValid && steamId == SteamClient.SteamId.Value) return SelfSpeaking;
            return Speakers.TryGetValue(steamId, out Speaker speaker) && Time.unscaledTime - speaker.LastPacketAt < 0.3f;
        }

        public static string StatusText
        {
            get
            {
                if (!CoopSession.IsConnected) return "进入房间后可以语音";
                switch (CoopSettings.Mic)
                {
                    case MicMode.Open: return SelfSpeaking ? "正在说话…" : "自由麦：直接说话即可";
                    case MicMode.PushToTalk: return SelfSpeaking ? "正在说话…" : "按住 " + CoopSettings.KeyName(CoopSettings.PushKey) + " 说话";
                    default: return "麦克风已关闭";
                }
            }
        }

        // ---------------------------------------------------------------- 每帧：录音并发送

        public static void Update()
        {
            MicMode mode = CoopSettings.Mic;
            bool keyHeld = mode == MicMode.PushToTalk && CoopSettings.PushKey != KeyCode.None && Input.GetKey(CoopSettings.PushKey) && !SettingsUi.IsTyping();
            bool record = (mode == MicMode.Open || keyHeld) && CoopSession.IsConnected && SteamClient.IsValid;
            try
            {
                if (SteamUser.VoiceRecord != record) SteamUser.VoiceRecord = record;
                if (!record) return;
                for (int guard = 0; guard < 8 && SteamUser.HasVoiceData; guard++)
                {
                    byte[] data = SteamUser.ReadVoiceDataBytes();
                    if (data == null || data.Length == 0) break;
                    _lastSentAt = Time.unscaledTime;
                    CoopSession.SendVoice(Pack(SteamClient.SteamId.Value, data));
                }
            }
            catch (Exception exception)
            {
                Main.Log("[Coop] Voice capture failed: " + exception.Message);
                CoopSettings.Mic = MicMode.Off;
            }
        }

        public static void Stop()
        {
            try
            {
                if (SteamClient.IsValid) SteamUser.VoiceRecord = false;
            }
            catch (Exception)
            {
                // Steam 已经关了
            }

            foreach (Speaker speaker in Speakers.Values) speaker.Dispose();
            Speakers.Clear();
            Muted.Clear();
            if (_host != null) UnityEngine.Object.Destroy(_host);
            _host = null;
        }

        public static byte[] Pack(ulong speaker, byte[] data)
        {
            var payload = new byte[data.Length + 8];
            for (int i = 0; i < 8; i++) payload[i] = (byte)(speaker >> (i * 8));
            Buffer.BlockCopy(data, 0, payload, 8, data.Length);
            return payload;
        }

        public static ulong SpeakerOf(byte[] payload)
        {
            ulong id = 0;
            for (int i = 0; i < 8 && i < payload.Length; i++) id |= (ulong)payload[i] << (i * 8);
            return id;
        }

        // ---------------------------------------------------------------- 收到语音：解压、放进对应的人的缓冲

        public static void Play(byte[] payload)
        {
            if (payload.Length <= 8 || !SteamClient.IsValid) return;
            ulong speakerId = SpeakerOf(payload);
            if (speakerId == SteamClient.SteamId.Value) return;
            try
            {
                if (_sampleRate == 0)
                {
                    _sampleRate = (int)SteamUser.OptimalSampleRate;
                    SteamUser.SampleRate = (uint)_sampleRate;
                }

                var compressed = new byte[payload.Length - 8];
                Buffer.BlockCopy(payload, 8, compressed, 0, compressed.Length);
                Decompressed.Position = 0;
                Decompressed.SetLength(0);
                int bytes = SteamUser.DecompressVoice(compressed, Decompressed);
                if (bytes <= 0) return;

                if (!Speakers.TryGetValue(speakerId, out Speaker speaker))
                {
                    speaker = new Speaker(Host(), _sampleRate, Volume);
                    speaker.Source.mute = Muted.Contains(speakerId);
                    Speakers[speakerId] = speaker;
                }

                speaker.LastPacketAt = Time.unscaledTime;
                speaker.Write(Decompressed.GetBuffer(), bytes);
            }
            catch (Exception exception)
            {
                Main.Log("[Coop] Voice playback failed: " + exception.Message);
            }
        }

        /// <summary>有人离开房间：停掉他的声音。</summary>
        public static void Forget(ulong steamId)
        {
            if (!Speakers.TryGetValue(steamId, out Speaker speaker)) return;
            speaker.Dispose();
            Speakers.Remove(steamId);
        }

        private static GameObject Host()
        {
            if (_host != null) return _host;
            _host = new GameObject("ADOFAI.EditorTweaks Voice");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            return _host;
        }

        /// <summary>一个说话的人：环形缓冲 + 流式 AudioClip（音频线程从缓冲里取样本）。</summary>
        private sealed class Speaker
        {
            private const float PrebufferSeconds = 0.08f;
            private readonly float[] _ring;
            private readonly int _prebuffer;
            private readonly object _lock = new object();
            private int _read;
            private int _count;
            private bool _playing;

            public Speaker(GameObject host, int sampleRate, float volume)
            {
                _ring = new float[sampleRate * 2];
                _prebuffer = (int)(sampleRate * PrebufferSeconds);
                Source = host.AddComponent<AudioSource>();
                Source.spatialBlend = 0f;
                Source.loop = true;
                Source.volume = Mathf.Min(1f, volume);
                Source.ignoreListenerPause = true;
                Source.clip = AudioClip.Create("voice", sampleRate, 1, sampleRate, true, OnRead);
                Source.Play();
            }

            public AudioSource Source { get; }

            public float LastPacketAt { get; set; }

            /// <summary>写入 16 位单声道 PCM（太多来不及放时丢掉最旧的，保证延迟不越积越大）。</summary>
            public void Write(byte[] pcm, int bytes)
            {
                float gain = Mathf.Max(1f, Volume); // 音量超过 100% 的部分在这里放大
                lock (_lock)
                {
                    for (int i = 0; i + 1 < bytes; i += 2)
                    {
                        float sample = (short)(pcm[i] | pcm[i + 1] << 8) / 32768f * gain;
                        if (_count == _ring.Length)
                        {
                            _read = (_read + 1) % _ring.Length;
                            _count--;
                        }

                        _ring[(_read + _count) % _ring.Length] = Mathf.Clamp(sample, -1f, 1f);
                        _count++;
                    }
                }
            }

            private void OnRead(float[] data)
            {
                lock (_lock)
                {
                    if (!_playing && _count >= _prebuffer) _playing = true;
                    for (int i = 0; i < data.Length; i++)
                    {
                        if (!_playing || _count == 0)
                        {
                            data[i] = 0f;
                            _playing = false; // 放空了：重新攒一点再放
                            continue;
                        }

                        data[i] = _ring[_read];
                        _read = (_read + 1) % _ring.Length;
                        _count--;
                    }
                }
            }

            public void Dispose()
            {
                if (Source == null) return;
                Source.Stop();
                UnityEngine.Object.Destroy(Source.clip);
                UnityEngine.Object.Destroy(Source);
            }
        }
    }
}
