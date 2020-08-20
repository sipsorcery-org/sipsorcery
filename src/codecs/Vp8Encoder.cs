using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SIPSorceryMedia.Windows.Codecs
{
    public class Vp8Encoder
    {
        public const string LIBVPX_BASE_NAME = "vpxmd";

        [DllImport(LIBVPX_BASE_NAME)]
        static extern string vpx_codec_version_str();
    }
}
