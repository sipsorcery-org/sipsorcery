using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIPSorcery.SIP.App {
    public interface ISIPCallManager {
        string ProcessWebCallback(string username, string number);
        void CreateDialogueBridge(SIPDialogue firstLegDialogue, SIPDialogue secondLegDialogue, string owner);
        void ReInvite(SIPDialogue firstLegDialogue, string RemoteSDP);
        int GetCurrentCallCount(string owner);
    }
}
