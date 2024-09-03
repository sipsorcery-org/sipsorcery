using System;
using System.Collections.Generic;
using System.Linq;
using SIPSorcery.net.RTP.RTPHeaderExtensions;
using SIPSorcery.Net;

namespace SIPSorcery.net.RTP
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
        public static RTPHeaderExtension GetRTPHeaderExtension(int id, String uri)
        {
            switch (uri)
            {
                case AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI:
                    return new AbsSendTimeExtension(id);

                case CVOExtension.RTP_HEADER_EXTENSION_URI:
                    return new CVOExtension(id);
            }
            return null;
        }

        /// <summary>
        /// To get a list of all RT
        /// </summary>
        /// <returns></returns>
        public static Dictionary<int, RTPHeaderExtension> GetRTPHeaderExtensions()
        {
            int index = 1;
            var result = new Dictionary<int, RTPHeaderExtension>
            {
                { index, new AbsSendTimeExtension(index++) },
                { index, new CVOExtension(index++) }
            };

            return result;
        }

        public RTPHeaderExtension(int id, string uri, RTPHeaderExtensionType type, params SDPMediaTypesEnum[] medias )
        {
            Id = id;
            Uri = uri;
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
        public int Id { get; }

        // Uri
        public string Uri { get; }

        // Data read of the RTPHeaderExtension
        public byte[] Data { get; set;  }

        // Medias supported by this extension - if null all media are supported
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

        // Function to call to get the payload when writting the RTP header
        public abstract byte[] WriteHeader();

        // Function to call when reading the RTP header
        public abstract void ReadHeader(ref MediaStreamTrack localTrack, ref MediaStreamTrack remoteTrack, RTPHeader header, byte[] data);
    }

    public class RTPHeaderExtensionUri
    {
        public enum Type{
            Unknown,
            AbsCaptureTime
        }

        private static Dictionary<string, Type> Types { get; } = new Dictionary<string, Type>() {{"http://www.webrtc.org/experiments/rtp-hdrext/abs-capture-time", Type.AbsCaptureTime}};

        public static Type? GetType(string uri) {
            if (!Types.ContainsKey(uri)) {
                return Type.Unknown;
            }

            return Types[uri];
        }
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
