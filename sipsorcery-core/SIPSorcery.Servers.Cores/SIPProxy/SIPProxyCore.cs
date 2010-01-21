// ============================================================================
// FileName: SIPProxyCore.cs
//
// Description:
// A SIP proxy core that routes SIP requests and responses to and from other SIP servers.
//
// Author(s):
// Aaron Clauson
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;
using Microsoft.Scripting.Hosting;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{
    public delegate SIPEndPoint GetAppServerDelegate();

    public class SIPProxyCore
    {
        private static ILog logger = log4net.LogManager.GetLogger("sipproxy");

        private SIPMonitorLogDelegate m_proxyLogger = (e) => { };

        private SIPTransport m_sipTransport;
        private CompiledCode m_compiledScript;
        private string m_scriptPath;
        private ScriptLoader m_scriptLoader;
        private SIPProxyScriptFacade m_proxyScriptFacade;
        private SIPProxyDispatcher m_proxyDispatcher;
        private SIPCallDispatcherFile m_sipCallDispatcherFile;
        private DateTime m_lastScriptChange = DateTime.MinValue;

        public IPAddress PublicIPAddress;       // Can be set if there is an object somewhere that knows the public IP. The address wil be available in the proxy runtime script.

        public SIPProxyCore(
            SIPMonitorLogDelegate proxyLogger,
            SIPTransport sipTransport,
            string scriptPath,
            string appServerEndPointsPath)
        {
            try
            {
                m_proxyLogger = proxyLogger ?? m_proxyLogger;
                m_scriptPath = scriptPath;
                m_sipTransport = sipTransport;

                if (!appServerEndPointsPath.IsNullOrBlank() && File.Exists(appServerEndPointsPath))
                {
                    m_sipCallDispatcherFile = new SIPCallDispatcherFile(SendMonitorEvent, appServerEndPointsPath);
                    m_sipCallDispatcherFile.LoadAndWatch();
                }
                else
                {
                    logger.Warn("No call dispatcher file specified for SIP Proxy.");
                }

                m_proxyDispatcher = new SIPProxyDispatcher(new SIPMonitorLogDelegate(SendMonitorEvent));
                GetAppServerDelegate getAppServer = (m_sipCallDispatcherFile != null) ? new GetAppServerDelegate(m_sipCallDispatcherFile.GetAppServer) : null;
                m_proxyScriptFacade = new SIPProxyScriptFacade(
                    new SIPMonitorLogDelegate(SendMonitorEvent),         // Don't use the m_proxyLogger delegate directly here as doing so caused stack overflow exceptions in the IronRuby engine.
                    sipTransport,
                    m_proxyDispatcher,
                    getAppServer);

                m_scriptLoader = new ScriptLoader(SendMonitorEvent, m_scriptPath);
                m_scriptLoader.ScriptFileChanged += (s, e) => { m_compiledScript = m_scriptLoader.GetCompiledScript(); };
                m_compiledScript = m_scriptLoader.GetCompiledScript();

                // Events that pass the SIP requests and responses onto the Stateless Proxy Core.
                m_sipTransport.SIPTransportRequestReceived += GotRequest;
                m_sipTransport.SIPTransportResponseReceived += GotResponse;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPProxyCore (ctor). " + excp.Message);
                throw excp;
            }
        }

        /// <summary>
        ///  From RFC3261: Stateless Proxy Request Processing:
        ///  
        ///  For each target, the proxy forwards the request following these
        ///   1.  Make a copy of the received request
        ///   2.  Update the Request-URI
        ///   3.  Update the Max-Forwards header field
        ///   4.  Optionally add a Record-route header field value
        ///   5.  Optionally add additional header fields
        ///   6.  Postprocess routing information
        ///   7.  Determine the next-hop address, port, and transport
        ///   8.  Add a Via header field value
        ///   9.  Add a Content-Length header field if necessary
        ///  10.  Forward the new request
        ///  
        ///  See sections 12.2.1.1 and 16.12.1.2 in the SIP RFC for the best explanation on the way the Route header works.
        /// </summary>
        /// <param name="sipRequest"></param>
        /// <param name="inEndPoint">End point the request was received on.</param>
        /// <param name="sipChannel"></param>
        private void GotRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            try
            {
                // Calculate the proxy branch parameter for this request. The branch parameter has to be calculated so that INVITE's and CANCEL's generate the same branchid.
                //string toTag = (sipRequest.Header.To != null) ? sipRequest.Header.To.ToTag : null;
                //string fromTag = (sipRequest.Header.From != null) ? sipRequest.Header.From.FromTag : null;
                string route = (sipRequest.Header.Routes != null) ? sipRequest.Header.Routes.ToString() : null;
                //string authHeader = (sipRequest.Header.AuthenticationHeader != null) ? sipRequest.Header.AuthenticationHeader.ToString() : null;
                string proxyBranch = CallProperties.CreateBranchId(SIPConstants.SIP_BRANCH_MAGICCOOKIE, null, null, sipRequest.Header.CallId, sipRequest.URI.ToString(), null, sipRequest.Header.CSeq, route, null, null);
                // Check whether the branch parameter already exists in the Via list.
                foreach (SIPViaHeader viaHeader in sipRequest.Header.Vias.Via)
                {
                    if (viaHeader.Branch == proxyBranch)
                    {
                        SendMonitorEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.SIPProxy, SIPMonitorEventTypesEnum.Warn, "Loop detected on request from " + remoteEndPoint + " to " + sipRequest.URI.ToString() + ".", null));
                        m_sipTransport.SendResponse(SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.LoopDetected, null));
                        return;
                    }
                }

                // Used in the proxy monitor messages only, plays no part in request routing.
                string fromUser = (sipRequest.Header.From != null) ? sipRequest.Header.From.FromURI.User : null;
                string toUser = (sipRequest.Header.To != null) ? sipRequest.Header.To.ToURI.User : null;
                string summaryStr = "req " + sipRequest.Method + " from=" + fromUser + ", to=" + toUser + ", " + remoteEndPoint.ToString();

                bool isFromAppServer = (m_sipCallDispatcherFile != null) ? m_sipCallDispatcherFile.IsAppServerEndPoint(remoteEndPoint) : false;

                lock (this)
                {
                    m_compiledScript.DefaultScope.RemoveVariable("sys");
                    m_compiledScript.DefaultScope.RemoveVariable("localEndPoint");
                    m_compiledScript.DefaultScope.RemoveVariable("isreq");
                    m_compiledScript.DefaultScope.RemoveVariable("req");
                    m_compiledScript.DefaultScope.RemoveVariable("remoteEndPoint");
                    m_compiledScript.DefaultScope.RemoveVariable("summary");
                    m_compiledScript.DefaultScope.RemoveVariable("proxyBranch");
                    m_compiledScript.DefaultScope.RemoveVariable("sipMethod");
                    m_compiledScript.DefaultScope.RemoveVariable("publicip");
                    m_compiledScript.DefaultScope.RemoveVariable("IsFromAppServer");

                    m_compiledScript.DefaultScope.SetVariable("sys", m_proxyScriptFacade);
                    m_compiledScript.DefaultScope.SetVariable("localEndPoint", localSIPEndPoint);
                    m_compiledScript.DefaultScope.SetVariable("isreq", true);
                    m_compiledScript.DefaultScope.SetVariable("req", sipRequest);
                    m_compiledScript.DefaultScope.SetVariable("remoteEndPoint", remoteEndPoint);
                    m_compiledScript.DefaultScope.SetVariable("summary", summaryStr);
                    m_compiledScript.DefaultScope.SetVariable("proxyBranch", proxyBranch);
                    m_compiledScript.DefaultScope.SetVariable("sipMethod", sipRequest.Method.ToString());
                    m_compiledScript.DefaultScope.SetVariable("publicip", PublicIPAddress);
                    m_compiledScript.DefaultScope.SetVariable("IsFromAppServer", isFromAppServer);

                    m_compiledScript.Execute();
                }


                //if (requestStopwatch.ElapsedMilliseconds > 20)
                //{
                //    logger.Debug("GotRequest processing time=" + requestStopwatch.ElapsedMilliseconds + "ms, script time=" + scriptStopwatch.ElapsedMilliseconds + "ms.");
                //}
            }
            catch (SIPValidationException)
            {
                throw;
            }
            catch (Exception excp)
            {
                string reqExcpError = "Exception SIPProxyCore GotRequest. " + excp.Message;
                logger.Error(reqExcpError);
                SIPMonitorEvent reqExcpEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.SIPProxy, SIPMonitorEventTypesEnum.Error, reqExcpError, localSIPEndPoint, remoteEndPoint, null);
                SendMonitorEvent(reqExcpEvent);

                throw excp;
            }
        }

        /// <summary>
        ///  From RFC3261: Stateless Proxy Response Processing:
        ///  
        /// When a response arrives at a stateless proxy, the proxy MUST inspect the sent-by value in the first
        /// (topmost) Via header field value.  If that address matches the proxy, (it equals a value this proxy has 
        /// inserted into previous requests) the proxy MUST remove that header field value from the response and  
        /// forward the result to the location indicated in the next Via header field value.  The proxy MUST NOT add 
        /// to, modify, or remove the message body.  Unless specified otherwise, the proxy MUST NOT remove
        /// any other header field values.  If the address does not match the  proxy, the message MUST be silently discarded.
        /// </summary>
        private void GotResponse(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse)
        {
            try
            {
                // Used in the proxy monitor messages only, plays no part in response processing.
                string fromUser = (sipResponse.Header.From != null) ? sipResponse.Header.From.FromURI.User : null;
                string toUser = (sipResponse.Header.To != null) ? sipResponse.Header.To.ToURI.User : null;
                string summaryStr = "resp " + sipResponse.Header.CSeqMethod + " from=" + fromUser + ", to=" + toUser + ", " + remoteEndPoint.ToString();

                SIPViaHeader topVia = sipResponse.Header.Vias.PopTopViaHeader();
                SIPEndPoint outSocket = localSIPEndPoint;

                // If the second Via header on the response was also set by this proxy it means the request was originally received and forwarded
                // on different sockets. To get the response to travel the same path in reverse it must be forwarded from the proxy socket indicated
                // by the second top Via.
                if (sipResponse.Header.Vias.Length > 0)
                {
                    SIPViaHeader nextTopVia = sipResponse.Header.Vias.TopViaHeader;
                    SIPEndPoint nextTopViaSIPEndPoint = new SIPEndPoint(nextTopVia.Transport, IPSocket.ParseSocketString(nextTopVia.ContactAddress));
                    if (m_sipTransport.IsLocalSIPEndPoint(nextTopViaSIPEndPoint) || (PublicIPAddress != null && nextTopViaSIPEndPoint.SocketEndPoint.Address.ToString() == PublicIPAddress.ToString()))
                    {
                        sipResponse.Header.Vias.PopTopViaHeader();
                        outSocket = nextTopViaSIPEndPoint;
                    }
                }

                bool isFromAppServer = (m_sipCallDispatcherFile != null) ?  m_sipCallDispatcherFile.IsAppServerEndPoint(remoteEndPoint) : false;

                lock (this)
                {
                    m_compiledScript.DefaultScope.RemoveVariable("sys");
                    m_compiledScript.DefaultScope.RemoveVariable("isreq");
                    m_compiledScript.DefaultScope.RemoveVariable("localEndPoint");
                    m_compiledScript.DefaultScope.RemoveVariable("outSocket");
                    m_compiledScript.DefaultScope.RemoveVariable("resp");
                    m_compiledScript.DefaultScope.RemoveVariable("remoteEndPoint");
                    m_compiledScript.DefaultScope.RemoveVariable("summary");
                    m_compiledScript.DefaultScope.RemoveVariable("sipMethod");
                    m_compiledScript.DefaultScope.RemoveVariable("topVia");
                    m_compiledScript.DefaultScope.RemoveVariable("IsFromAppServer");

                    m_compiledScript.DefaultScope.SetVariable("sys", m_proxyScriptFacade);
                    m_compiledScript.DefaultScope.SetVariable("isreq", false);
                    m_compiledScript.DefaultScope.SetVariable("localEndPoint", localSIPEndPoint);
                    m_compiledScript.DefaultScope.SetVariable("outSocket", outSocket);
                    m_compiledScript.DefaultScope.SetVariable("resp", sipResponse);
                    m_compiledScript.DefaultScope.SetVariable("remoteEndPoint", remoteEndPoint);
                    m_compiledScript.DefaultScope.SetVariable("summary", summaryStr);
                    m_compiledScript.DefaultScope.SetVariable("sipMethod", sipResponse.Header.CSeqMethod.ToString());
                    m_compiledScript.DefaultScope.SetVariable("topVia", topVia);
                    m_compiledScript.DefaultScope.SetVariable("IsFromAppServer", isFromAppServer);

                    m_compiledScript.Execute();
                }

                //if (responseStopwatch.ElapsedMilliseconds > 20)
                //{
                //    logger.Debug("GotResponse processing time=" + responseStopwatch.ElapsedMilliseconds + "ms, script time=" + scriptStopwatch.ElapsedMilliseconds + "ms.");
                //}
            }
            catch (Exception excp)
            {
                string respExcpError = "Exception SIPProxyCore GotResponse. " + excp.Message;
                logger.Error(respExcpError);
                SIPMonitorEvent respExcpEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.SIPProxy, SIPMonitorEventTypesEnum.Error, respExcpError, localSIPEndPoint, remoteEndPoint, null);
                SendMonitorEvent(respExcpEvent);

                throw excp;
            }
        }

        private void SendMonitorEvent(SIPMonitorEventTypesEnum eventType, string message, SIPEndPoint localEndPoint, SIPEndPoint remoteEndPoint, SIPEndPoint dstEndPoint)
        {
            SIPMonitorEvent proxyEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.SIPProxy, eventType, message, localEndPoint, remoteEndPoint, dstEndPoint);
            SendMonitorEvent(proxyEvent);
        }

        private void SendMonitorEvent(SIPMonitorEvent monitorEvent)
        {
            if (m_proxyLogger != null)
            {
                m_proxyLogger(monitorEvent);
            }
        }

        #region Unit testing.

#if UNITTEST

        #region Mock Objects

		/*public class ProxySIPChannel
		{		
			public IPEndPoint m_unitTestEndPoint;	// Used so that tests using this mock object can see the end point a message was sent to.

			public ProxySIPChannel()
			{

			}
		
			public ProxySIPChannel(IPEndPoint endPoint)
			{

			}
		
			public void Send(IPEndPoint endPoint, SIPResponse sipResponse)
			{
				m_unitTestEndPoint = endPoint;
				
				Console.WriteLine("Dummy Channel: Sending to: " + endPoint.Address.ToString() + ":" + endPoint.Port + "\r\n" + sipResponse.ToString());
			}

			public void Send(IPEndPoint endPoint, SIPRequest sipRequest)
			{
				m_unitTestEndPoint = endPoint;
				
				Console.WriteLine("Dummy Channel: Sending to: " + endPoint.Address.ToString() + ":" + endPoint.Port + "\r\n" + sipRequest.ToString());
			}
		}*/
			

        #endregion

		/*
		[TestFixture]
		public class StatelessProxyCoreUnitTest
		{
			private static string m_CRLF = SIPConstants.CRLF;
			
			private static XmlDocument m_sipRoutes = new XmlDocument();
			
			[TestFixtureSetUp]
			public void Init()
			{
				string sipRoutesXML = 
					"<siproutes>" +
					" <inexsipserversocket>192.168.1.28:5061</inexsipserversocket>" +
					" <sipserversocket>192.168.1.28:5061</sipserversocket>" +
					"</siproutes>";
				m_sipRoutes.LoadXml(sipRoutesXML);
			}

			[TestFixtureTearDown]
			public void Dispose()
			{			
				
			}

			[Test]
			public void SampleTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				Assert.IsTrue(true, "True was false.");

				Console.WriteLine("---------------------------------"); 
			}

			
			[Test]
				//[Ignore("Next hop tests only.")]
			public void ResponseAddViaHeaderTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				Assert.IsTrue(true, "True was false.");

				Console.WriteLine("---------------------------------"); 
			}

			
		
			[Test]
			//[Ignore("Next hop tests only.")]
			public void ParseRouteNotForThisProxyBYEUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string sipRequest = 
					"BYE sip:bluesipd@192.168.1.2:5065 SIP/2.0" + m_CRLF +
					"Via: SIP/2.0/UDP 213.168.225.133:5060;branch=z9hG4bK74ab714b;rport" + m_CRLF +	
					"From: <sip:303@bluesipd>;tag=as6a65fae3" + m_CRLF +
					"To: bluesipd <sip:bluesipd@bluesipd:5065>;tag=1898247079" + m_CRLF +
					"Contact: <sip:303@213.168.225.133>" + m_CRLF +
					"Call-ID: 80B34165-8C89-4623-B862-40AFB1884071@192.168.1.2" + m_CRLF +
					"CSeq: 102 BYE" + m_CRLF +
					"Route: <sip:bluesipd@12.12.12.12:5065;lr>" + m_CRLF +
					"Content-Length: 0" + m_CRLF+ m_CRLF;

				SIPMessage sipMsg = SIPMessage.ParseSIPMessage(Encoding.ASCII.GetBytes(sipRequest), new IPEndPoint(IPAddress.Loopback, 9998));
				SIPRequest byeReq = SIPRequest.ParseSIPRequest(sipMsg);
				
				IPEndPoint proxyEndPoint = new IPEndPoint(IPAddress.Loopback, 19998);
				ProxySIPChannel dummySIPChannel = new ProxySIPChannel(proxyEndPoint, null);
				StatelessProxyCore proxyCore = new StatelessProxyCore(dummySIPChannel, proxyEndPoint, null, null, true, null, null);

				IPEndPoint dummyRcvdEndPoint = new IPEndPoint(IPAddress.Loopback, 19999);
				proxyCore.GotRequest(proxyEndPoint, dummyRcvdEndPoint, byeReq);

				//Assert.IsTrue(dummySIPChannel.m_unitTestEndPoint.Address.ToString() == "12.12.12.12", "The IP address for the UA end point was not correctly extracted, extracted address " + IPSocketAddress.GetSocketString(dummySIPChannel.m_unitTestEndPoint) + ".");
				//Assert.IsTrue(dummySIPChannel.m_unitTestEndPoint.Port == 5065, "The IP port for the UA end point was not correctly extracted.");
				Assert.IsTrue(byeReq.URI.ToString() == "sip:bluesipd@192.168.1.2:5065", "The SIP URI was incorrect.");
				Assert.IsTrue(byeReq.Header.Routes.TopRoute.ToString() == "<sip:bluesipd@12.12.12.12:5065;lr>", "The top route was incorrect.");

				Console.WriteLine("-----------------------------------------");
			}

			[Test]
			//[Ignore("Next hop tests only.")]
			public void ParseStrictRouteNotForThisProxyBYEUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string sipRequest = 
					"BYE sip:bluesipd@192.168.1.2:5065 SIP/2.0" + m_CRLF +
					"Via: SIP/2.0/UDP 213.168.225.133:5060;branch=z9hG4bK74ab714b;rport" + m_CRLF +	
					"From: <sip:303@bluesipd>;tag=as6a65fae3" + m_CRLF +
					"To: bluesipd <sip:bluesipd@bluesipd:5065>;tag=1898247079" + m_CRLF +
					"Contact: <sip:303@213.168.225.133>" + m_CRLF +
					"Call-ID: 80B34165-8C89-4623-B862-40AFB1884071@192.168.1.2" + m_CRLF +
					"CSeq: 102 BYE" + m_CRLF +
					"Route: <sip:bluesipd@12.12.12.12:5065>" + m_CRLF +
					"Content-Length: 0" + m_CRLF+ m_CRLF;

				SIPMessage sipMsg = SIPMessage.ParseSIPMessage(Encoding.ASCII.GetBytes(sipRequest), new IPEndPoint(IPAddress.Loopback, 9998));
				SIPRequest byeReq = SIPRequest.ParseSIPRequest(sipMsg);
				
				IPEndPoint proxyEndPoint = new IPEndPoint(IPAddress.Loopback, 19998);
				ProxySIPChannel dummySIPChannel = new ProxySIPChannel(proxyEndPoint);
				StatelessProxyCore proxyCore = new StatelessProxyCore(dummySIPChannel, proxyEndPoint, null, null, true, null, null);

				IPEndPoint dummyRcvdEndPoint = new IPEndPoint(IPAddress.Loopback, 19999);
				proxyCore.GotRequest(proxyEndPoint, dummyRcvdEndPoint, byeReq);

				//Assert.IsTrue(dummySIPChannel.m_unitTestEndPoint.Address.ToString() == "12.12.12.12", "The IP address for the UA end point was not correctly extracted, extracted address " + IPSocketAddress.GetSocketString(dummySIPChannel.m_unitTestEndPoint) + ".");
				//Assert.IsTrue(dummySIPChannel.m_unitTestEndPoint.Port == 5065, "The IP port for the UA end point was not correctly extracted.");
				//Assert.IsTrue(dummySIPChannel.m_unitTestEndPoint.Address.ToString() == "12.12.12.12", "The IP address for the UA end point was not correctly extracted, extracted address " + IPSocketAddress.GetSocketString(dummySIPChannel.m_unitTestEndPoint) + ".");
				//Assert.IsTrue(dummySIPChannel.m_unitTestEndPoint.Port == 5065, "The IP port for the UA end point was not correctly extracted.");
				Assert.IsTrue(byeReq.Header.Routes.TopRoute.ToString() == "sip:bluesipd@192.168.1.2:5065", "The SIP URI was incorrect.");
				Assert.IsTrue(byeReq.URI.ToString() == "<sip:bluesipd@12.12.12.12:5065;lr>", "The top route was incorrect.");

				Console.WriteLine("-----------------------------------------");
			}

			[Test]
				//[Ignore("Next hop tests only.")]
			public void ParseRouteForThisProxyBYEUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				IPEndPoint proxyEndPoint = new IPEndPoint(IPAddress.Loopback, 19998);

				string sipRequest = 
					"BYE sip:bluesipd@192.168.1.2:5065 SIP/2.0" + m_CRLF +
					"Via: SIP/2.0/UDP 213.168.225.133:5060;branch=z9hG4bK74ab714b;rport" + m_CRLF +	
					"From: <sip:303@bluesipd>;tag=as6a65fae3" + m_CRLF +
					"To: bluesipd <sip:bluesipd@bluesipd:5065>;tag=1898247079" + m_CRLF +
					"Contact: <sip:303@213.168.225.133>" + m_CRLF +
					"Call-ID: 80B34165-8C89-4623-B862-40AFB1884071@192.168.1.2" + m_CRLF +
					"CSeq: 102 BYE" + m_CRLF +
					"Route: <sip:" + IPSocketAddress.GetSocketString(proxyEndPoint) + ";lr>" + m_CRLF +
					"Content-Length: 0" + m_CRLF+ m_CRLF;

				SIPMessage sipMsg = SIPMessage.ParseSIPMessage(Encoding.ASCII.GetBytes(sipRequest), new IPEndPoint(IPAddress.Loopback, 9998));
				SIPRequest byeReq = SIPRequest.ParseSIPRequest(sipMsg);
				
				ProxySIPChannel dummySIPChannel = new ProxySIPChannel(proxyEndPoint);
				StatelessProxyCore proxyCore = new StatelessProxyCore(dummySIPChannel, proxyEndPoint, null, null, true, null, null);

				IPEndPoint dummyRcvdEndPoint = new IPEndPoint(IPAddress.Loopback, 19999);
				proxyCore.GotRequest(proxyEndPoint, dummyRcvdEndPoint, byeReq);

				Assert.IsTrue(byeReq.URI.ToString() == "sip:bluesipd@192.168.1.2:5065", "The SIP URI was incorrect.");
				//Assert.IsTrue(dummySIPChannel.m_unitTestEndPoint.Address.ToString() == "12.12.12.12", "The IP address for the UA end point was not correctly extracted, extracted address " + IPSocketAddress.GetSocketString(dummySIPChannel.m_unitTestEndPoint) + ".");
				//Assert.IsTrue(dummySIPChannel.m_unitTestEndPoint.Port == 5065, "The IP port for the UA end point was not correctly extracted.");

				Console.WriteLine("-----------------------------------------");
			}

			[Test]
			//[Ignore("Next hop tests only.")]
			public void TooManyHopsUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string sipRequest = 
					"BYE sip:bluesipd@192.168.1.2:5065 SIP/2.0" + m_CRLF +
					"Via: SIP/2.0/UDP 213.168.225.133:5060;branch=z9hG4bK74ab714b;rport" + m_CRLF +
					"Route: <sip:bluesipd@12.12.12.12:5065>" + m_CRLF +
					"From: <sip:303@bluesipd>;tag=as6a65fae3" + m_CRLF +
					"To: bluesipd <sip:bluesipd@bluesipd:5065>;tag=1898247079" + m_CRLF +
					"Contact: <sip:303@213.168.225.133>" + m_CRLF +
					"Call-ID: 80B34165-8C89-4623-B862-40AFB1884071@192.168.1.2" + m_CRLF +
					"CSeq: 102 BYE" + m_CRLF +
					"Max-Forwards: 0" + m_CRLF +
					"User-Agent: asterisk" + m_CRLF +
					"Content-Length: 0" + m_CRLF+ m_CRLF;

				SIPMessage sipMsg = SIPMessage.ParseSIPMessage(Encoding.ASCII.GetBytes(sipRequest), new IPEndPoint(IPAddress.Loopback, 9998));
				SIPRequest byeReq = SIPRequest.ParseSIPRequest(sipMsg);
				
				IPEndPoint proxyEndPoint = new IPEndPoint(IPAddress.Loopback, 19998);
				ProxySIPChannel dummySIPChannel = new ProxySIPChannel(proxyEndPoint);
				StatelessProxyCore proxyCore = new StatelessProxyCore(dummySIPChannel, proxyEndPoint, null, null, true, null, null);

				IPEndPoint dummyRcvdEndPoint = new IPEndPoint(IPAddress.Loopback, 19999);
				proxyCore.GotRequest(proxyEndPoint, dummyRcvdEndPoint, byeReq);

				//Assert.IsTrue(dummyChannel.m_unitTestEndPoint.Address.ToString() == "12.12.12.12", "The IP address for the UA end point was not correctly extracted, extracted address " + dummyChannel.m_unitTestEndPoint.Address.ToString() + ".");
				//ssert.IsTrue(dummyChannel.m_unitTestEndPoint.Port == 5065, "The IP port for the UA end point was not correctly extracted.");

				Console.WriteLine("-----------------------------------------");
			}
	
			[Test]
			public void DetermineHextHopUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
				StatelessProxyCore proxy = new StatelessProxyCore(null, 0);
				string uri = "sip:303@213.168.225.133";
				IPEndPoint nextHop = proxy.DetermineNextHop(uri);
				Console.WriteLine("The next hop for uri = " + uri + " is " + nextHop.Address.ToString() + ":" + nextHop.Port + ".");
				Assert.IsTrue(nextHop.Address.ToString() == "213.168.225.133", "The next hop IP address was not correctly determined from the request URI.");
				Assert.IsTrue(nextHop.Port == 5060, "The next hop port was not correctly determined from the request URI.");
			}


			[Test]
			public void DetermineHextHopDifferentServerUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
				StatelessProxyCore proxy = new StatelessProxyCore(null, 0);
				string uri = "sip:303@213.168.225.135";
				IPEndPoint nextHop = proxy.DetermineNextHop(uri);
				Console.WriteLine("The next hop for uri = " + uri + " is " + nextHop.Address.ToString() + ":" + nextHop.Port + ".");
				Assert.IsTrue(nextHop.Address.ToString() == "213.168.225.135", "The next hop IP address was not correctly determined from the request URI.");
				Assert.IsTrue(nextHop.Port == 5060, "The next hop port was not correctly determined from the request URI.");
			}

		
			[Test]
			public void DetermineHextHopWithPortUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
				StatelessProxyCore proxy = new StatelessProxyCore(null, 0);
				string uri = "sip:303@213.168.225.133:5066";
				IPEndPoint nextHop = proxy.DetermineNextHop(uri);
				Console.WriteLine("The next hop for uri = " + uri + " is " + nextHop.Address.ToString() + ":" + nextHop.Port + ".");
				Assert.IsTrue(nextHop.Address.ToString() == "213.168.225.133", "The next hop IP address was not correctly determined from the request URI.");
				Assert.IsTrue(nextHop.Port== 5066, "The next hop port was not correctly determined from the request URI.");
			}

		
			[Test]
			public void DetermineHextHopWithInvalidURIUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
				StatelessProxyCore proxy = new StatelessProxyCore(null, 0);
				string uri = "sip:303@myserver";
				IPEndPoint nextHop = proxy.DetermineNextHop(uri);
				Console.WriteLine("The next hop for uri = " + uri + " is " + nextHop.Address.ToString() + ":" + nextHop.Port + ".");
				Assert.IsTrue(nextHop.Address.ToString() == "213.168.225.133", "The next hop IP address was not correctly determined from the request URI.");
				Assert.IsTrue(nextHop.Port== 5060, "The next hop port was not correctly determined from the request URI.");
			}

			[Test]
			public void CredentialsPassThruUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
				string registerMsg = 
					"REGISTER sip:blueface SIP/2.0" + m_CRLF +
					"Via: SIP/2.0/UDP 192.168.1.2:5066;branch=z9hG4bK60341d2470" + m_CRLF +
					"From: \"aaron\" <sip:aaron@blueface>;tag=632743740325493750" + m_CRLF +
					"To: \"aaron\" <sip:aaron@blueface>" + m_CRLF +
					"Contact: <sip:aaron@220.240.255.198:50652>" + m_CRLF +
					"Call-ID: 1540e4c48d3b447@192.168.1.2" + m_CRLF +
					"CSeq: 550 REGISTER" + m_CRLF +
					"Max-Forwards: 70" + m_CRLF +
					"Expires: 600" + m_CRLF +
					"Authorization: Digest username=\"aaron\",realm=\"asterisk\",nonce=\"422e215b\",response=\"af05b4f63e3593c449ad9eac9f0443e4\",uri=\"sip:blueface\"" + m_CRLF + m_CRLF;
				StatelessProxyCore proxy = new StatelessProxyCore(null, 0);
				SIPRequest registerReq = SIPRequest.ParseSIPRequest(registerMsg);
				
				StatelessProxyCore proxyCore = new StatelessProxyCore("213.168.225.135", 5060);

				SIPChannel dummyChannel = new SIPChannel();
				proxyCore.GotRequest(registerReq, new IPEndPoint(IPAddress.Parse("12.12.12.12"), 5060), dummyChannel);

				Console.WriteLine("-----------------------------------------");	
			}

			[Test]
			public void CredentialsPassThruNonceAndRealmResposeUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
				string unauthRespStr = 
					"SIP/2.0 401 Unauthorized" + m_CRLF +
					"Via: SIP/2.0/UDP 213.168.225.135:5060;branch=z9hG4bKbaec65a4f8" + m_CRLF +
					"Via: SIP/2.0/UDP 192.168.1.2:5066;received=220.240.255.198:63994;branch=z9hG4bKbaec65a4f9" + m_CRLF +
					"From: \"aaron\" <sip:aaron@blueface>;tag=632743740325493750" + m_CRLF +
					"To: \"aaron\" <sip:aaron@blueface>;tag=as7b152585" + m_CRLF +
					"Contact: <sip:aaron@213.168.225.133>" + m_CRLF +
					"Call-ID: 1540e4c48d3b447@192.168.1.2" + m_CRLF +
					"CSeq: 549 REGISTER" + m_CRLF +
					"Max-Forwards: 70" + m_CRLF +
					"User-Agent: asterisk" + m_CRLF +
					"WWW-Authenticate: Digest realm=\"asterisk\",nonce=\"422e215b\"" + m_CRLF +
					"Record-Route: <sip:213.168.225.136:6060;lr>" + m_CRLF +
					"Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY" + m_CRLF + m_CRLF;
				StatelessProxyCore proxy = new StatelessProxyCore(null, 0);
				SIPResponse unauthResp = SIPResponse.ParseSIPResponse(unauthRespStr);
				
				StatelessProxyCore proxyCore = new StatelessProxyCore("213.168.225.135", 5060);

				SIPChannel dummyChannel = new SIPChannel();
				proxyCore.GotResponse(unauthResp, new IPEndPoint(IPAddress.Parse("12.12.12.12"), 5060), dummyChannel);

				Console.WriteLine("-----------------------------------------");	
			}						
		}
		*/
#endif

        #endregion
    }
}
