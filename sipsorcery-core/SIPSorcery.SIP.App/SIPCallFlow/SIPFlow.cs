using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using BlueFace.FlowChart;
using BlueFace.FlowChart.FlowChartDiagrams;
using BlueFace.Sys;
using BlueFace.Sys.Net;
using BlueFace.VoIP.Authentication;
using BlueFace.VoIP.Net;
using BlueFace.VoIP.Net.RTP;
using BlueFace.VoIP.Net.SIP;
using IronPython.Compiler;
using IronPython.Hosting;
using log4net;

namespace BlueFace.Net.SignallingGUI
{
    public delegate void SIPFlowLogDelegate(string message);
    
    public class SIPFlow : BaseFlow
    {
        private const string CRLF = "\r\n";

        private ManualResetEvent m_flowItemProcessing = new ManualResetEvent(false);
        private MemoryStream m_sipFlowDebugStream;
        private StreamReader m_debugStreamReader;
        private long m_debugStreamPosition = 0;

        private SIPTransport m_sipTransport;

        public event SIPFlowLogDelegate LogEvent;

        // IronPython Globals
        private PythonHelper pythonHelper;

        public SIPFlow(SIPTransport sipTransport, List<ActionDiagram> actionDiagrams, List<DecisionDiagram> decisionDiagrams)
            : base(actionDiagrams, decisionDiagrams)
        {
            m_sipTransport = sipTransport;
            
            pythonHelper = new PythonHelper(m_pythonEngine, m_sipTransport);
            pythonHelper.LogEvent += new SIPFlowLogDelegate(pythonHelper_LogEvent);
            
            //m_sipTransport.SIPTransportResponseReceived += new SIPTransportResponseReceivedDelegate(pythonHelper.SIPTransportResponseReceived);
            
            //m_pythonEngine.Import("BlueFace.VoIP.Net.SIP.*");
            m_pythonEngine.Execute("import clr");
            //m_pythonEngine.Execute("clr.AddReference('BlueFace.VoIP.Net')");
            //m_pythonEngine.Execute("from BlueFace.VoIP.Net.SIP import *");
            //m_pythonEngine.Execute("clr.AddReference('siggui')");
            //m_pythonEngine.Execute("from BlueFace.Net.SignallingGUI import *");

            m_pythonEngine.Globals["pythonHelper"] = pythonHelper;
            //m_pythonEngine.Globals["sipRequest"] = sipRequest;
            
            //SIPTransaction transaction = new SIPTransaction(sipRequest);
            //SIPTransport.SendSIPReliable(transaction);

            m_sipFlowDebugStream = new MemoryStream();
            m_debugStreamReader = new StreamReader(m_sipFlowDebugStream);
            m_pythonEngine.SetStandardOutput(m_sipFlowDebugStream);
        }

        private void pythonHelper_LogEvent(string message)
        {
            if (LogEvent != null)
            {
                LogEvent(message);
            }
        }

        public void StartFlow()
        {
            try
            {
                m_sipTransport.SIPTransportRequestReceived += new SIPTransportRequestReceivedDelegate(pythonHelper.SIPTransportRequestReceived);
                
                // First item has to be an Action one.
                string nextFlowItemId = ProcessFlowAction(StartFlowItem);

                while (nextFlowItemId != null)
                {
                    m_flowItemProcessing.Reset();
                    //m_flowItemProcessing.WaitOne(1000, false);

                    nextFlowItemId = ProcessFlowItem(nextFlowItemId);
                }
            }
            catch (Exception excp)
            {
                //throw new ApplicationException("Exception running flow. " + excp.Message);
                logger.Error("Exception StartFlow. " + excp.Message);
                FireFlowDebugMessage("Exception running call flow. " + excp.Message + CRLF);
            }
            finally
            {
                m_sipTransport.SIPTransportRequestReceived -= new SIPTransportRequestReceivedDelegate(pythonHelper.SIPTransportRequestReceived);
                m_flowItemProcessing.Reset();
                FireFlowComplete();
            }
        }

        private string ProcessFlowItem(string flowItemId)
        {
            if (m_actionFlowItems.ContainsKey(flowItemId))
            {
                return ProcessFlowAction(m_actionFlowItems[flowItemId]);
            }
            else if (m_decisionFlowItems.ContainsKey(flowItemId))
            {
                return ProcessFlowDecision(m_decisionFlowItems[flowItemId]);
            }
            else
            {
                throw new ApplicationException("Could not locate flow item for id " + flowItemId + ".");
            }
        }

        /// <summary>
        /// Processes the Action item and returns the connection id of the next item in the flow.
        /// </summary>
        private string ProcessFlowAction(ActionFlowItem actionItem)
        {
            try
            {
                FireFlowItemInProgess(actionItem);

                if (!String.IsNullOrEmpty(actionItem.Contents))
                {
                    //FireFlowDebugMessage("Action: " + actionItem.Contents + CRLF);

                    // Executing flow diagram contents using Iron Python engine.
                    m_pythonEngine.Execute(actionItem.Contents);

                    // Read any output from the debug stream.
                    m_sipFlowDebugStream.Position = m_debugStreamPosition;
                    string debugOutput = m_debugStreamReader.ReadToEnd();
                    m_debugStreamPosition = m_sipFlowDebugStream.Position;

                    if(debugOutput != null && debugOutput.Trim().Length > 0)
                    {
                        FireFlowDebugMessage(DateTime.Now.ToString("dd MMM yyyy HH:mm:ss:fff") + CRLF + debugOutput);
                    }
                }

                // Return the flowid of the item connected downstream of this action item.
                return actionItem.DownstreamFlowConnection.DestinationFlowItemId;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPFlow ProcessFlowAction. " + excp.Message);
                throw excp;
            }
        }

        private string ProcessFlowDecision(DecisionFlowItem decisionItem)
        {
            try
            {
                FireFlowItemInProgess(decisionItem);

                // For a decision diagram evaluate each of the conditions in order left, bottom, right and take a branch as soon as a true condition is found.
                if (decisionItem.LeftConnection != null && decisionItem.LeftConnection.DestinationFlowItemId != null && !String.IsNullOrEmpty(decisionItem.LeftConnection.Condition))
                {
                    bool result = m_pythonEngine.EvaluateAs<bool>(decisionItem.LeftConnection.Condition);

                    //FireFlowDebugMessage("Decision (left): " + decisionItem.LeftConnection.Condition + " = " + result + CRLF);

                    if (result)
                    {
                        return decisionItem.LeftConnection.DestinationFlowItemId;
                    }
                }
                
                if (decisionItem.BottomConnection != null && decisionItem.BottomConnection.DestinationFlowItemId != null && !String.IsNullOrEmpty(decisionItem.BottomConnection.Condition))
                {
                    bool result = m_pythonEngine.EvaluateAs<bool>(decisionItem.BottomConnection.Condition);
                    
                    //FireFlowDebugMessage("Decision (bottom): " + decisionItem.BottomConnection.Condition + " = " + result + CRLF);
                    
                    if (result)
                    {
                        return decisionItem.BottomConnection.DestinationFlowItemId;
                    }
                }
                
                if (decisionItem.RightConnection != null && decisionItem.RightConnection.DestinationFlowItemId != null && !String.IsNullOrEmpty(decisionItem.RightConnection.Condition))
                {
                    bool result = m_pythonEngine.EvaluateAs<bool>(decisionItem.RightConnection.Condition);
                    
                    //FireFlowDebugMessage("Decision (right): " + decisionItem.RightConnection.Condition + "= " + result + CRLF);

                    if (result)
                    {
                        return decisionItem.RightConnection.DestinationFlowItemId;
                    }
                }

                throw new ApplicationException("A downstream flow connection could not be deterimed from decision flow item with id " + decisionItem.FlowItemId + ".");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPFlow ProcessFlowDecision. " + excp.Message);
                throw excp;
            }
        }
    }

    public class PythonHelper
    {
        private const string CRLF = "\r\n";

        private static ILog logger = LogManager.GetLogger("flow.py");

        private SIPTransport m_sipTransport;

        private SIPResponse m_lastFinalResponse;
        private SIPResponse m_lastInfoResponse;
        private SIPRequest m_lastRequest;
                
        private ManualResetEvent m_waitForSIPFinalResponse = new ManualResetEvent(false);
        private ManualResetEvent m_waitForSIPInfoResponse = new ManualResetEvent(false);
        private ManualResetEvent m_waitForSIPRequest = new ManualResetEvent(false);

        public event SIPFlowLogDelegate LogEvent;

        public PythonHelper(PythonEngine pythonEngine, SIPTransport sipTransport)
        {
            m_sipTransport = sipTransport;

            logger.Debug("PythonHelper SIP Transport local socket is " + m_sipTransport.GetDefaultTransportContact(SIPProtocolsEnum.UDP) + ".");
        }
        
        public SIPRequest GetInviteRequest(string inviteURIStr, string fromURIStr, string body, int rtpPort)
        {
            SIPURI inviteURI = (inviteURIStr.StartsWith("sip:")) ? SIPURI.ParseSIPURI(inviteURIStr) : SIPURI.ParseSIPURI("sip:" + inviteURIStr);
            SIPFromHeader fromHeader = SIPFromHeader.ParseFromHeader(fromURIStr); // (fromURIStr.StartsWith("sip:")) ? SIPFromHeader.ParseFromHeader(fromURIStr) : SIPFromHeader.ParseFromHeader("sip:" + fromURIStr);
            SIPToHeader toHeader = new SIPToHeader(null, inviteURI, null);

            SIPRequest inviteRequest = new SIPRequest(SIPMethodsEnum.INVITE, inviteURI);

            IPEndPoint localSIPEndPoint = m_sipTransport.GetIPEndPointsList()[0];
            SIPHeader inviteHeader = new SIPHeader(fromHeader, toHeader, 1, CallProperties.CreateNewCallId());

            inviteHeader.From.FromTag = CallProperties.CreateNewTag();
            inviteHeader.Contact = SIPContactHeader.ParseContactHeader("sip:" + localSIPEndPoint.ToString());
            inviteHeader.CSeqMethod = SIPMethodsEnum.INVITE;
            //inviteHeader.UnknownHeaders.Add("BlueFace-Test: 12324");
            inviteRequest.Header = inviteHeader;

            SIPViaHeader viaHeader = new SIPViaHeader(localSIPEndPoint.Address.ToString(), localSIPEndPoint.Port, CallProperties.CreateBranchId());
            inviteRequest.Header.Via.PushViaHeader(viaHeader);

            rtpPort = (rtpPort != 0) ? rtpPort : Crypto.GetRandomInt(10000, 20000);
            string sessionId = Crypto.GetRandomInt(1000, 5000).ToString();

            if (body != null && body.Trim().Length > 0)
            {
                inviteRequest.Body = body;
            }
            else
            {
                inviteRequest.Body =
                   "v=0" + CRLF +
                    "o=- " + sessionId + " " + sessionId + " IN IP4 " + localSIPEndPoint.Address.ToString() + CRLF +
                    "s=session" + CRLF +
                    "c=IN IP4 " + localSIPEndPoint.Address.ToString() + CRLF +
                    "t=0 0" + CRLF +
                    "m=audio " + rtpPort + " RTP/AVP 0 101" + CRLF +
                    "a=rtpmap:0 PCMU/8000" + CRLF +
                    "a=rtpmap:101 telephone-event/8000" + CRLF +
                    "a=fmtp:101 0-16" + CRLF +
                    "a=sendrecv";
            }

            inviteRequest.Header.ContentLength = inviteRequest.Body.Length;
            inviteRequest.Header.ContentType = "application/sdp";

            return inviteRequest;
        }

        public SIPRequest GetAuthenticatedRequest(SIPRequest origRequest, SIPResponse authReqdResponse, string username, string password)
        {
            SIPRequest authRequest = origRequest;
            authRequest.Header.Via.TopViaHeader.Branch = CallProperties.CreateBranchId();
            authRequest.Header.From.FromTag = CallProperties.CreateNewTag();
            authRequest.Header.CSeq = origRequest.Header.CSeq + 1;

            AuthorizationRequest authorizationRequest = authReqdResponse.Header.AuthenticationHeader.AuthRequest;
            authorizationRequest.SetCredentials(username, password, origRequest.URI.ToString(), origRequest.Method.ToString());

            authRequest.Header.AuthenticationHeader = new SIPAuthenticationHeader(authorizationRequest);
            authRequest.Header.AuthenticationHeader.AuthRequest.Response = authorizationRequest.Digest;

            return authRequest;
        }

        public SIPRequest GetCancelRequest(SIPRequest inviteRequest)
        {
            SIPRequest cancelRequest = new SIPRequest(SIPMethodsEnum.CANCEL, inviteRequest.URI);
            SIPHeader cancelHeader = new SIPHeader(inviteRequest.Header.From, inviteRequest.Header.To, inviteRequest.Header.CSeq, inviteRequest.Header.CallId);
            cancelHeader.Via = inviteRequest.Header.Via;
            cancelHeader.CSeqMethod = SIPMethodsEnum.CANCEL;

            cancelRequest.Header = cancelHeader;

            return cancelRequest;
        }

        public SIPRequest GetByeRequest(SIPResponse inviteResponse)
        {
            SIPRequest byeRequest = new SIPRequest(SIPMethodsEnum.BYE, inviteResponse.Header.Contact[0].ContactURI);
            SIPHeader byeHeader = new SIPHeader(inviteResponse.Header.From, inviteResponse.Header.To, inviteResponse.Header.CSeq + 1, inviteResponse.Header.CallId);
            byeHeader.CSeqMethod = SIPMethodsEnum.BYE;

            IPEndPoint localSIPEndPoint = m_sipTransport.GetIPEndPointsList()[0];
            SIPViaHeader viaHeader = new SIPViaHeader(localSIPEndPoint.Address.ToString(), localSIPEndPoint.Port, CallProperties.CreateBranchId());
            byeHeader.Via.PushViaHeader(viaHeader);

            byeRequest.Header = byeHeader;

            return byeRequest;
        }

        public SIPRequest GetReferRequest(SIPRequest inviteRequest, SIPResponse inviteResponse, string referToURI)
        {
            SIPRequest referRequest = new SIPRequest(SIPMethodsEnum.REFER, inviteRequest.URI);
            SIPHeader referHeader = new SIPHeader(inviteResponse.Header.From, inviteResponse.Header.To, inviteRequest.Header.CSeq + 1, inviteRequest.Header.CallId);
            referHeader.Contact = inviteRequest.Header.Contact;
            referHeader.CSeqMethod = SIPMethodsEnum.REFER;
            referHeader.ReferTo = referToURI;
            SIPFromHeader referredBy = new SIPFromHeader(inviteRequest.Header.From.FromName, inviteRequest.Header.From.FromURI, null);
            referHeader.ReferredBy = referredBy.ToString();
            referHeader.AuthenticationHeader = inviteRequest.Header.AuthenticationHeader;

            IPEndPoint localSIPEndPoint = m_sipTransport.GetIPEndPointsList()[0];
            SIPViaHeader viaHeader = new SIPViaHeader(localSIPEndPoint.Address.ToString(), localSIPEndPoint.Port, CallProperties.CreateBranchId());
            referHeader.Via.PushViaHeader(viaHeader);

            referRequest.Header = referHeader;

            return referRequest;
        }

        public SIPRequest GetRegisterRequest(string server, string toURIStr, string contactStr)
        {
            try
            {
                IPEndPoint localSIPEndPoint = m_sipTransport.GetDefaultTransportContact(SIPProtocolsEnum.UDP);

                SIPRequest registerRequest = new SIPRequest(SIPMethodsEnum.REGISTER, "sip:" + server);
                SIPHeader registerHeader = new SIPHeader(SIPFromHeader.ParseFromHeader(toURIStr), SIPToHeader.ParseToHeader(toURIStr), 1, CallProperties.CreateNewCallId());
                registerHeader.From.FromTag = CallProperties.CreateNewTag();
                registerHeader.Contact = SIPContactHeader.ParseContactHeader(contactStr);
                SIPViaHeader viaHeader = new SIPViaHeader(localSIPEndPoint.Address.ToString(), localSIPEndPoint.Port, CallProperties.CreateBranchId());
                registerHeader.Via.PushViaHeader(viaHeader);
                registerHeader.CSeqMethod = SIPMethodsEnum.REGISTER;
                registerHeader.Expires = SIPConstants.DEFAULT_REGISTEREXPIRY_SECONDS;

                registerRequest.Header = registerHeader;

                return registerRequest;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetRegisterRequest. " + excp.Message);
                throw new ApplicationException("GetRegisterRequest " + excp.GetType().ToString() + ".  " + excp.Message);
            }
        }

        public SIPRequest GetSIPRequest(SIPMethodsEnum sipMethod, string requestURIStr, string fromURIStr)
        {
            return GetSIPRequest(sipMethod, requestURIStr, fromURIStr, 1, CallProperties.CreateNewCallId(), null, null);
        }

        public SIPRequest GetSIPRequest(SIPMethodsEnum sipMethod, string requestURIStr, string fromURIStr, int cseq, string callId)
        {
            return GetSIPRequest(sipMethod, requestURIStr, fromURIStr, cseq, callId, null, null);
        }

        public SIPRequest GetSIPRequest(SIPMethodsEnum sipMethod, string requestURIStr, string fromURIStr, int cseq, string callId, string contentType, string body)
        {
            SIPURI requestURI = (requestURIStr.StartsWith("sip:")) ? SIPURI.ParseSIPURI(requestURIStr) : SIPURI.ParseSIPURI("sip:" + requestURIStr);
            SIPURI fromURI = (fromURIStr.StartsWith("sip:")) ? SIPURI.ParseSIPURI(fromURIStr) : SIPURI.ParseSIPURI("sip:" + fromURIStr);

            SIPFromHeader fromHeader = new SIPFromHeader(null, fromURI, CallProperties.CreateNewTag());
            SIPToHeader toHeader = new SIPToHeader(null, requestURI, null);

            SIPRequest sipRequest = new SIPRequest(sipMethod, requestURI);

            IPEndPoint localSIPEndPoint = m_sipTransport.GetIPEndPointsList()[0];
            SIPHeader sipHeader = new SIPHeader(fromHeader, toHeader, cseq, callId);

            sipHeader.Contact = SIPContactHeader.ParseContactHeader("sip:" + localSIPEndPoint.ToString());
            sipHeader.CSeqMethod = sipMethod;
            sipRequest.Header = sipHeader;

            SIPViaHeader viaHeader = new SIPViaHeader(localSIPEndPoint.Address.ToString(), localSIPEndPoint.Port, CallProperties.CreateBranchId());
            sipRequest.Header.Via.PushViaHeader(viaHeader);

            if (body != null && body.Trim().Length > 0)
            {
                sipRequest.Body = body;
                //sipRequest.Body = "Signal=5\r\nDuration=250";
                //sipRequest.Body = "<rtcp>blah blah blah</rtcp>";
                sipRequest.Header.ContentLength = sipRequest.Body.Length;
                sipRequest.Header.ContentType = contentType;
            }

            return sipRequest;
        }

        public SIPRequest ParseSIPRequest(string sipRequestStr)
        {
            // Strings from Rich text boxes use a \n end of line character.
            sipRequestStr = Regex.Replace(sipRequestStr, "\n", "\r\n");
            sipRequestStr = Regex.Replace(sipRequestStr, "\r\r", "\r");

            SIPMessage sipMessage = SIPMessage.ParseSIPMessage(sipRequestStr, null, null);
            return SIPRequest.ParseSIPRequest(sipMessage);
        }

        public void SendSIPRequest(SIPRequest sipRequest)
        {
            SendSIPRequest(sipRequest, sipRequest.URI.GetURIEndPoint().ToString());
        }

        public void SendSIPRequest(SIPRequest sipRequest, string dstSocket)
        {
            //ResetSIPResponse();

            if (sipRequest.Method == SIPMethodsEnum.INVITE)
            {
                //m_inviteRequest = sipRequest;
                UACInviteTransaction inviteTransaction = m_sipTransport.CreateUACTransaction(sipRequest, IPSocket.GetIPEndPoint(dstSocket), m_sipTransport.GetTransportContact(null), SIPProtocolsEnum.UDP);
                inviteTransaction.UACInviteTransactionInformationResponseReceived += new SIPTransactionResponseReceivedDelegate(TransactionInformationResponseReceived);
                inviteTransaction.UACInviteTransactionFinalResponseReceived += new SIPTransactionResponseReceivedDelegate(TransactionFinalResponseReceived);
                m_sipTransport.SendSIPReliable(inviteTransaction);
            }
            else
            {
                SIPNonInviteTransaction sipTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, IPSocket.GetIPEndPoint(dstSocket), m_sipTransport.GetTransportContact(null), SIPProtocolsEnum.UDP);
                sipTransaction.NonInviteTransactionFinalResponseReceived += new SIPTransactionResponseReceivedDelegate(TransactionFinalResponseReceived);
                m_sipTransport.SendSIPReliable(sipTransaction);
            }
        }

        private void TransactionInformationResponseReceived(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            m_lastInfoResponse = sipResponse;
            m_waitForSIPInfoResponse.Set();
        }

        private void TransactionFinalResponseReceived(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            m_lastFinalResponse = sipResponse;
            m_waitForSIPFinalResponse.Set(); 
        }

        public void SIPTransportRequestReceived(SIPProtocolsEnum protocol, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            if (sipRequest.Method == SIPMethodsEnum.BYE)
            {
                // Send an Ok response.
                SIPResponse okResponse = GetResponse(sipRequest.Header, SIPResponseStatusCodesEnum.Ok);
                m_sipTransport.SendResponseFrom(localEndPoint, remoteEndPoint, protocol, okResponse);
            }
            else if (sipRequest.Method == SIPMethodsEnum.NOTIFY)
            {
                // Send an not supported response.
                //SIPResponse notSupportedResponse = GetResponse(sipRequest.Header, SIPResponseStatusCodesEnum.MethodNotAllowed);
                //SIPTransport.SendResponseFrom(localEndPoint, remoteEndPoint, notSupportedResponse);
                // Send an Ok response.
                SIPResponse okResponse = GetResponse(sipRequest.Header, SIPResponseStatusCodesEnum.Ok);
                okResponse.Header.Contact = null;
                m_sipTransport.SendResponseFrom(localEndPoint, remoteEndPoint, protocol, okResponse);
            }
            else
            {
                m_lastRequest = sipRequest;
                m_waitForSIPRequest.Set();
            }
        }

        public SIPResponse WaitForInfoResponse(int waitSeconds, SIPRequest sipRequest)
        {
            DateTime startWaitTime = DateTime.Now;
            m_waitForSIPInfoResponse.Reset();
            m_lastInfoResponse = null;

            while (m_lastInfoResponse == null && DateTime.Now.Subtract(startWaitTime).TotalSeconds < waitSeconds)
            {
                m_waitForSIPInfoResponse.WaitOne(Convert.ToInt32((waitSeconds - DateTime.Now.Subtract(startWaitTime).TotalSeconds) * 1000), false);

                if (m_lastInfoResponse != null && m_lastInfoResponse.Header.CallId == sipRequest.Header.CallId)
                {
                    break;
                }
                else
                {
                    m_lastInfoResponse = null;
                }
            }

            return m_lastInfoResponse;
        }

        public SIPResponse WaitForFinalResponse(int waitSeconds, SIPRequest sipRequest)
        {
            DateTime startWaitTime = DateTime.Now;
            m_waitForSIPFinalResponse.Reset();
            m_lastFinalResponse = null;

            while (m_lastFinalResponse == null && DateTime.Now.Subtract(startWaitTime).TotalSeconds < waitSeconds)
            {
                m_waitForSIPFinalResponse.WaitOne(Convert.ToInt32((waitSeconds - DateTime.Now.Subtract(startWaitTime).TotalSeconds) * 1000), false);

                if (m_lastFinalResponse != null && m_lastFinalResponse.Header.CallId == sipRequest.Header.CallId)
                {
                    break;
                }
                else
                {
                    m_lastFinalResponse = null;
                }
            }

            return m_lastFinalResponse;
        }

        public SIPRequest WaitForRequest(int waitSeconds)
        {
            m_waitForSIPRequest.Reset();
            m_waitForSIPRequest.WaitOne(waitSeconds * 1000, false);

            return m_lastRequest;
        }

        private SIPResponse GetResponse(SIPHeader requestHeader, SIPResponseStatusCodesEnum responseCode)
        {
            try
            {
                SIPResponse response = new SIPResponse(responseCode);

                response.Header = new SIPHeader(requestHeader.Contact, requestHeader.From, requestHeader.To, requestHeader.CSeq, requestHeader.CallId);
                response.Header.CSeqMethod = requestHeader.CSeqMethod;
                response.Header.Via = requestHeader.Via;
                response.Header.MaxForwards = Int32.MinValue;

                return response;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetResponse. " + excp.Message);
                throw excp;
            }
        }

        public void SendRTPPacket(string sourceSocket, string destinationSocket)
        {
            try
            {
                //logger.Debug("Attempting to send RTP packet from " + sourceSocket + " to " + destinationSocket + ".");
                FireLogEvent("Attempting to send RTP packet from " + sourceSocket + " to " + destinationSocket + ".");
                
                IPEndPoint sourceEP = IPSocket.GetIPEndPoint(sourceSocket);
                IPEndPoint destEP = IPSocket.GetIPEndPoint(destinationSocket);

                RTPPacket rtpPacket = new RTPPacket(80);
                rtpPacket.Header.SequenceNumber = (UInt16)6500;
                rtpPacket.Header.Timestamp = 100000;

                UDPPacket udpPacket = new UDPPacket(sourceEP.Port, destEP.Port, rtpPacket.GetBytes());
                IPv4Header ipHeader = new IPv4Header(ProtocolType.Udp, Crypto.GetRandomInt(6), sourceEP.Address, destEP.Address);
                IPv4Packet ipPacket = new IPv4Packet(ipHeader, udpPacket.GetBytes());

                byte[] data = ipPacket.GetBytes();

                Socket rawSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
                rawSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, 1);

                rawSocket.SendTo(data, destEP);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendRTPPacket. " + excp.Message);
            }
        }

        public void FireLogEvent(string message)
        {
            if (LogEvent != null)
            {
                LogEvent(message);
            }
        }
    }
}
