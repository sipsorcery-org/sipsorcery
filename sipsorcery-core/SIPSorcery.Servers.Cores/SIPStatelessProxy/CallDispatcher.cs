using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery.Servers {

    public delegate SIPEndPoint GetActiveAppServerDelegate();
    public delegate bool IsAppServerDelegate(SIPEndPoint sipEndPoint);

    public class CallDispatcher {

        public GetActiveAppServerDelegate GetActiveAppServer = () => { return null; };
        public IsAppServerDelegate IsAppServer = (s) => { return false; };

        private StatelessProxyScriptHelper m_scriptHelper;

        public CallDispatcher(StatelessProxyScriptHelper scriptHelper) {
            m_scriptHelper = scriptHelper;
        }

        public void DispatchOut(SIPRequest sipRequest, SIPEndPoint destination, SIPURI contactURI) {
            string branchId = sipRequest.Header.Vias.PopTopViaHeader().Branch;
            m_scriptHelper.SendTransparent(destination, sipRequest, branchId, null, contactURI);
        }

        public void DispatchIn(SIPRequest sipRequest, string internalSIPSocket, string branchId) {
            SIPEndPoint appServerSocket = GetAppServerSIPEndPoint(null);
            m_scriptHelper.Send(appServerSocket, sipRequest, branchId, SIPEndPoint.ParseSIPEndPoint(internalSIPSocket));
        }

        public void DispatchIn(SIPResponse sipResponse, string internalSIPSocket, string branchId) {
            SIPEndPoint internalProxyEndPoint = SIPEndPoint.ParseSIPEndPoint(internalSIPSocket);
            SIPEndPoint appServerSocket = GetAppServerSIPEndPoint(null);
            sipResponse.Header.Vias.PushViaHeader(new SIPViaHeader(appServerSocket, branchId));
            m_scriptHelper.Send(sipResponse, internalSIPSocket);
        }

        private SIPEndPoint GetAppServerSIPEndPoint(string callId) {
            return SIPEndPoint.ParseSIPEndPoint("127.0.0.1:5065"); ;
        }
    }
}
