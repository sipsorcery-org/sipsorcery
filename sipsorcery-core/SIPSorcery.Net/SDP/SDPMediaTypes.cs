using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIPSorcery.Net
{
    public enum SDPMediaTypesEnum
    {
        audio = 1,
        video = 2,
        application = 3,
        data = 4,
        control = 5,
    }

    public class SDPMediaTypes
    {
        public static SDPMediaTypesEnum GetSDPMediaType(string mediaType)
        {
            return (SDPMediaTypesEnum)Enum.Parse(typeof(SDPMediaTypesEnum), mediaType, true);
        }
    }
}
