using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Servers
{
    public enum DialPlanContextsEnum
    {
        None = 0,
        Line = 1,
        Script = 2,
    }

    public class DialPlanContext {
        protected static ILog logger = AppState.GetLogger("dialplan");

        protected SIPDialPlan m_dialPlan;
        protected List<SIPProvider> m_sipProviders;
        protected string m_initialTraceMessage = null;
        protected StringBuilder TraceLog;

        public DialPlanContextsEnum ContextType;

        public string Owner {
            get { return m_dialPlan.Owner; }
        }

        public string AdminMemberId {
            get { return m_dialPlan.AdminMemberId; }
        }

        public List<SIPProvider> SIPProviders {
            get { return m_sipProviders; }
        }

        public string DialPlanScript {
            get { return m_dialPlan.DialPlanScript; }
        }

        public DialPlanContext(SIPDialPlan dialPlan, List<SIPProvider> sipProviders) {
            m_dialPlan = dialPlan;
            m_sipProviders = sipProviders;
        }
    }
}
