using System;
using System.Collections.Generic;
using System.Linq;

namespace SIPSorceryMedia.Abstractions
{
    public class CodecManager<T> where T : System.Enum
    {
        public readonly List<T> SupportedCodecs = new List<T>();

        public T SelectedCodec { get; private set; }
        private List<T> _supportedCodecs = new List<T>();

        public CodecManager(List<T> supportedCodecs)
        {
            SupportedCodecs = supportedCodecs;
        }

        public List<T> GetSourceFormats()
        {
            return _supportedCodecs;
        }

        public void RestrictCodecs(List<T> codecs)
        {
            if (codecs == null || codecs.Count == 0)
            {
                _supportedCodecs = new List<T>(SupportedCodecs);
            }
            else
            {
                _supportedCodecs = new List<T>();
                foreach (var codec in codecs)
                {
                    if (SupportedCodecs.Any(x => x.Equals(codec)))
                    {
                        _supportedCodecs.Add(codec);
                    }
                    else
                    {
                        throw new ApplicationException($"Unsupported codec {codec} for RestrictCodecs.");
                    }
                }
            }
        }

        public void SetSelectedCodec(T codec)
        {
            if(_supportedCodecs.Contains(codec))
            {
                SelectedCodec = codec;
            }
            else
            {
                throw new ApplicationException($"Codec {codec} is not available for SetSelectedCodec.");
            }
        }
    }
}
