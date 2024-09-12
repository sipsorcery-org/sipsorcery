using SIPSorcery.Net;
using System;

namespace SIPSorcery.net.RTP.RTPHeaderExtensions
{
    public class AudioLevelExtension : RTPHeaderExtension
    {
        public class AudioLevel
        {
            public Boolean Voice;
            public ushort Level;
        };

        public const string RTP_HEADER_EXTENSION_URI = "urn:ietf:params:rtp-hdrext:ssrc-audio-level";
        private const int RTP_HEADER_EXTENSION_SIZE = 1;

        private AudioLevel _audioLevel;

        public AudioLevelExtension(int id) : base(id, RTP_HEADER_EXTENSION_URI, RTP_HEADER_EXTENSION_SIZE, RTPHeaderExtensionType.OneByte, Net.SDPMediaTypesEnum.audio)
        {
            _audioLevel = new AudioLevel()
            {
                Voice = false,
                Level = 0
            };
        }

        /// <summary>
        /// To set Audio Level
        /// </summary>
        /// <param name="value">An <see cref="AudioLevel"/> object is expected here</param>
        public override void Set(Object value) 
        { 
            if (value is AudioLevel audioLevel) 
            {
                SetVoice(audioLevel.Voice);
                SetLevel(audioLevel.Level);
            }
        }

        public void SetVoice(Boolean voice)
        {
            if(_audioLevel.Voice != voice)
            {
                _audioLevel.Voice = voice;
            }
        }

        public void SetLevel(ushort level)
        {
            if(level > 127)
            {
                return;
            }

            if (_audioLevel.Level != level)
            {
                _audioLevel.Level = level;
            }
        }

        public override byte[] Marshal()
        {
            byte voice = 0;
            if (_audioLevel.Voice)
            {
                voice = 0x80;
            };

            return new[]
            {
                (byte)((Id << 4) | ExtensionSize - 1),
                (byte)(voice | _audioLevel.Level)
            };
        }

        public override Object Unmarshal(RTPHeader header, byte[] data)
        {
            if (data.Length == ExtensionSize)
            {
                SetLevel((ushort)(data[0] & 0x7F));
                SetVoice((data[0] & 0x80) != 0);
            }
            return _audioLevel;
        }
    }
}
