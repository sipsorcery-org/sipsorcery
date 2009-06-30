//-----------------------------------------------------------------------------
// Filename: SIPDialStringParser.cs
//
// Description: Resolves user provided call strings into structures that can be oassed to other 
// applications to initiate SIP calls.
// 
// History:
// 10 Aug 2008	    Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
//-----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq.Dynamic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.AppServer.DialPlan
{
    /// <remarks>
    /// This class builds a list of calls from a dial plan Dial string. The dial string is an evolving thing and depending on the type of 
    /// dial plan it can take different forms. Some forms are specific to a certain type of dial plan, for example an Asterisk formatted dial
    /// plan can have a long list of options to pass in the dial string whereas in a Ruby dial plan there are more elegant mechanisms. Each 
    /// different type of dial string needs to be described here or there will be processing errors as the different options get overlooked
    /// or forgotten.
    /// 
    /// The original dial strings from the Asterisk formatted dial plans can only forward to a SINGLE destination and use a form of:
    /// Dial(username,password,${EXTEN}@sip.provider.com[,FromUser[,SendToSocket]])
    /// 
    /// The second iteration dial string commands can forward to MULTIPLE destinations and use a form of:
    /// Dial(123@provider1&provider2|123@sip.blueface.ie|provider4&456@provider5[,trace])
    /// 
    /// The From header processing involves special behaviour as it can be customised in different ways. The rules are:
    /// 
    /// 1. By default the From header on the request that initiated the forward will be passed through,
    /// 2.
    /// </remarks>
    public class DialStringParser
    {
        private const char CALLLEG_SIMULTANEOUS_SEPARATOR = '&';
        private const char CALLLEG_FOLLOWON_SEPARATOR = '|';
        private const char CALLLEG_OPTIONS_START_CHAR = '[';
        private const char CALLLEG_OPTIONS_END_CHAR = ']';
        public const char DESTINATION_PROVIDER_SEPARATOR = '@';
        private const string ANON_CALLERS = @"anonymous\.invalid|anonymous|anon";
        
        private static ILog logger = AppState.logger;

        private static string m_anonymousUsername = SIPConstants.SIP_DEFAULT_USERNAME;

        private string m_username;
        private List<SIPProvider> m_sipProviders;
        private SIPMonitorLogDelegate Log_External;
        private SIPAssetGetDelegate<SIPAccount> GetSIPAccount_External;
        private SIPAssetGetListDelegate<SIPRegistrarBinding> GetRegistrarBindings_External;
        private GetCanonicalDomainDelegate GetCanonicalDomain_External;
        private SIPTransport m_sipTransport;

        public DialStringParser(
            SIPTransport sipTransport, 
            string username, 
            List<SIPProvider> sipProviders,
            SIPAssetGetDelegate<SIPAccount> getSIPAccount,
            SIPAssetGetListDelegate<SIPRegistrarBinding> getRegistrarBindings, 
            GetCanonicalDomainDelegate getCanonicalDomainDelegate,
            SIPMonitorLogDelegate logDelegate)
        {
            m_sipTransport = sipTransport;
            m_username = username;
            m_sipProviders = sipProviders;
            GetSIPAccount_External = getSIPAccount;
            GetRegistrarBindings_External = getRegistrarBindings;
            GetCanonicalDomain_External = getCanonicalDomainDelegate;
            Log_External = logDelegate;
        }

        /// <summary>
        /// Parses a dial string that has been used in a dial plan Dial command. The format of the dial string is likely to continue to evolve, check the class
        /// summary for the different formats available. This method determines which format the dial string is in and passes off to the appropriate method to 
        /// build the call list.
        /// </summary>
        /// <param name="dialPlanType">The type of dialplan that generated the Dial command.</param>
        /// <param name="sipRequest">The SIP Request that originated this command. Can be null if the Dial application has been centrally generated.</param>
        /// <param name="command">The Dial command string being parsed.</param>
        /// <param name="customHeaders">If non-empty contains a list of custom SIP headers that will be added to the forwarded request.</param>
        /// <param name="customContentType">If set indicates a custom content type header is required on the forwarded request and it
        /// overrides any other value.</param>
        /// <param name="customContent">If set indicates a custom body is required on the forwarded request and it
        /// overrides any other value.</param>
        /// <param name="callersNetworkId">If the call originated from a locally administered account this will hold the account's
        /// networkid which is used to determine if two local accounts are on the same network and therefore should have their SDP
        /// left alone.</param>
        /// <returns>A queue where each item is a list of calls. If there was only a single forward there would only be one item in the list which contained a 
        /// single call. For dial strings containing multiple forwards each queue item can be a list with multiple calls.</returns>
        public Queue<List<SIPCallDescriptor>> ParseDialString(
            DialPlanContextsEnum dialPlanType,
            SIPRequest sipRequest, 
            string command, 
            HybridDictionary customHeaders,
            string customContentType,
            string customContent,
            string callersNetworkId)
        {
            try
            {
                if (command == null || command.Trim().Length == 0)
                {
                    throw new ArgumentException("The dial string cannot be empty.");
                }
                else
                {
                    Queue<List<SIPCallDescriptor>> prioritisedCallList = new Queue<List<SIPCallDescriptor>>();

                    if (dialPlanType == DialPlanContextsEnum.Line)
                    {
                        // Singled legged call (Asterisk format).
                        SIPCallDescriptor SIPCallDescriptor = ParseAsteriskDialString(command, sipRequest);
                        List<SIPCallDescriptor> callList = new List<SIPCallDescriptor>();
                        callList.Add(SIPCallDescriptor);
                        prioritisedCallList.Enqueue(callList);
                    }
                    else
                    {
                        // Multi legged call (Script sys.Dial format).
                        //string providersString = (command.IndexOf(',') == -1) ? command : command.Substring(0, command.IndexOf(','));
                        prioritisedCallList = ParseScriptDialString(sipRequest, command, customHeaders, customContentType, customContent, callersNetworkId);
                    }

                    return prioritisedCallList;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ParseDialString. " + excp);
                throw excp;
            }
        }

        /// <summary>
        /// Builds the call list based on the original dial plan SwitchCall format. This will result in only a single call leg with a single forward
        /// destination. Examples of the dial string in a dial plan command are:
        ///
        /// exten = number,priority,Switch(username,password,new number[,From[,SendTo[,Trace]]]) 
        ///  or
        /// sys.Dial("username,password,${dst}@provider")
        /// </summary>
        private SIPCallDescriptor ParseAsteriskDialString(string data, SIPRequest sipRequest) {
            try {
                string username = null;
                string password = null;
                string sendToSocket = null;
                bool traceReqd = false;
                string forwardURIStr = null;
                string fromHeaderStr = null;
                SIPURI forwardURI = null;

                string[] dataFields = data.Split(new char[] { ',' });

                username = dataFields[0].Trim().Trim(new char[] { '"', '\'' });
                password = dataFields[1].Trim().Trim(new char[] { '"', '\'' });
                forwardURIStr = dataFields[2].Trim().Trim(new char[] { '"', '\'' });

                if (dataFields.Length > 3 && dataFields[3] != null) {
                    fromHeaderStr = dataFields[3].Trim();
                }

                if (dataFields.Length > 4 && dataFields[4] != null) {
                    sendToSocket = dataFields[4].Trim().Trim(new char[] { '"', '\'' });
                }

                if (dataFields.Length > 5 && dataFields[5] != null) {
                    Boolean.TryParse(dataFields[5].Trim(), out traceReqd);
                }

                forwardURI = SIPURI.ParseSIPURIRelaxed(DialPlanEngine.SubstituteRequestVars(sipRequest, forwardURIStr));
                if (forwardURI != null) {
                    if (forwardURI.User == null) {
                        forwardURI.User = sipRequest.URI.User;
                    }

                    SIPFromHeader callFromHeader = ParseFromHeaderOption(fromHeaderStr, sipRequest, username, forwardURI.Host);
                    string socket = (sendToSocket != null && sendToSocket.Trim().Length > 0) ? sendToSocket : null;

                    SIPCallDescriptor switchCallStruct = new SIPCallDescriptor(username, password, forwardURI.ToString(), callFromHeader.ToString(), forwardURI.ToString(), socket, null, null, SIPCallDirection.Out, sipRequest.Header.ContentType, sipRequest.Body, true);

                    return switchCallStruct;
                }
                else {
                    logger.Warn("Could not parse SIP URI from " + forwardURIStr + " in ParseAsteriskDialString.");
                    return null;
                }
            }
            catch (Exception excp) {
                logger.Error("Exception ParseAsteriskDialString. " + excp.Message);
                return null;
            }
        }

        /// <summary>
        /// Processes dial strings using the multi-legged format. Each leg is separated from the proceeding one by the | character and each subsequent leg
        /// will only be used if all the forwards in the preceeding one fail. Within each leg the forwards are separated by the & character.
        /// 
        /// Example: 
        /// Dial(123@provider1&provider2|123@sip.blueface.ie|provider4&456@provider5[,trace])
        /// </summary>
        private Queue<List<SIPCallDescriptor>> ParseScriptDialString(
            SIPRequest sipRequest, 
            string command, 
            HybridDictionary customHeaders,
            string customContentType,
            string customContent,
            string callersNetworkId) {
            try {
                Queue<List<SIPCallDescriptor>> callsQueue = new Queue<List<SIPCallDescriptor>>();

                string[] followonLegs = command.Split(CALLLEG_FOLLOWON_SEPARATOR);
                foreach (string followOnLeg in followonLegs) {
                    List<SIPCallDescriptor> switchCalls = new List<SIPCallDescriptor>();
                    string[] callLegs = followOnLeg.Split(CALLLEG_SIMULTANEOUS_SEPARATOR);

                    foreach (string callLeg in callLegs) {
                        if (!callLeg.IsNullOrBlank()) {
                            // Strip off the options string if present.
                            string options = null;
                            string callLegDestination = callLeg;

                            int optionsStartIndex = callLeg.IndexOf(CALLLEG_OPTIONS_START_CHAR);
                            int optionsEndIndex = callLeg.IndexOf(CALLLEG_OPTIONS_END_CHAR);
                            if (optionsStartIndex != -1) {
                                callLegDestination = callLeg.Substring(0, optionsStartIndex);
                                options = callLeg.Substring(optionsStartIndex, optionsEndIndex - optionsStartIndex);
                            }

                            // Determine whether the call forward is for a local domain or not.
                            SIPURI callLegSIPURI = SIPURI.ParseSIPURIRelaxed(DialPlanEngine.SubstituteRequestVars(sipRequest, callLegDestination));
                            if (callLegSIPURI != null && callLegSIPURI.User == null) {
                                callLegSIPURI.User = sipRequest.URI.User;
                            }

                            if (callLegSIPURI != null) {
                                string localDomain = GetCanonicalDomain_External(callLegSIPURI.Host);
                                if (localDomain != null) {
                                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call leg is for local domain looking up bindings for " + callLegSIPURI.User + "@" + localDomain + " for call leg " + callLegDestination + ".", m_username));
                                    SIPAccount sipAccount = GetSIPAccount_External(s => s.SIPUsername == callLegSIPURI.User && s.SIPDomain == localDomain);
                                    if (sipAccount != null) {
                                        switchCalls.AddRange(GetForwardsForLocalLeg(sipRequest, sipAccount, customHeaders, customContentType, customContent, callersNetworkId));
                                    }
                                    else {
                                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "No sip account could be found for local call leg " + callLeg + ".", m_username));
                                    }
                                }
                                else {
                                    // Construct a call forward for a remote destination.
                                    SIPCallDescriptor sipCallDescriptor = GetForwardsForExternalLeg(sipRequest, callLegSIPURI, customContentType, customContent);

                                    if (sipCallDescriptor != null) {
                                        // Add and provided custom headers to those already present in the call descriptor and overwrite if an existing
                                        // header is already present.
                                        if (customHeaders != null && customHeaders.Count > 0) {
                                            foreach (DictionaryEntry customHeader in customHeaders) {
                                                if (sipCallDescriptor.CustomHeaders.Contains(customHeader.Key as string)) {
                                                    sipCallDescriptor.CustomHeaders[customHeader.Key as string] = customHeader.Value as string;
                                                }
                                                else {
                                                    sipCallDescriptor.CustomHeaders.Add(customHeader.Key as string, customHeader.Value as string);
                                                }
                                            }
                                        }

                                        sipCallDescriptor.ParseCallOptions(options);
                                        switchCalls.Add(sipCallDescriptor);
                                    }
                                }
                            }
                            else {
                                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Could not parse a SIP URI from " + callLeg + " in ParseMultiDialString.", m_username));
                            }
                        }
                    }

                    callsQueue.Enqueue(switchCalls);
                }

                return callsQueue;
            }
            catch (Exception excp) {
                logger.Error("Exception ParseScriptDialString. " + excp.Message);
                throw excp;
            }
        }

        /// <summary>
        /// Creates a list of calls based on the registered contacts for a user registration.
        /// </summary>
        /// <param name="user"></param>
        /// <param name="domain"></param>
        /// <param name="from">The From header that will be set on the forwarded call leg.</param>
        /// <returns></returns>
        public List<SIPCallDescriptor> GetForwardsForLocalLeg(
            SIPRequest sipRequest, 
            SIPAccount sipAccount, 
            HybridDictionary customHeaders,
            string customContentType,
            string customContent,
            string callersNetworkId) {

            List<SIPCallDescriptor> localUserSwitchCalls = new List<SIPCallDescriptor>();

            try {
                if (sipAccount == null) {
                    throw new ApplicationException("Cannot lookup forwards for a null SIP account.");
                }

                List<SIPRegistrarBinding> bindings = GetRegistrarBindings_External(b => b.SIPAccountId == sipAccount.Id, null, 0, Int32.MaxValue);

                if (bindings != null) {
                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, bindings.Count + " found for " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + ".", m_username));

                    // Build list of registered contacts.
                    for (int index = 0; index < bindings.Count; index++) {
                        SIPRegistrarBinding binding = bindings[index];
                        SIPURI contactURI = binding.MangledContactSIPURI;

                        //logger.Debug("Creating switch call for local user " + contactURI.ToString() + ".");
                        SIPEndPoint outboundProxy = null;

                        // If the binding has a proxy socket defined that's NOT a socket on this agent then the call should be sent to the proxy for forwarding to the user agent.
                        if (binding.ProxySIPEndPoint != null) {
                            if (!m_sipTransport.IsLocalSIPEndPoint(new SIPEndPoint(contactURI))) {
                                outboundProxy = binding.ProxySIPEndPoint;
                            }
                        }

                        string outboundProxyStr = (outboundProxy != null) ? outboundProxy.ToString() : null;

                        // Determine the content based on a custom request, caller's network id and whether mangling is required.
                        string contentType = null;
                        string content = null;
                        bool mangleResponseSDP = true;

                        if(!customContentType.IsNullOrBlank()) {
                            contentType = customContentType;
                        }
                        else if(sipRequest != null) {
                            contentType = sipRequest.Header.ContentType;
                        }
                      
                        if(!customContent.IsNullOrBlank()) {
                            content = customContent;
                        }
                        else if (!callersNetworkId.IsNullOrBlank() && callersNetworkId == sipAccount.NetworkId) {
                            mangleResponseSDP = false;
                            content = sipRequest.Body;
                        }
                        else if (sipRequest != null) {
                            content = MangleContent(sipRequest.Body, sipRequest.Header.ProxyReceivedFrom, sipAccount.SIPUsername + "@" + sipAccount.SIPDomain);
                        }

                        SIPCallDescriptor switchCall = new SIPCallDescriptor(
                            null, 
                            null, 
                            contactURI.ToString(), 
                            sipRequest.Header.From.ToString(),
                            new SIPToHeader(null, SIPURI.ParseSIPURIRelaxed(sipAccount.SIPUsername + "@" + sipAccount.SIPDomain), null).ToString(), 
                            outboundProxyStr, 
                            customHeaders, 
                            null, 
                            SIPCallDirection.In, 
                            contentType, 
                            content,
                            mangleResponseSDP);
                        localUserSwitchCalls.Add(switchCall);
                    }
                }
                else {
                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "No current bindings found for local SIP account " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + ".", m_username));
                }

                return localUserSwitchCalls;
            }
            catch (Exception excp) {
                logger.Error("Exception GetForwardsForLocalLeg. " + excp);
                return localUserSwitchCalls;
            }
        }

        /// <summary>
        /// Can't be used for local destinations!
        /// </summary>
        /// <param name="sipRequest"></param>
        /// <param name="command"></param>
        /// <returns></returns>
        private SIPCallDescriptor GetForwardsForExternalLeg(
            SIPRequest sipRequest, 
            SIPURI callLegURI,
            string customContentType,
            string customContent) {

            try {
                SIPCallDescriptor SIPCallDescriptor = null;

                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Attempting to locate a provider for call leg: " + callLegURI.ToString() + ".", m_username));
                bool providerFound = false;

                string contentType = null;
                string content = null;

                if (!customContentType.IsNullOrBlank()) {
                    contentType = customContentType;
                }
                else if (sipRequest != null) {
                    contentType = sipRequest.Header.ContentType;
                }

                if (!customContent.IsNullOrBlank()) {
                    content = customContent;
                }
                else if (sipRequest != null) {
                    content = MangleContent(sipRequest.Body, sipRequest.Header.ProxyReceivedFrom, callLegURI.ToString());
                }

                if (m_sipProviders != null) {
                    foreach (SIPProvider provider in m_sipProviders) {
                        if (callLegURI.Host.ToUpper() == provider.ProviderName.ToUpper()) {
                            SIPURI providerURI = SIPURI.ParseSIPURI(provider.ProviderServer);
                            if (providerURI != null) {
                                providerURI.User = callLegURI.User;

                                if (callLegURI.Parameters.Count > 0) {
                                    foreach (string parameterName in callLegURI.Parameters.GetKeys()) {
                                        if (!providerURI.Parameters.Has(parameterName)) {
                                            providerURI.Parameters.Set(parameterName, callLegURI.Parameters.Get(parameterName));
                                        }
                                    }
                                }

                                if (callLegURI.Headers.Count > 0) {
                                    foreach (string headerName in callLegURI.Headers.GetKeys()) {
                                        if (!providerURI.Headers.Has(headerName)) {
                                            providerURI.Headers.Set(headerName, callLegURI.Headers.Get(headerName));
                                        }
                                    }
                                }

                                SIPFromHeader fromHeader = ParseFromHeaderOption(provider.ProviderFrom, sipRequest, provider.ProviderUsername, providerURI.Host);

                                SIPCallDescriptor = new SIPCallDescriptor(
                                    provider.ProviderUsername,
                                    provider.ProviderPassword,
                                    providerURI.ToString(),
                                    fromHeader.ToString(),
                                    providerURI.ToString(),
                                    (provider.ProviderOutboundProxy != null && provider.ProviderOutboundProxy.Trim().Length > 0) ? provider.ProviderOutboundProxy.Trim() : null,
                                    SIPCallDescriptor.ParseCustomHeaders(provider.CustomHeaders),
                                    provider.ProviderAuthUsername,
                                    SIPCallDirection.Out,
                                    contentType,
                                    content,
                                    true);

                                providerFound = true;
                                break;
                            }
                            else {
                                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Could not parse SIP URI from Provider Server " + provider.ProviderServer + " in GetForwardsForExternalLeg.", m_username));
                            }
                        }
                    }
                }

                if (!providerFound) {
                    // Treat as an anonymous SIP URI.

                    // Copy the From header so the tag can be removed before adding to the forwarded request.
                    SIPFromHeader forwardedFromHeader = SIPFromHeader.ParseFromHeader(sipRequest.Header.From.ToString());
                    forwardedFromHeader.FromTag = null;

                    SIPCallDescriptor = new SIPCallDescriptor(
                        m_anonymousUsername,
                        null,
                        callLegURI.ToString(),
                        forwardedFromHeader.ToString(),
                        callLegURI.ToString(),
                        null,
                        null,
                        null,
                        SIPCallDirection.Out,
                        contentType,
                        content,
                        true);
                }

                return SIPCallDescriptor;
            }
            catch (Exception excp) {
                logger.Error("Exception GetForwardsForExternalLeg. " + excp.Message);
                return null;
            }
        }

        /// <summary>
        /// The From header on forwarded calls can be customised. This method parses the dial plan option for
        /// the From header field or lack of it field and produces the From header string that will be used for
        /// forwarded calls.
        /// </summary>
        /// <param name="fromHeaderOption"></param>
        /// <returns></returns>
        private SIPFromHeader ParseFromHeaderOption(string fromHeaderOption, SIPRequest sipRequest, string username, string forwardURIHost)
        {
            SIPFromHeader fromHeader = null;

            if (fromHeaderOption != null && fromHeaderOption.Trim().Length > 0)
            {
                SIPFromHeader dialplanFrom = SIPFromHeader.ParseFromHeader(fromHeaderOption);
                fromHeader = SIPFromHeader.ParseFromHeader(DialPlanEngine.SubstituteRequestVars(sipRequest, fromHeaderOption));
            }
            else if (Regex.Match(username, ANON_CALLERS).Success)
            {
                fromHeader = SIPFromHeader.ParseFromHeader("sip:" + username + "@" + sipRequest.Header.From.FromURI.Host);
            }
            else
            {
                fromHeader = SIPFromHeader.ParseFromHeader("sip:" + username + "@" + forwardURIHost);
            }

            return fromHeader;
        }

        private string MangleContent(string body, string proxyReceivedFromHeader, string legDestination) {
            try {
                string content = null;
                bool wasSDPMangled = false;
                IPEndPoint sdpEndPoint = (!body.IsNullOrBlank()) ? SDP.GetSDPRTPEndPoint(body) : null;
                //Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "MangleContent, SDP=" + sdpEndPoint.Address.ToString() + ", Proxy-ReceivedFrom=" + proxyReceivedFromHeader + ".", m_username));
                IPAddress remoteUACAddress = null;
                
                if (!body.IsNullOrBlank() && !proxyReceivedFromHeader.IsNullOrBlank()) {
                    // Caller and callee are not on the same network id so an attempt will be made to mangle any private IP addresses found
                    // in the SDP.
                    //logger.Debug("Mangling SDP for call leg " + legDestination + ".");
                    remoteUACAddress = SIPEndPoint.ParseSIPEndPoint(proxyReceivedFromHeader).SocketEndPoint.Address;
                    content = SIPPacketMangler.MangleSDP(body, remoteUACAddress.ToString(), out wasSDPMangled);
                }
                else {
                    //logger.Debug("Not mangling SDP for call leg " + legDestination + ".");
                    content = body;
                }

                if (wasSDPMangled) {
                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP for call to " + legDestination + " had address mangled from " + sdpEndPoint.Address.ToString() + " to " + remoteUACAddress.ToString() + ".", m_username));
                }
                else if (sdpEndPoint != null) {
                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP for call to " + legDestination + " using address of " + sdpEndPoint.Address.ToString() + ".", m_username));
                }
                else {
                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Could not determine the IP address for SDP request for call to " + legDestination + ".", m_username));
                }

                return content;
            }
            catch (Exception excp) {
                logger.Error("Exception MangleContent. " + excp.Message);
                return body;
            }
        }

        #region Unit testing.

        #if UNITTEST

		[TestFixture]
		public class DialStringParserUnitTest
		{			
			[TestFixtureSetUp]
			public void Init()
			{ }

            [TestFixtureTearDown]
            public void Dispose()
            { }

			[Test]
			public void SampleTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				Assert.IsTrue(true, "True was false.");

				Console.WriteLine("---------------------------------"); 
			}

            [Test]
            public void SingleProviderLegUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPRequest inviteRequest = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:1234@localhost"));
                SIPHeader inviteHeader = new SIPHeader(SIPFromHeader.ParseFromHeader("<sip:joe@localhost>"), SIPToHeader.ParseToHeader("<sip:jane@localhost>"), 23, CallProperties.CreateNewCallId());
                SIPViaHeader viaHeader = new SIPViaHeader("127.0.0.1", 5060, CallProperties.CreateBranchId());
                inviteHeader.Vias.PushViaHeader(viaHeader);
                inviteRequest.Header = inviteHeader;

                List<SIPProvider> providers = new List<SIPProvider>();
                SIPProvider provider = new SIPProvider("test", "blueface", "test", "password", SIPURI.ParseSIPURIRelaxed("sip.blueface.ie"), null, null, null, null, 3600, null, null, null, false, false);
                providers.Add(provider);

                DialStringParser dialStringParser = new DialStringParser(null, "test", providers, delegate {return null;}, null, (host) => { return null; });
                Queue<List<SIPCallDescriptor>> callQueue = dialStringParser.ParseDialString(DialPlanContextsEnum.Script, inviteRequest, "blueface", null);

                Assert.IsNotNull(callQueue, "The call list should have contained a call.");
                Assert.IsTrue(callQueue.Count == 1, "The call queue list should have contained one leg.");

                List<SIPCallDescriptor> firstLeg = callQueue.Dequeue();

                Assert.IsNotNull(firstLeg, "The first call leg should exist.");
                Assert.IsTrue(firstLeg.Count == 1, "The first call leg should have had one switch call.");
                Assert.IsTrue(firstLeg[0].Username == "test", "The username for the first call leg was not correct.");
                Assert.IsTrue(firstLeg[0].Uri.ToString() == "sip:1234@sip.blueface.ie", "The destination URI for the first call leg was not correct.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void SingleProviderWithDstLegUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPRequest inviteRequest = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:1234@localhost"));
                SIPHeader inviteHeader = new SIPHeader(SIPFromHeader.ParseFromHeader("<sip:joe@localhost>"), SIPToHeader.ParseToHeader("<sip:jane@localhost>"), 23, CallProperties.CreateNewCallId());
                SIPViaHeader viaHeader = new SIPViaHeader("127.0.0.1", 5060, CallProperties.CreateBranchId());
                inviteHeader.Vias.PushViaHeader(viaHeader);
                inviteRequest.Header = inviteHeader;

                List<SIPProvider> providers = new List<SIPProvider>();
                SIPProvider provider = new SIPProvider("test", "blueface", "test", "password", SIPURI.ParseSIPURIRelaxed("sip.blueface.ie"), null, null, null, null, 3600, null, null, null, false, false);
                providers.Add(provider);

                DialStringParser dialStringParser = new DialStringParser(null, "test", providers, delegate { return null; }, null, (host) => { return null; });
                Queue<List<SIPCallDescriptor>> callQueue = dialStringParser.ParseDialString(DialPlanContextsEnum.Script, inviteRequest, "303@blueface", null);

                Assert.IsNotNull(callQueue, "The call list should have contained a call.");
                Assert.IsTrue(callQueue.Count == 1, "The call queue list should have contained one leg.");

                List<SIPCallDescriptor> firstLeg = callQueue.Dequeue();

                Assert.IsNotNull(firstLeg, "The first call leg should exist.");
                Assert.IsTrue(firstLeg.Count == 1, "The first call leg should have had one switch call.");
                Assert.IsTrue(firstLeg[0].Username == "test", "The username for the first call leg was not correct.");
                Assert.IsTrue(firstLeg[0].Uri.ToString() == "sip:303@sip.blueface.ie", "The destination URI for the first call leg was not correct.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void NoMatchingProviderUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPRequest inviteRequest = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:1234@localhost"));
                SIPHeader inviteHeader = new SIPHeader(SIPFromHeader.ParseFromHeader("<sip:joe@localhost>"), SIPToHeader.ParseToHeader("<sip:jane@localhost>"), 23, CallProperties.CreateNewCallId());
                SIPViaHeader viaHeader = new SIPViaHeader("127.0.0.1", 5060, CallProperties.CreateBranchId());
                inviteHeader.Vias.PushViaHeader(viaHeader);
                inviteRequest.Header = inviteHeader;

                List<SIPProvider> providers = new List<SIPProvider>();
                SIPProvider provider = new SIPProvider("test", "blueface", "test", "password", SIPURI.ParseSIPURIRelaxed("sip.blueface.ie"), null, null, null, null, 3600, null, null, null, false, false);
                providers.Add(provider);

                DialStringParser dialStringParser = new DialStringParser(null, "test", providers, delegate { return null; }, null, (host) => { return null; });
                Queue<List<SIPCallDescriptor>> callQueue = dialStringParser.ParseDialString(DialPlanContextsEnum.Script, inviteRequest, "303@noprovider", null);

                Assert.IsNotNull(callQueue, "The call list should be returned.");
                Assert.IsTrue(callQueue.Count == 1, "The call queue list should not have contained one leg.");
                List<SIPCallDescriptor> firstLeg = callQueue.Dequeue();

                Assert.IsNotNull(firstLeg, "The first call leg should exist.");
                Assert.IsTrue(firstLeg.Count == 1, "The first call leg should have had one switch call.");
                Assert.IsTrue(firstLeg[0].Username == DialStringParser.m_anonymousUsername, "The username for the first call leg was not correct.");
                Assert.IsTrue(firstLeg[0].Uri.ToString() == "sip:303@noprovider", "The destination URI for the first call leg was not correct.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void LookupSIPAccountUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                DialStringParser dialStringParser = new DialStringParser(null, "test", null, (where) => { return null; }, (where, offset, count, orderby) => { return null; }, (host) => { return host; });
                Queue<List<SIPCallDescriptor>> callList = dialStringParser.ParseDialString(DialPlanContextsEnum.Script, null, "aaron@local", null);

                Assert.IsTrue(callList.Dequeue().Count == 0, "No local contacts should have been returned.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void MultipleForwardsUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPRequest inviteRequest = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:1234@localhost"));
                SIPHeader inviteHeader = new SIPHeader(SIPFromHeader.ParseFromHeader("<sip:joe@localhost>"), SIPToHeader.ParseToHeader("<sip:jane@localhost>"), 23, CallProperties.CreateNewCallId());
                SIPViaHeader viaHeader = new SIPViaHeader("127.0.0.1", 5060, CallProperties.CreateBranchId());
                inviteHeader.Vias.PushViaHeader(viaHeader);
                inviteRequest.Header = inviteHeader;

                List<SIPProvider> providers = new List<SIPProvider>();
                SIPProvider provider = new SIPProvider("test", "provider1", "user", "password", SIPURI.ParseSIPURIRelaxed("sip.blueface.ie"), null, null, null, null, 3600, null, null, null, false, false);
                SIPProvider provider2 = new SIPProvider("test", "provider2", "user", "password", SIPURI.ParseSIPURIRelaxed("sip.blueface.ie"), null, null, null, null, 3600, null, null, null, false, false);
                providers.Add(provider);
                providers.Add(provider2);

                DialStringParser dialStringParser = new DialStringParser(null, "test", providers, (where) => { return null; }, (where, offset, count, orderby) => { return null; }, (host) => { return null; });
                Queue<List<SIPCallDescriptor>> callQueue = dialStringParser.ParseDialString(DialPlanContextsEnum.Script, inviteRequest, "provider1&provider2", null);

                Assert.IsNotNull(callQueue, "The call list should have contained a call.");
                Assert.IsTrue(callQueue.Count == 1, "The call queue list should have contained one leg.");

                List<SIPCallDescriptor> firstLeg = callQueue.Dequeue();

                Assert.IsNotNull(firstLeg, "The first call leg should exist.");
                Assert.IsTrue(firstLeg.Count == 2, "The first call leg should have had two switch calls.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void MultipleForwardsWithLocalUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPRequest inviteRequest = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:1234@localhost"));
                SIPHeader inviteHeader = new SIPHeader(SIPFromHeader.ParseFromHeader("<sip:joe@localhost>"), SIPToHeader.ParseToHeader("<sip:jane@localhost>"), 23, CallProperties.CreateNewCallId());
                SIPViaHeader viaHeader = new SIPViaHeader("127.0.0.1", 5060, CallProperties.CreateBranchId());
                inviteHeader.Vias.PushViaHeader(viaHeader);
                inviteRequest.Header = inviteHeader;

                List<SIPProvider> providers = new List<SIPProvider>();
                SIPProvider provider = new SIPProvider("test", "provider1", "user", "password", SIPURI.ParseSIPURIRelaxed("sip.blueface.ie"), null, null, null, null, 3600, null, null, null, false, false);
                SIPProvider provider2 = new SIPProvider("test", "provider2", "user", "password", SIPURI.ParseSIPURIRelaxed("sip.blueface.ie"), null, null, null, null, 3600, null, null, null, false, false);
                providers.Add(provider);
                providers.Add(provider2);

                DialStringParser dialStringParser = new DialStringParser(null, "test", providers, (where) => { return null; }, (where, offset, count, orderby) => { return null; }, (host) => { return null; });
                Queue<List<SIPCallDescriptor>> callQueue = dialStringParser.ParseDialString(DialPlanContextsEnum.Script, inviteRequest, "local&1234@provider2", null);

                Assert.IsNotNull(callQueue, "The call list should have contained a call.");
                Assert.IsTrue(callQueue.Count == 1, "The call queue list should have contained one leg.");

                List<SIPCallDescriptor> firstLeg = callQueue.Dequeue();

                Assert.IsNotNull(firstLeg, "The first call leg should exist.");
                Assert.IsTrue(firstLeg.Count == 2, "The first call leg should have had two switch calls.");

                Console.WriteLine("First destination uri=" + firstLeg[0].Uri.ToString());
                Console.WriteLine("Second destination uri=" + firstLeg[1].Uri.ToString());

                Console.WriteLine("---------------------------------");
            }
        }

        #endif

        #endregion
    }
}
