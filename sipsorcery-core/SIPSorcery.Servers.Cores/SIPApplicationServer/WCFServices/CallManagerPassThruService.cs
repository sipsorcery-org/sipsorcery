using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Servers {

    public class CallManagerPassThruService : ICallManagerServices {

        private const int OPERATION_TIMEOUT = 5000;

        private static ILog logger = AppState.logger;

        private CallManagerServiceClient m_callManagerClient;

        public CallManagerPassThruService() {
           m_callManagerClient = new CallManagerServiceClient("CallManagerSvc");
        }

        public bool IsAlive() {
            try {
                logger.Debug("CallManagerPassThruService IsAlive.");

                bool isAlive = false;
                ManualResetEvent isAliveMRE = new ManualResetEvent(false);

                m_callManagerClient.IsAliveComplete += (s, a) => { isAlive = a.Result; isAliveMRE.Set(); };
                m_callManagerClient.IsAliveAsync();

                if (isAliveMRE.WaitOne(OPERATION_TIMEOUT)) {
                    return isAlive;
                }
                else {
                    throw new TimeoutException();
                }
            }
            catch (Exception excp) {
                logger.Error("Exception CallManagerPassThruService IsAlive. " + excp.Message);
                throw;
            }
        }

        public string WebCallback(string username, string number) {
            try {
                logger.Debug("CallManagerPassThruService WebCallback, username=" + username + ", number=" + number + ".");

                string result = null;
                ManualResetEvent webCallbackMRE = new ManualResetEvent(false);

                m_callManagerClient.WebCallbackComplete += (s, a) => { result = a.Result; webCallbackMRE.Set(); };
                m_callManagerClient.WebCallbackAsync(username, number);

                if (webCallbackMRE.WaitOne(OPERATION_TIMEOUT)) {
                    return result;
                }
                else {
                    throw new TimeoutException();
                }
            }
            catch (Exception excp) {
                logger.Error("Exception CallManagerPassThruService WebCallback. " + excp.Message);
                return "Sorry there was an unexpected error, the callback was not initiated.";
            }
        }
    }
}
