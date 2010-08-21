using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Servers {

    public class CallManagerServices : ICallManagerServices {

        private static ILog logger = AppState.logger;

        private ISIPCallManager m_sipCallManager;

        public CallManagerServices() { }

        public CallManagerServices(ISIPCallManager sipCallManager) {
            m_sipCallManager = sipCallManager;
        }

        public bool IsAlive() {
            return true;
        }

        public string WebCallback(string username, string number) {
            try {
                logger.Debug("CallManagerServices webcallback, username=" + username + ", number=" + number + ".");
                return m_sipCallManager.ProcessWebCallback(username, number); 
            }
            catch (Exception excp) {
                logger.Error("Exception WebCallback. " + excp.Message);
                throw;
            }
        }
    }
}
