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
        void QueueNewCall(ISIPServerUserAgent serverUA);
        void AddWaitingApplication(CallbackWaiter callbackWaiter);
    }

    public enum CallbackWaiterEnum {
        None = 0,
        GoogleVoice = 1,
    }

    public class CallbackWaiter {

        public CallbackWaiterEnum CallbackApplication;
        public string UniqueId;
        public Func<ISIPServerUserAgent, bool> IsMyCall;
        public DateTime Added = DateTime.Now;

        public CallbackWaiter(CallbackWaiterEnum application, string uniqueId, Func<ISIPServerUserAgent, bool> isMyCall) {
            CallbackApplication = application;
            UniqueId = uniqueId;
            IsMyCall = isMyCall;
        }
    }
}
