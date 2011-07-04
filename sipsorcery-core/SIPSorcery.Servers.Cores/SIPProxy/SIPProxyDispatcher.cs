using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Text;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorcery.Web.Services;
using log4net;

namespace SIPSorcery.Servers
{
    [ServiceBehavior(ConcurrencyMode = ConcurrencyMode.Multiple, InstanceContextMode = InstanceContextMode.Single)]
    public class SIPProxyDispatcher : ICallDispatcherService
    {
        private struct UserCallbackRecord
        {
            public DateTime ExpiresAt;
            public string DesintationEndPoint;

            public UserCallbackRecord(DateTime expiresAt, string destinationEndPoint)
            {
                ExpiresAt = expiresAt;
                DesintationEndPoint = destinationEndPoint;
            }
        }

        private const int MAX_LIFETIME_SECONDS = 180;
        private const int REMOVE_EXPIREDS_SECONDS = 60;
        private const int CALLBACK_LIFETIME_SECONDS = 30;

        private static ILog logger = log4net.LogManager.GetLogger("sipproxy");

        private SIPMonitorLogDelegate ProxyLogger_External;

        private Dictionary<string, string> m_transactionEndPoints = new Dictionary<string, string>();       // [transactionId, dispatched endpoint].
        private Dictionary<string, DateTime> m_transactionIDAddedAt = new Dictionary<string, DateTime>();   // [transactionId, time added].
        private Dictionary<string, UserCallbackRecord> m_userCallbacks = new Dictionary<string, UserCallbackRecord>();    // [owner username, callback record], dictates the next call for the user should be directed to a specific app server.
        private DateTime m_lastRemove = DateTime.Now;

        public SIPProxyDispatcher(SIPMonitorLogDelegate proxyLogger)
        {
            ProxyLogger_External = proxyLogger;
            StartService();
        }

        private void StartService()
        {
            try
            {
                if (WCFUtility.DoesWCFServiceExist(this.GetType().FullName.ToString()))
                {
                    ServiceHost callDispatcherHost = new ServiceHost(this);
                    callDispatcherHost.Open();
                    logger.Debug("SIPProxyDispatcher call dispatcher service started.");
                }
            }
            catch (Exception excp)
            {
                logger.Warn("Exception SIPProxyDispatcher StartService. " + excp.Message);
            }
        }

        public void RecordDispatch(SIPRequest sipRequest, SIPEndPoint internalEndPoint)
        {
            lock(m_transactionEndPoints)
            {
                if (m_transactionEndPoints.ContainsKey(sipRequest.Header.CallId))
                {
                    if (m_transactionEndPoints[sipRequest.Header.CallId] == internalEndPoint.ToString())
                    {
                        // The application server end point has not changed for this Call-Id
                        return;
                    }
                    else
                    {
                        // The application server end point has changed within the lifetime of the Call-Id. Remove the old mapping.
                        lock (m_transactionEndPoints)
                        {
                            m_transactionEndPoints.Remove(sipRequest.Header.CallId);
                        }
                    }
                }

                m_transactionEndPoints.Add(sipRequest.Header.CallId, internalEndPoint.ToString());
                m_transactionIDAddedAt.Add(sipRequest.Header.CallId, DateTime.Now);
                //ProxyLogger_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.SIPProxy, SIPMonitorEventTypesEnum.CallDispatcher, "Record dispatch for " + sipRequest.Method + " " + sipRequest.URI.ToString() + " to " + internalEndPoint.ToString() + " (id=" + transactionID + ").", null));
            }

            if (m_lastRemove < DateTime.Now.AddSeconds(REMOVE_EXPIREDS_SECONDS * -1))
            {
                RemoveExpiredDispatchRecords();
            }
        }

        public bool IsAlive()
        {
            return true;
        }

        public void SetNextCallDest(string username, string appServerEndPoint)
        {
            if (username.IsNullOrBlank() || appServerEndPoint.IsNullOrBlank())
            {
                return;
            }

            lock (m_userCallbacks)
            {
                ProxyLogger_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.SIPProxy, SIPMonitorEventTypesEnum.DialPlan, "SIP Proxy setting application server for next call to user " + username + " as " + appServerEndPoint + ".", username));

                if (m_userCallbacks.ContainsKey(username))
                {
                    m_userCallbacks.Remove(username);
                }

                m_userCallbacks.Add(username, new UserCallbackRecord(DateTime.Now.AddSeconds(CALLBACK_LIFETIME_SECONDS), appServerEndPoint));
            }
        }

        public SIPEndPoint LookupTransactionID(SIPRequest sipRequest)
        {
            try
            {
                SIPEndPoint transactionEndPoint = LookupTransactionID(sipRequest.Header.CallId);
                if (transactionEndPoint != null)
                {
                    return transactionEndPoint;
                }
                else if (sipRequest.Method == SIPMethodsEnum.INVITE && !sipRequest.URI.User.IsNullOrBlank())
                {
                    string toUser = (sipRequest.URI.User.IndexOf('.') != -1) ? sipRequest.URI.User.Substring(sipRequest.URI.User.LastIndexOf('.') + 1) : sipRequest.URI.User;
                    if (m_userCallbacks.ContainsKey(toUser))
                    {
                        SIPEndPoint callbackEndPoint = null;
                        callbackEndPoint = SIPEndPoint.ParseSIPEndPoint(m_userCallbacks[toUser].DesintationEndPoint);
                        RecordDispatch(sipRequest, callbackEndPoint);
                        ProxyLogger_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.SIPProxy, SIPMonitorEventTypesEnum.DialPlan, "SIP Proxy directing incoming call for user " + toUser + " to application server " + callbackEndPoint.ToString() + ".", toUser));

                        lock (m_userCallbacks)
                        {
                            m_userCallbacks.Remove(toUser);
                        }

                        return callbackEndPoint;
                    }
                }

                return null;
            }
            catch (Exception excp)
            {
                logger.Error("Exception LookupTransactionID. " + excp.Message);
                return null;
            }
        }

        public SIPEndPoint LookupTransactionID(SIPResponse sipResponse)
        {
            return LookupTransactionID(sipResponse.Header.CallId);
        }

        private SIPEndPoint LookupTransactionID(string callID)
        {
            if (callID.IsNullOrBlank())
            {
                return null;
            }

            lock (m_transactionEndPoints)
            {
                if (m_transactionEndPoints.ContainsKey(callID))
                {
                    SIPEndPoint dispacthEndPoint = SIPEndPoint.ParseSIPEndPoint(m_transactionEndPoints[callID]);
                    //ProxyLogger_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.SIPProxy, SIPMonitorEventTypesEnum.CallDispatcher, "Dispatcher lookup for " + method + " returned " + dispacthEndPoint.ToString() + " (id=" + transactionID + ").", null));
                    return dispacthEndPoint;
                }
            }

            return null;
        }

        private void RemoveExpiredDispatchRecords()
        {
            try
            {
                lock (m_transactionEndPoints)
                {
                    (from entry in m_transactionIDAddedAt
                     where entry.Value < DateTime.Now.AddSeconds(MAX_LIFETIME_SECONDS * -1)
                     select entry).ToList().ForEach((entry) =>
                     {
                         //logger.Debug("SIPProxyDispatcher removing expired transactionid=" + entry.Key + ".");
                         m_transactionEndPoints.Remove(entry.Key);
                         m_transactionIDAddedAt.Remove(entry.Key);
                     });
                }

                lock (m_userCallbacks)
                {
                    (from userRecord in m_userCallbacks
                     where userRecord.Value.ExpiresAt < DateTime.Now
                     select userRecord).ToList().ForEach((entry) =>
                     {
                         logger.Debug("SIPProxyDispatcher removing expired callback record for=" + entry.Key + ".");
                         m_userCallbacks.Remove(entry.Key);
                     });
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception RemoveExpiredTransactionIDs. " + excp.Message);
            }
        }
    }
}
