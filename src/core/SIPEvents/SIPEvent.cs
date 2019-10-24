using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIPSorcery.SIP
{
    public class SIPEvent
    {
        public SIPEvent()
        { }

        public virtual void Load(string eventStr)
        {
            throw new NotImplementedException();
        }

        public virtual string ToXMLText()
        {
            throw new NotImplementedException();
        }
    }
}
