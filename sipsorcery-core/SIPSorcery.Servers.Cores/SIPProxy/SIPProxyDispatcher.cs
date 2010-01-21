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
            public SIPEndPoint DesintationEndPoint;

            public UserCallbackRecord(DateTime expiresAt, SIPEndPoint destinationEndPoint)
            {
                ExpiresAt = expiresAt;
                DesintationEndPoint = destinationEndPoint;
            }
        }

        private const int MAX_LIFETIME_SECONDS = 180;
        private const int REMOVE_EXPIREDS_EVERY_DISPATCH = 10000;
        private const int CALLBACK_LIFETIME_SECONDS = 60;

        private static ILog logger = log4net.LogManager.GetLogger("sipproxy");

        private SIPMonitorLogDelegate ProxyLogger_External;

        private Dictionary<string, string> m_transactionEndPoints = new Dictionary<string, string>();       // [transactionId, dispatched endpoint].
        private Dictionary<string, DateTime> m_transactionIDAddedAt = new Dictionary<string, DateTime>();   // [transactionId, time added].
        private Dictionary<string, UserCallbackRecord> m_userCallbacks = new Dictionary<string, UserCallbackRecord>();    // [owner username, callback record], dictates the next call for the user should be directed to a specific app server.
        private int m_dispatchCount = 0;

        public SIPProxyDispatcher(SIPMonitorLogDelegate proxyLogger)
        {
            ProxyLogger_External = proxyLogger;
            StartService();
        }

        private void StartService()
        {
            try
            {
                ServiceHost callDispatcherHost = new ServiceHost(this);
                callDispatcherHost.Open();
                logger.Debug("SIPProxyDispatcher call dispatcher service started.");
            }
            catch (Exception excp)
            {
                logger.Warn("Exception SIPProxyDispatcher StartService. " + excp.Message);
            }
        }

        public void RecordDispatch(SIPRequest sipRequest, SIPEndPoint internalEndPoint)
        {
            string transactionID = GetDispatcherTransactionID(sipRequest.Method, sipRequest.Header);

            lock(m_transactionEndPoints)
            {
                if (m_transactionEndPoints.ContainsKey(transactionID))
                {
                    return;
                }

                m_transactionEndPoints.Add(transactionID, internalEndPoint.ToString());
                m_transactionIDAddedAt.Add(transactionID, DateTime.Now);
                ProxyLogger_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.SIPProxy, SIPMonitorEventTypesEnum.CallDispatcher, "Record dispatch for " + sipRequest.Method + " " + sipRequest.URI.ToString() + " to " + internalEndPoint.ToString() + " (id=" + transactionID + ").", null));
            }

            m_dispatchCount++;
            if(m_dispatchCount % REMOVE_EXPIREDS_EVERY_DISPATCH == 0)
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
                logger.Debug("SIPProxyDispatcher SetNextCallDest for user " + username + " and destination " + appServerEndPoint + ".");

                if (m_userCallbacks.ContainsKey(username))
                {
                    m_userCallbacks.Remove(username);
                }

                m_userCallbacks.Add(username, new UserCallbackRecord(DateTime.Now.AddSeconds(CALLBACK_LIFETIME_SECONDS), SIPEndPoint.ParseSIPEndPoint(appServerEndPoint)));
            }
        }

        public SIPEndPoint LookupTransactionID(SIPRequest sipRequest)
        {
            SIPEndPoint transactionEndPoint = LookupTransactionID(sipRequest.Method, sipRequest.Header);
            if (transactionEndPoint != null)
            {
                return transactionEndPoint;
            }
            else if(sipRequest.Method == SIPMethodsEnum.INVITE && !sipRequest.URI.User.IsNullOrBlank())
            {
                if (m_userCallbacks.ContainsKey(sipRequest.URI.User))
                {
                    SIPEndPoint callbackEndPoint = null;

                    lock (m_userCallbacks)
                    {
                        callbackEndPoint = m_userCallbacks[sipRequest.URI.User].DesintationEndPoint;
                        //m_userCallbacks.Remove(sipRequest.URI.User);
                    }

                    RecordDispatch(sipRequest, callbackEndPoint);
                    return callbackEndPoint;
                }
            }

            return null;
        }

        public SIPEndPoint LookupTransactionID(SIPResponse sipResponse)
        {
            return LookupTransactionID(sipResponse.Header.CSeqMethod, sipResponse.Header);
        }

        public SIPEndPoint LookupTransactionID(string branch, SIPMethodsEnum method)
        {
            return LookupTransactionID(method, branch);
        }

        private SIPEndPoint LookupTransactionID(SIPMethodsEnum method, SIPHeader header)
        {
            return LookupTransactionID(method, header.Vias.TopViaHeader.Branch);
        }

        private SIPEndPoint LookupTransactionID(SIPMethodsEnum method, string branch)
        {
            if (branch.IsNullOrBlank())
            {
                return null;
            }

            string transactionID = GetDispatcherTransactionID(branch, method);
            lock (m_transactionEndPoints)
            {
                if (m_transactionEndPoints.ContainsKey(transactionID))
                {
                    SIPEndPoint dispacthEndPoint = SIPEndPoint.ParseSIPEndPoint(m_transactionEndPoints[transactionID]);
                    ProxyLogger_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.SIPProxy, SIPMonitorEventTypesEnum.CallDispatcher, "Dispatcher lookup for " + method + " returned " + dispacthEndPoint.ToString() + " (id=" + transactionID + ").", null));
                    return dispacthEndPoint;
                }
            }

            return null;
        }

        private string GetDispatcherTransactionID(SIPMethodsEnum method, SIPHeader sipHeader)
        {
            if (sipHeader.Vias == null || sipHeader.Vias.Length == 0 || sipHeader.Vias.TopViaHeader.Branch.IsNullOrBlank())
            {
                throw new ArgumentException("GetDispatcherTransactionID was passed a SIP header with an invalid Via header.");
            }

            return GetDispatcherTransactionID(sipHeader.Vias.TopViaHeader.Branch, method);
        }

        private string GetDispatcherTransactionID(string branch, SIPMethodsEnum method)
        {
            if (method == SIPMethodsEnum.ACK || method == SIPMethodsEnum.CANCEL)
            {
                method = SIPMethodsEnum.INVITE;
            }

            return SIPTransaction.GetRequestTransactionId(branch, method); 
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
                         //logger.Debug("SIPProxyDispatcher removing expired transactionid=" + entry.Key + ".");
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
