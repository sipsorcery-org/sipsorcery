using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Web.Services
{
    public class CallManagerPassThruService : ICallManagerServices
    {
        private const int OPERATION_TIMEOUT = 5000;

        private static ILog logger = AppState.logger;

        private CallManagerProxy m_callManagerClient;
        private ISIPCallDispatcher m_sipCallDispatcher;

        private bool _callbacksAllowed = false;     // The callback method MUST ONLY support authenticated requests.

        public CallManagerPassThruService()
        {
            m_callManagerClient = new CallManagerProxy();
        }

        public CallManagerPassThruService(ISIPCallDispatcher sipCallDispatcher)
        {
            m_sipCallDispatcher = sipCallDispatcher;
            _callbacksAllowed = true;
        }

        public bool IsAlive()
        {
            try
            {
                logger.Debug("CallManagerPassThruService IsAlive.");

                CallManagerProxy client = (m_sipCallDispatcher != null) ? m_sipCallDispatcher.GetCallManagerClient() : m_callManagerClient;

                if (client != null)
                {
                    logger.Debug("Sending isalive request to client endpoint at " + client.Endpoint.Address.ToString() + ".");
                    return client.IsAlive();
                }
                else
                {
                    throw new ApplicationException("Call Manager Pass Thru service could not create a client.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception CallManagerPassThruService IsAlive. " + excp.Message);
                return false;
            }
        }

        public string WebCallback(string username, string number)
        {
            try
            {
                logger.Debug("CallManagerPassThruService WebCallback, username=" + username + ", number=" + number + ".");

                CallManagerProxy client = (m_sipCallDispatcher != null) ? m_sipCallDispatcher.GetCallManagerClient() : m_callManagerClient;

                if (client != null)
                {
                    logger.Debug("Sending WebCallback request to client endpoint at " + client.Endpoint.Address.ToString() + ".");
                    return client.WebCallback(username, number);
                }
                else
                {
                    throw new ApplicationException("Call Manager Pass Thru service could not create a client.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception CallManagerPassThruService WebCallback. " + excp.Message);
                return "Sorry there was an unexpected error, the web callback was not initiated.";
            }
        }

        public string BlindTransfer(string username, string destination, string replacesCallID)
        {
            try
            {
                logger.Debug("CallManagerPassThruService BlindTransfer, username=" + username + ", destination=" + destination + ".");

                CallManagerProxy client = (m_sipCallDispatcher != null) ? m_sipCallDispatcher.GetCallManagerClient() : m_callManagerClient;

                if (client != null)
                {
                    logger.Debug("Sending BlindTransfer request to client endpoint at " + client.Endpoint.Address.ToString() + ".");
                    return client.BlindTransfer(username, destination, replacesCallID);
                }
                else
                {
                    throw new ApplicationException("Call Manager Pass Thru service could not create a client.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception CallManagerPassThruService BlindTransfer. " + excp.Message);
                return "Sorry there was an unexpected error, the blind transfer was not initiated.";
            }
        }

        public string DualTransfer(string username, string callID1, string callID2)
        {
            try
            {
                logger.Debug("CallManagerPassThruService DualTransfer, callID1=" + callID1 + ", callID2=" + callID2 + ".");

                CallManagerProxy client = (m_sipCallDispatcher != null) ? m_sipCallDispatcher.GetCallManagerClient() : m_callManagerClient;

                if (client != null)
                {
                    logger.Debug("Sending DualTransfer request to client endpoint at " + client.Endpoint.Address.ToString() + ".");
                    return client.DualTransfer(username, callID1, callID2);
                }
                else
                {
                    throw new ApplicationException("Call Manager Pass Thru service could not create a client.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception CallManagerPassThruService DualTransfer. " + excp.Message);
                return "Sorry there was an unexpected error, the dual transfer was not initiated.";
            }
        }

        public string Callback(string username, string dialString1, string dialString2)
        {
            try
            {
                if (!_callbacksAllowed)
                {
                    return "This server does not support the Callback method.";
                }
                else
                {
                    logger.Debug("CallManagerPassThruService Callback, username=" + username + ", dialString1=" + dialString1 + ", dialString2=" + dialString2 + ".");

                    CallManagerProxy client = (m_sipCallDispatcher != null) ? m_sipCallDispatcher.GetCallManagerClient() : m_callManagerClient;

                    if (client != null)
                    {
                        logger.Debug("Sending Callback request to client endpoint at " + client.Endpoint.Address.ToString() + ".");
                        return client.Callback(username, dialString1, dialString2);
                    }
                    else
                    {
                        throw new ApplicationException("Call Manager Pass Thru service could not create a client.");
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception CallManagerPassThruService Callback. " + excp.Message);
                return "Sorry there was an unexpected error, the callback was not initiated.";
            }
        }
    }
}
