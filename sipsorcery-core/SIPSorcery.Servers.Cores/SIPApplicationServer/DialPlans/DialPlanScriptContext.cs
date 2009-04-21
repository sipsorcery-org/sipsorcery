using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIPSorcery.SIP.App;
using log4net;

namespace SIPSorcery.Servers
{
    public class DialPlanScriptContext : DialPlanContext
    {
        public DialPlanScriptContext(SIPDialPlan dialPlan, List<SIPProvider> sipProviders)
            : base(dialPlan, sipProviders)
        {
            ContextType = DialPlanContextsEnum.Script;
        }
    }
}
