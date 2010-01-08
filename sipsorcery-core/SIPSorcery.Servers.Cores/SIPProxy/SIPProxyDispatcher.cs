using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Servers
{
    public class SIPProxyDispatcher
    {
        private const int MAX_LIFETIME_SECONDS = 180;
        private const int REMOVE_EXPIREDS_EVERY_DISPATCH = 100;

        private static ILog logger = log4net.LogManager.GetLogger("sipproxy");

        private SIPMonitorLogDelegate ProxyLogger_External;

        private Dictionary<string, string> m_transactionEndPoints = new Dictionary<string, string>();       // [transactionId, dispatched endpoint].
        private Dictionary<string, DateTime> m_transactionIDAddedAt = new Dictionary<string, DateTime>();   // [transactionId, time added].
        private int m_dispatchCount = 0;

        public SIPProxyDispatcher(SIPMonitorLogDelegate proxyLogger)
        {
            ProxyLogger_External = proxyLogger;
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
            }

            m_dispatchCount++;
            if(m_dispatchCount % REMOVE_EXPIREDS_EVERY_DISPATCH == 0)
            {
                RemoveExpiredDispatchRecords();
            }
        }

        public SIPEndPoint LookupTransactionID(SIPRequest sipRequest)
        {
            return LookupTransactionID(sipRequest.Method, sipRequest.Header);
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
            string transactionID = GetDispatcherTransactionID(branch, method);
            lock (m_transactionEndPoints)
            {
                if (m_transactionEndPoints.ContainsKey(transactionID))
                {
                    return SIPEndPoint.ParseSIPEndPoint(m_transactionEndPoints[transactionID]);
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
            }
            catch (Exception excp)
            {
                logger.Error("Exception RemoveExpiredTransactionIDs. " + excp.Message);
            }
        }
    }
}
