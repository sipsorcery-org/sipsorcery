using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using log4net;

namespace SIPSorcery.Servers
{
    public class DialPlanScriptContext : DialPlanContext
    {
        public DialPlanScriptContext(           
            SIPMonitorLogDelegate monitorLogDelegate,
            SIPTransport sipTransport,
            DialogueBridgeCreatedDelegate createBridge,
            SIPEndPoint outboundProxy,
            UASInviteTransaction clientTransaction,
            SIPDialPlan dialPlan,
            List<SIPProvider> sipProviders,
            string traceDirectory)
            : base(monitorLogDelegate, sipTransport, createBridge, outboundProxy, clientTransaction, dialPlan, sipProviders, traceDirectory)
        {
            ContextType = DialPlanContextsEnum.Script;
        }
    }
}
