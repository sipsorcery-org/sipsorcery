using SIPSorcery.Net;
using System;

namespace SIPSorcery.net.RTP.RTPHeaderExtensions
{
    public class AudioLevelExtension : RTPHeaderExtension
    {
        public const string RTP_HEADER_EXTENSION_URI = "urn:ietf:params:rtp-hdrext:ssrc-audio-level";
        public const int RTP_HEADER_EXTENSION_SIZE = 1;

        public event Action<Boolean> OnVoiceChange;
        public event Action<ushort> OnLevelChange;

        private Boolean _voice = false;
        private ushort _level = 0;

        public AudioLevelExtension(int id) : base(id, RTP_HEADER_EXTENSION_URI, RTP_HEADER_EXTENSION_SIZE, RTPHeaderExtensionType.OneByte, Net.SDPMediaTypesEnum.audio)
        {
        }

        public void SetVoice(Boolean voice)
        {
            if(_voice != voice)
            {
                _voice = voice;
                OnVoiceChange?.Invoke(voice);
            }
        }

        public void SetLevel(ushort level)
        {
            if(level > 127)
            {
                return;
            }

            if (_level != level)
            {
                _level = level;
                OnLevelChange?.Invoke(_level);
            }
        }

        public override byte[] Marshal()
        {
            byte voice = 0;
            if (_voice)
            {
                voice = 0x80;
            };

            return new[]
            {
                (byte)((Id << 4) | ExtensionSize - 1),
                (byte)(voice | _level)
            };
        }

        public override void Unmarshal(ref MediaStreamTrack localTrack, ref MediaStreamTrack remoteTrack, RTPHeader header, byte[] data)
        {
            if (data.Length == ExtensionSize)
            {
                SetLevel((ushort)(data[0] & 0x7F));
                SetVoice((data[0] & 0x80) != 0);
            }
        }

    }
}
