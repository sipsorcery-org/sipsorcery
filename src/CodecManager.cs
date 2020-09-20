using System;
using System.Collections.Generic;
using System.Linq;

namespace SIPSorceryMedia.Abstractions
{
    public class CodecManager<T> where T : System.Enum
    {
        public readonly List<T> SupportedCodecs = new List<T>();

        public T SelectedCodec { get; private set; }
        private List<T> _filteredCodecs = new List<T>();

        public CodecManager(List<T> supportedCodecs)
        {
            SupportedCodecs = supportedCodecs;
            _filteredCodecs = new List<T>(SupportedCodecs);
        }

        public List<T> GetSourceFormats()
        {
            return _filteredCodecs;
        }

        public void RestrictCodecs(List<T> codecs)
        {
            if (codecs == null || codecs.Count == 0)
            {
                _filteredCodecs = new List<T>(SupportedCodecs);
            }
            else
            {
                _filteredCodecs = new List<T>();
                foreach (var codec in codecs)
                {
                    if (SupportedCodecs.Any(x => x.Equals(codec)))
                    {
                        _filteredCodecs.Add(codec);
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
            if(_filteredCodecs.Contains(codec))
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
