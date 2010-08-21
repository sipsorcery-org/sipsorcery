using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIPSorcery.SIP.App
{
    public interface ISIPDialogueManager
    {
        void DualTransfer(string username, string callID1, string callID2);
    }
}
