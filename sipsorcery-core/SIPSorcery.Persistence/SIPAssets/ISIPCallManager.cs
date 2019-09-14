using System;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Persistence
{
    public interface ISIPCallManager
    {
        string ProcessWebCall(string username, string number, string dialplanName, string replacesCallID);
        string ProcessCallback(string username, string dialString1, string dialString2);
        void CreateDialogueBridge(SIPDialogue firstLegDialogue, SIPDialogue secondLegDialogue, string owner);
        void ReInvite(SIPDialogue firstLegDialogue, SIPDialogue substituteDialogue);
        int GetCurrentCallCount(string owner);
        void QueueNewCall(ISIPServerUserAgent serverUA);
        void AddWaitingApplication(CallbackWaiter callbackWaiter);
    }

    public enum CallbackWaiterEnum
    {
        None = 0,
        GoogleVoice = 1,
    }

    public class CallbackWaiter
    {
        public string Owner;
        public CallbackWaiterEnum CallbackApplication;
        public string UniqueId;
        public Func<ISIPServerUserAgent, bool> IsMyCall;
        public DateTime Added = DateTime.Now;

        public CallbackWaiter(string owner, CallbackWaiterEnum application, string uniqueId, Func<ISIPServerUserAgent, bool> isMyCall)
        {
            Owner = owner;
            CallbackApplication = application;
            UniqueId = uniqueId;
            IsMyCall = isMyCall;
        }
    }
}
