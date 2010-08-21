using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Servers {

    public class CallManagerPassThruService : ICallManagerServices {

        private const int OPERATION_TIMEOUT = 5000;

        private static ILog logger = AppState.logger;

        private CallManagerServiceClient m_callManagerClient;
        private SIPCallDispatcher m_sipCallDispatcher;

        public CallManagerPassThruService() {
           m_callManagerClient = new CallManagerServiceClient("CallManagerSvc");
        }

        public CallManagerPassThruService(SIPCallDispatcher sipCallDispatcher) {
            m_sipCallDispatcher = sipCallDispatcher;
        }

        public bool IsAlive() {
            try {
                logger.Debug("CallManagerPassThruService IsAlive.");

                bool isAlive = false;
                ManualResetEvent isAliveMRE = new ManualResetEvent(false);

                CallManagerServiceClient client = (m_sipCallDispatcher != null) ? m_sipCallDispatcher.GetCallManagerClient() : m_callManagerClient;

                if (client != null) {
                    logger.Debug("Sending isalive request to client endpoint at " + client.Endpoint.Address.ToString() + ".");

                    client.IsAliveComplete += (s, a) => {
                        try {
                            isAlive = a.Result;
                            isAliveMRE.Set();
                        }
                        catch (Exception excp) {
                            logger.Error("Exception IsAliveComplete. " + excp.Message);
                            isAlive = false;
                        }
                    };
                    client.IsAliveAsync();

                    if (isAliveMRE.WaitOne(OPERATION_TIMEOUT)) {
                        return isAlive;
                    }
                    else {
                        throw new TimeoutException();
                    }
                }
                else {
                    throw new ApplicationException("Call Manager Pass Thru service could not create a client.");
                }
            }
            catch (Exception excp) {
                logger.Error("Exception CallManagerPassThruService IsAlive. " + excp.Message);
                //throw;
                return false;
            }
        }

        public string WebCallback(string username, string number) {
            try {
                logger.Debug("CallManagerPassThruService WebCallback, username=" + username + ", number=" + number + ".");

                string result = null;
                ManualResetEvent webCallbackMRE = new ManualResetEvent(false);

                CallManagerServiceClient client = (m_sipCallDispatcher != null) ? m_sipCallDispatcher.GetCallManagerClient() : m_callManagerClient;

                if (client != null) {
                    logger.Debug("Sending webcallback request to client endpoint at " + client.Endpoint.Address.ToString() + ".");

                    client.WebCallbackComplete += (s, a) => {
                        try {
                            if(a != null) {
                                if(a.Error != null) {
                                    result = a.Error.Message;
                                }
                                else {
                                    result = a.Result; 
                                }
                            } 
                            webCallbackMRE.Set();
                        }
                        catch (Exception excp) {
                            logger.Debug("Exception CallManagerPassThruService WebCallbackComplete. " + excp.Message);
                            result = excp.Message;
                        }
                    };

                    client.WebCallbackAsync(username, number);

                    if (webCallbackMRE.WaitOne(OPERATION_TIMEOUT)) {
                        return result;
                    }
                    else {
                        throw new TimeoutException();
                    }
                }
                else {
                    throw new ApplicationException("Call Manager Pass Thru service could not create a client.");
                }
            }
            catch (Exception excp) {
                logger.Error("Exception CallManagerPassThruService WebCallback. " + excp.Message);
                return "Sorry there was an unexpected error, the callback was not initiated.";
            }
        }
    }
}
