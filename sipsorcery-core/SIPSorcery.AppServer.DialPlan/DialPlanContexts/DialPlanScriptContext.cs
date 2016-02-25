using System.Collections.Generic;
using SIPSorcery.CRM;
using SIPSorcery.Persistence;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

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
            Customer customer,
            DialPlanEngine dialPlanEngine,
            GetCanonicalDomainDelegate getCanonicalDomain)
            : base(monitorLogDelegate, sipTransport, createBridge, outboundProxy, sipServerUserAgent, dialPlan, sipProviders, traceDirectory, callersNetworkId, customer, dialPlanEngine, getCanonicalDomain)
        {
            ContextType = DialPlanContextsEnum.Script;
        }
    }
}
