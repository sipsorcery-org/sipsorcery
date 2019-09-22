using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIPSorcery.Entities
{
    public enum ProviderTypes
    {
        SIP = 0,
        GoogleVoice = 1,
    }

    public enum GoogleVoiceCallbackTypes
    {
        Home = 1,
        Mobile = 2,
        Work = 3,
    }
}
