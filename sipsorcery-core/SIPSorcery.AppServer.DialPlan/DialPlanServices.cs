using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.AppServer.DialPlan {

    public class DialPlanServices : IDialPlanServices {

        private static ILog logger = AppState.logger;

        private ISIPCallManager m_sipCallManager;

        public DialPlanServices() { }

        public DialPlanServices(ISIPCallManager sipCallManager) {
            m_sipCallManager = sipCallManager;
        }

        public bool IsAlive() {
            return true;
        }

        public string WebCallback(string username, string number) {
            try {
                return m_sipCallManager.ProcessWebCallback(username, number); 
            }
            catch (Exception excp) {
                logger.Error("Exception WebCallback. " + excp.Message);
                return "Sorry there was an unexpected error, the callback was not initiated.";
            }
        }
    }
}
