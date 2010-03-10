using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIPSorcery.SIP
{
    public interface ISIPEvent
    {
        void Load(string eventStr);
    }
}
