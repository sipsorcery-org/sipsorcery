using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.net.RTP
{
    public class RTPHeaderExtension
    {
        public RTPHeaderExtension(int id, string uri)
        {
            Id = id;
            Uri = uri;
        }
        public int Id { get; }
        public string Uri { get; }

        public RTPHeaderExtensionUri.Type? Type => RTPHeaderExtensionUri.GetType(Uri);
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

        public RTPHeaderExtensionUri.Type? GetUriType(Dictionary<int, RTPHeaderExtension> map) {
            return !map.ContainsKey(Id) ? null : map[Id].Type;
        }


        public ulong? GetNtpTimestamp(Dictionary<int, RTPHeaderExtension> extensions){
            var extensionType = GetUriType(extensions);
            if (extensionType != RTPHeaderExtensionUri.Type.AbsCaptureTime) {
                return null;
            }

            return GetUlong(0);
        }

        public ulong? GetUlong(int offset) {
            if (offset + sizeof(ulong) - 1 >= Data.Length) {
                return null;
            }

            return BitConverter.IsLittleEndian ? 
                NetConvert.DoReverseEndian(BitConverter.ToUInt64(Data, offset)) : 
                BitConverter.ToUInt64(Data, offset);
        }
    }
}
