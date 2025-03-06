using System;
using System.Collections.Generic;
using System.Linq;

namespace SIPSorcery.Net
{
    public abstract class RTPHeaderExtension
    {
        /// <summary>
        /// Create an RTPHeaderExtension (<see cref="AbsSendTimeExtension"/>, <see cref="CVOExtension"/>, etc ...) based on the URI provided
        /// If found, id permits to store the "extmap" value related to this extension
        /// It not found returns null
        /// </summary>
        /// <param name="id">extmap value</param>
        /// <param name="uri">URI of the extension - for example: "http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time" or "urn:3gpp:video-orientation" </param>
        /// <returns>A Specific RTPHeaderExtension</returns>
        public static RTPHeaderExtension GetRTPHeaderExtension(int id, string uri, SDPMediaTypesEnum media)
        {
            RTPHeaderExtension result = null;
            switch (uri)
            {
                case AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI:
                    result = new AbsSendTimeExtension(id);
                    break;

                case CVOExtension.RTP_HEADER_EXTENSION_URI:
                    result = new CVOExtension(id);
                    break;

                case AudioLevelExtension.RTP_HEADER_EXTENSION_URI:
                    result = new AudioLevelExtension(id);
                    break;

                case TransportWideCCExtension.RTP_HEADER_EXTENSION_URI:
                //case TransportWideCCExtension.RTP_HEADER_EXTENSION_URI_ALT:
                    result = new TransportWideCCExtension(id);
                    break;
            }

            if ( (result != null) &&  result.IsMediaSupported(media) )
            {
                return result;
            }

            return null;
        }

        /// <summary>
        /// To create a RTP Header Extension
        /// </summary>
        /// <param name="id"><see cref="int"/> Id / extmap</param>
        /// <param name="uri"><see cref="String"/>uri</param>
        /// <param name="type"><see cref="RTPHeaderExtension"/>type (one or two bytes)</param>
        /// <param name="medias"><see cref="SDPMediaTypesEnum"/>media(s) supported by this extension - set null/empty if all medias are supported</param>
        public RTPHeaderExtension(int id, string uri, int extensionSize, RTPHeaderExtensionType type, params SDPMediaTypesEnum[] medias )
        {
            Id = id;
            Uri = uri;
            ExtensionSize = extensionSize;
            Type = type;

            if (medias != null)
            {
                Medias = medias.ToList();
            }
            else
            {
                Medias = new List<SDPMediaTypesEnum>();
            }
        }

        // Id / "extmap"
        public int Id { get; internal set; }

        // Uri
        public string Uri { get; }

        public int ExtensionSize { get; }

        // Medias supported by this extension - if null/empty all medias are supported
        public List<SDPMediaTypesEnum> Medias { get;}

        // Type (one or two bytes)
        public RTPHeaderExtensionType Type { get; }

        public Boolean IsMediaSupported(SDPMediaTypesEnum media)
        {
            if (Medias.Count == 0)
            {
                return true;
            }

            return Medias.Contains(media);
        }

        // Function to call to set a new value to this extension
        public abstract void Set(Object obj);

        // Function to call to get the payload when writting the RTP header
        public abstract byte[] Marshal();

        // Function to call when reading the RTP header
        public abstract Object Unmarshal(RTPHeader header, byte[] data);
    }

    public enum RTPHeaderExtensionType
    {
        OneByte,
        TwoByte
    }

    public class RTPHeaderExtensionData
    {
        public RTPHeaderExtensionData(int id, byte[] data, RTPHeaderExtensionType type)
        {
            Id = id;
            Data = data;
            Type = type;
        }
        public int Id { get; }
        public byte[] Data { get; }
        public RTPHeaderExtensionType Type { get; }
    }
}
