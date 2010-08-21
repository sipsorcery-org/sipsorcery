using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using log4net;

namespace SIPSorcery.AppServer.DialPlan
{
    public class DialPlanScriptContext : DialPlanContext
    {
        public DialPlanScriptContext(           
            SIPMonitorLogDelegate monitorLogDelegate,
            SIPTransport sipTransport,
            DialogueBridgeCreatedDelegate createBridge,
            SIPEndPoint outboundProxy,
            ISIPServerUserAgent sipServerUserAgent,
            SIPDialPlan dialPlan,
            List<SIPProvider> sipProviders,
            string traceDirectory,
            string callersNetworkId,
            Guid customerId)
            : base(monitorLogDelegate, sipTransport, createBridge, outboundProxy, sipServerUserAgent, dialPlan, sipProviders, traceDirectory, callersNetworkId, customerId)
        {
            ContextType = DialPlanContextsEnum.Script;
        }
    }
}
