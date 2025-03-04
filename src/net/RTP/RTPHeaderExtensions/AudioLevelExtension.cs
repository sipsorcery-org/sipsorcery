using System;

namespace SIPSorcery.Net
{
    // Code reference: https://chromium.googlesource.com/external/webrtc/+/e2a017725570ead5946a4ca8235af27470ca0df9/webrtc/modules/rtp_rtcp/source/rtp_header_extensions.cc#49
    //    0                   1
    //    0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5
    //   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    //   |  ID   | len=0 |V|   level     |
    //   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    public class AudioLevelExtension : RTPHeaderExtension
    {
        public class AudioLevel
        {
            public Boolean Voice;
            public ushort Level;

            public AudioLevel()
            {
                Voice = false;
                Level = 0;
            }

            public AudioLevel(byte[] data)
            {
                if ((data == null) || (data.Length != AudioLevelExtension.RTP_HEADER_EXTENSION_SIZE))
                {
                    throw new ArgumentException(nameof(data));
                }

                Voice = (data[0] & 0x80) != 0;
                Level = (ushort)(data[0] & 0x7F);
            }
        };

        public const string RTP_HEADER_EXTENSION_URI = "urn:ietf:params:rtp-hdrext:ssrc-audio-level";
        internal const int RTP_HEADER_EXTENSION_SIZE = 1;

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
                _audioLevel = audioLevel;
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
            try
            {
                if ((data == null) || (data.Length != AudioLevelExtension.RTP_HEADER_EXTENSION_SIZE))
                {
                    _audioLevel = new AudioLevel(data);
                }
            }
            catch
            {
                // Nothing to do more
            }
            return _audioLevel;
        }
    }
}
