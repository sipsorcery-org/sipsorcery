// ============================================================================
// FileName: StatelessProxyCore.cs
//
// Description:
// A stateless SIP proxy core to demonstrate SIP Proxy functionality.
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
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using IronPython.Compiler;
using IronPython.Hosting;
using IronRuby;
using log4net;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{	  
    public class StatelessProxyCore
	{
        private enum ProxyScriptEnum
        {
            None = 0,
            Python = 1,
            Ruby = 2,
        }

        public const string CHANNEL_NAME_KEY = "cn";
        private const string PYTHON_SCRIPT_EXTENSION = ".py";
        private const string RUBY_SCRIPT_EXTENSION = ".rb";
        private const string JAVASCRIPT_SCRIPT_EXTENSION = ".js";

        private static ILog logger = log4net.LogManager.GetLogger("sipproxy");

        private SIPMonitorLogDelegate m_proxyLogger = (e) => { };

        private SIPTransport m_sipTransport;
        private ScriptScope m_scriptScope;
        private Microsoft.Scripting.Hosting.CompiledCode m_rubyCompiledScript;
        private PythonEngine m_pythonEngine;
        private IronPython.Hosting.CompiledCode m_pythonCompiledScript;
        private string m_scriptPath;
        private ProxyScriptEnum m_proxyScriptType;
        private string m_proxyScript;
        private StatelessProxyScriptHelper m_proxyScriptHelper;
        private DateTime m_lastScriptChange = DateTime.MinValue;
        private Dictionary<string, SIPDispatcherJob> m_dispatcherJobs = new Dictionary<string, SIPDispatcherJob>();
        private SIPTransport m_dispatcherSIPTransport;
        private XmlNode m_dispatcherJobsNode;

        public IPAddress PublicIPAddress;       // Can be set if there is an object somewhere that knows the public IP. The address wil be available in the proxy runtime script.

		public StatelessProxyCore(
            SIPMonitorLogDelegate proxyLogger,
            SIPTransport sipTransport,
            string scriptPath,
            XmlNode dispatcherJobsNode)
		{
			try
			{
                m_proxyLogger = proxyLogger ?? m_proxyLogger;
                m_scriptPath = scriptPath;
                m_dispatcherJobsNode = dispatcherJobsNode;
                m_sipTransport = sipTransport;
                m_proxyScriptHelper = new StatelessProxyScriptHelper(
                    new SIPMonitorLogDelegate(SendMonitorEvent),         // Don't use the m_proxyLogger delegate directly here as doing so caused stack overflow exceptions in the IronRuby engine.
                    sipTransport);

                if (!File.Exists(m_scriptPath))
                {
                    throw new ApplicationException("Cannot instantiate SIP Proxy without a script file.");
                }

                StreamReader sr = new StreamReader(m_scriptPath);
                m_proxyScript = sr.ReadToEnd();
                sr.Close();

                // File system watcher needs a fully qualified path.
                if (!m_scriptPath.Contains(Path.DirectorySeparatorChar.ToString()))
                {
                    m_scriptPath = Environment.CurrentDirectory + Path.DirectorySeparatorChar + m_scriptPath;
                }

                FileSystemWatcher runtimeWatcher = new FileSystemWatcher(Path.GetDirectoryName(m_scriptPath), Path.GetFileName(m_scriptPath));
                runtimeWatcher.Changed += new FileSystemEventHandler(ProxyScriptChanged);
                runtimeWatcher.EnableRaisingEvents = true;

                // Configure script engine.
                m_proxyScriptType = GetProxyScriptType(m_scriptPath);

                if (m_proxyScriptType == ProxyScriptEnum.Python)
                {
                    logger.Debug("Stateless proxy script is Python.");
                    m_pythonEngine = new PythonEngine();
                    m_pythonCompiledScript = m_pythonEngine.Compile(m_proxyScript);
                }
                else if (m_proxyScriptType == ProxyScriptEnum.Ruby)
                {
                    logger.Debug("Stateless proxy script is Ruby.");
                    ScriptRuntime scriptRuntime = IronRuby.Ruby.CreateRuntime();
                    m_scriptScope = scriptRuntime.CreateScope("IronRuby");
                    m_rubyCompiledScript = m_scriptScope.Engine.CreateScriptSourceFromString(m_proxyScript).Compile();
                }
                else
                {
                    throw new ApplicationException("Stateless Proxy Core cannot start, unrecognised proxy script type " + m_proxyScriptType + ".");
                }

                // Events that pass the SIP requests and responses onto the Stateless Proxy Core.
                m_sipTransport.SIPTransportRequestReceived += GotRequest;
                m_sipTransport.SIPTransportResponseReceived += GotResponse;

                if (m_dispatcherJobsNode != null && m_dispatcherJobsNode.ChildNodes.Count > 0) {
                    try {
                        SIPChannel dispatcherChannel = new SIPUDPChannel(new IPEndPoint(IPAddress.Loopback, 7080));
                        m_dispatcherSIPTransport = new SIPTransport(SIPDNSManager.Resolve, new SIPTransactionEngine(), dispatcherChannel, true, false); 

                        foreach (XmlNode dispatcherNode in m_dispatcherJobsNode.ChildNodes) {
                            string jobType = dispatcherNode.Attributes.GetNamedItem("class").Value;
                            string jobKey = dispatcherNode.Attributes.GetNamedItem("key").Value;

                            if (!jobKey.IsNullOrBlank() && !jobType.IsNullOrBlank()) {
                                SIPDispatcherJob job = SIPDispatcherJobFactory.CreateJob(jobType, dispatcherNode, m_dispatcherSIPTransport);
                                if (job != null && !m_dispatcherJobs.ContainsKey(jobKey)) {
                                    ThreadPool.QueueUserWorkItem(delegate { job.Start(); });
                                    m_dispatcherJobs.Add(jobKey, job);
                                }
                            }
                            else {
                                logger.Warn("The job key or class were empty for a SIPDispatcherJob node.\n" + dispatcherNode.OuterXml);
                            }
                        }
                    }
                    catch (Exception dispatcherExcp) {
                        logger.Error("Exception StatelessProxyCore Starting Dispatcher. " + dispatcherExcp.Message);
                    }
                }
                else {
                    logger.Debug("No dispatcher jobs were supplied to the StatelessProxyCore.");
                }
            }
			catch(Exception excp)
			{
				logger.Error("Exception StatelessProxyCore (ctor). " + excp.Message);
				throw excp;
			}
		}

        private void ProxyScriptChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (DateTime.Now.Subtract(m_lastScriptChange).TotalSeconds > 1)
                {
                    m_lastScriptChange = DateTime.Now;  // Prevent double re-loads. The file changed event fires twice when a file is saved.
                    logger.Debug("Reloading proxy script from " + m_scriptPath + ".");

                    string tempPath = Path.GetDirectoryName(m_scriptPath) + Path.DirectorySeparatorChar + Path.GetFileName(m_scriptPath) + ".tmp";
                    File.Copy(m_scriptPath, tempPath, true);

                    StreamReader sr = new StreamReader(tempPath);
                    m_proxyScript = sr.ReadToEnd();
                    sr.Close();

                    if (m_proxyScriptType == ProxyScriptEnum.Python)
                    {
                        m_pythonCompiledScript = m_pythonEngine.Compile(m_proxyScript);
                    }
                    else
                    {
                        m_rubyCompiledScript = m_scriptScope.Engine.CreateScriptSourceFromString(m_proxyScript).Compile();
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ProxyScriptChanged. " + excp.Message);
            }
        }

        private ProxyScriptEnum GetProxyScriptType(string scriptFileName)
        {
            string extension = Path.GetExtension(scriptFileName);

            switch (extension)
            {
                case PYTHON_SCRIPT_EXTENSION:
                    return ProxyScriptEnum.Python;
                case RUBY_SCRIPT_EXTENSION:
                    return ProxyScriptEnum.Ruby;
                default:
                    throw new ApplicationException("The script engine could not be identified for the proxy script with extension of " + extension + ".");
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
                DateTime startTime = DateTime.Now;
                DateTime scriptStartTime = DateTime.Now;

                // Calculate the proxy branch parameter for this request. The branch parameter has to be calculated so that INVITE's and CANCEL's generate the same branchid.
                //string toTag = (sipRequest.Header.To != null) ? sipRequest.Header.To.ToTag : null;
                //string fromTag = (sipRequest.Header.From != null) ? sipRequest.Header.From.FromTag : null;
                string route = (sipRequest.Header.Routes != null) ? sipRequest.Header.Routes.ToString() : null;
                //string authHeader = (sipRequest.Header.AuthenticationHeader != null) ? sipRequest.Header.AuthenticationHeader.ToString() : null;
                string proxyBranch =  CallProperties.CreateBranchId(SIPConstants.SIP_BRANCH_MAGICCOOKIE, null, null, sipRequest.Header.CallId, null, null, sipRequest.Header.CSeq, route, null, null);
                // Check whether the branch parameter already exists in the Via list.
                foreach (SIPViaHeader viaHeader in sipRequest.Header.Vias.Via)
                {
                    if (viaHeader.Branch == proxyBranch)
                    {
                        SendMonitorEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatelessProxy, SIPMonitorEventTypesEnum.Warn, "Loop detected on request from " + remoteEndPoint + " to " + sipRequest.URI.ToString() + ".", null));
                        m_sipTransport.SendResponse(GetProxyResponse(sipRequest, SIPResponseStatusCodesEnum.LoopDetected));
                        return;
                    }
                }

                // Used in the proxy monitor messages only, plays no part in request routing.
                string fromUser = (sipRequest.Header.From != null) ? sipRequest.Header.From.FromURI.User : null;
                string toUser = (sipRequest.Header.To != null) ? sipRequest.Header.To.ToURI.User : null;
                string summaryStr = "req " + sipRequest.Method + " from=" + fromUser + ", to=" + toUser + ", " + remoteEndPoint.ToString();

                lock (this)
                {
                    scriptStartTime = DateTime.Now;
                    //m_scriptScope.Execute(m_proxyScript);

                    if (m_proxyScriptType == ProxyScriptEnum.Python)
                    {
                        m_pythonEngine.Globals["sys"] = m_proxyScriptHelper;
                        m_pythonEngine.Globals["localEndPoint"] = localSIPEndPoint;
                        m_pythonEngine.Globals["channelName"] = m_sipTransport.FindSIPChannel(localSIPEndPoint).Name;
                        m_pythonEngine.Globals["isreq"] = true;
                        m_pythonEngine.Globals["req"] = sipRequest;
                        m_pythonEngine.Globals["remoteEndPoint"] = remoteEndPoint;
                        m_pythonEngine.Globals["summary"] = summaryStr;
                        m_pythonEngine.Globals["proxyBranch"] = proxyBranch;
                        m_pythonEngine.Globals["sipMethod"] = sipRequest.Method.ToString();
                        m_pythonEngine.Globals["publicip"] = PublicIPAddress;

                        foreach (KeyValuePair<string, SIPDispatcherJob> dispatcherJob in m_dispatcherJobs) {
                            m_pythonEngine.Globals[dispatcherJob.Key] = dispatcherJob.Value.GetSIPEndPoint();
                        }

                        m_pythonCompiledScript.Execute();
                    }
                    else
                    {
                        //m_scriptScope.ClearVariables();

                        m_scriptScope.SetVariable("sys", m_proxyScriptHelper);
                        m_scriptScope.SetVariable("localEndPoint", localSIPEndPoint);
                        m_scriptScope.SetVariable("channelName", m_sipTransport.FindSIPChannel(localSIPEndPoint).Name);
                        m_scriptScope.SetVariable("isreq", true);
                        m_scriptScope.SetVariable("req", sipRequest);
                        m_scriptScope.SetVariable("remoteEndPoint", remoteEndPoint);
                        m_scriptScope.SetVariable("summary", summaryStr);
                        m_scriptScope.SetVariable("proxyBranch", proxyBranch);
                        m_scriptScope.SetVariable("sipMethod", sipRequest.Method.ToString());
                        m_scriptScope.SetVariable("publicip", PublicIPAddress);

                        foreach (KeyValuePair<string, SIPDispatcherJob> dispatcherJob in m_dispatcherJobs) {
                            m_scriptScope.SetVariable(dispatcherJob.Key, dispatcherJob.Value.GetSIPEndPoint());
                        }

                        m_rubyCompiledScript.Execute(m_scriptScope);
                    }
                }

                double processingTime = DateTime.Now.Subtract(startTime).TotalMilliseconds;
                if (processingTime > 20)
                {
                    double scriptTime = DateTime.Now.Subtract(scriptStartTime).TotalMilliseconds;
                    logger.Debug("GotRequest processing time=" + processingTime.ToString("0.##") + "ms, script time=" + scriptTime.ToString("0.##") + ".");
                }
            }
            catch (SIPValidationException)
            {
                throw;
            }
            catch (Exception excp)
            {
                string reqExcpError = "Exception StatelessProxyCore GotRequest. " + excp.Message;
                logger.Error(reqExcpError);
                SIPMonitorEvent reqExcpEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatelessProxy, SIPMonitorEventTypesEnum.Error, reqExcpError, localSIPEndPoint, remoteEndPoint, null);
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
                DateTime startTime = DateTime.Now;
                DateTime scriptStartTime = DateTime.Now;

                // Used in the proxy monitor messages only, plays no part in response processing.
                string fromUser = (sipResponse.Header.From != null) ? sipResponse.Header.From.FromURI.User : null;
                string toUser = (sipResponse.Header.To != null) ? sipResponse.Header.To.ToURI.User : null;
                string summaryStr = "resp " + sipResponse.Header.CSeqMethod + " from=" + fromUser + ", to=" + toUser + ", " + remoteEndPoint.ToString();

                SIPViaHeader topVia = sipResponse.Header.Vias.PopTopViaHeader();
                SIPEndPoint outSocket = localSIPEndPoint;

                // If the second Via header on the response was also set by this proxy it means the request was originally recieved and forwarded
                // on different sockets. To get the response to travel the same path in reverse it must be forwarded from the proxy socket indicated
                // by the second top Via.
                if (sipResponse.Header.Vias.Length > 0) {
                    SIPViaHeader nextTopVia = sipResponse.Header.Vias.TopViaHeader;
                    SIPEndPoint nextTopViaSIPEndPoint = new SIPEndPoint(nextTopVia.Transport, IPSocket.ParseSocketString(nextTopVia.ContactAddress));
                    if (m_sipTransport.IsLocalSIPEndPoint(nextTopViaSIPEndPoint)) {
                        sipResponse.Header.Vias.PopTopViaHeader();
                        outSocket = nextTopViaSIPEndPoint;
                    }
                }
                
                /*string channelName = topVia.ViaParameters.Get(CHANNEL_NAME_KEY);
                if (!channelName.IsNullOrBlank())
                {
                    SIPChannel sendFromChannel = m_sipTransport.FindSIPChannel(channelName.Trim());
                    if (sendFromChannel != null)
                    {
                        sipResponse.LocalSIPEndPoint = sendFromChannel.SIPChannelEndPoint;
                    }
                }*/

                lock (this)
                {
                    //m_scriptScope.SetVariable("topVia", proxyVia);

                    scriptStartTime = DateTime.Now;
                    //m_scriptScope.Execute(m_proxyScript);
                    if (m_proxyScriptType == ProxyScriptEnum.Python)
                    {
                        m_pythonEngine.Globals["sys"] = m_proxyScriptHelper;
                        m_pythonEngine.Globals["localEndPoint"] = localSIPEndPoint;
                        m_pythonEngine.Globals["outSocket"] = outSocket;
                        m_pythonEngine.Globals["isreq"] = false;
                        m_pythonEngine.Globals["resp"] = sipResponse;
                        m_pythonEngine.Globals["remoteEndPoint"] = remoteEndPoint;
                        m_pythonEngine.Globals["summary"] = summaryStr;
                        m_pythonEngine.Globals["sipMethod"] = sipResponse.Header.CSeqMethod.ToString();
                        m_pythonEngine.Globals["topVia"] = topVia;

                        m_pythonCompiledScript.Execute();
                    }
                    else
                    {
                        //m_scriptScope.ClearVariables();

                        m_scriptScope.SetVariable("sys", m_proxyScriptHelper);
                        m_scriptScope.SetVariable("isreq", false);
                        m_scriptScope.SetVariable("localEndPoint", localSIPEndPoint);
                        m_pythonEngine.Globals["outSocket"] = outSocket;
                        m_scriptScope.SetVariable("resp", sipResponse);
                        m_scriptScope.SetVariable("remoteEndPoint", remoteEndPoint);
                        m_scriptScope.SetVariable("summary", summaryStr);
                        m_scriptScope.SetVariable("sipMethod", sipResponse.Header.CSeqMethod.ToString());
                        m_scriptScope.SetVariable("topVia", topVia);

                        m_rubyCompiledScript.Execute(m_scriptScope);
                    }
                }

                double processingTime = DateTime.Now.Subtract(startTime).TotalMilliseconds;
                if (processingTime > 20)
                {
                    double scriptTime = DateTime.Now.Subtract(scriptStartTime).TotalMilliseconds;
                    logger.Debug("GotResponse processing time=" + processingTime.ToString("0.##") + "ms, script time=" + scriptTime.ToString("0.##") + ".");
                }
            }
            catch (Exception excp)
            {
                string respExcpError = "Exception StatelessProxyCore GotResponse. " + excp.Message;
                logger.Error(respExcpError);
                SIPMonitorEvent respExcpEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatelessProxy, SIPMonitorEventTypesEnum.Error, respExcpError, localSIPEndPoint, remoteEndPoint, null);
                SendMonitorEvent(respExcpEvent);

                throw excp;
            }
		}

        private SIPResponse GetProxyResponse(SIPRequest sipRequest, SIPResponseStatusCodesEnum responseCode)
        {
            return SIPTransport.GetResponse(sipRequest, responseCode, null);
        }

        private void SendMonitorEvent(SIPMonitorEventTypesEnum eventType, string message, SIPEndPoint localEndPoint, SIPEndPoint remoteEndPoint, SIPEndPoint dstEndPoint)
        {
            SIPMonitorEvent proxyEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatelessProxy, eventType, message, localEndPoint, remoteEndPoint, dstEndPoint);
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
