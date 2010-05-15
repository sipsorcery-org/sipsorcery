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
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SIPSorcery.Net;
using SIPSorcery.Persistence;
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
        private static string m_anonymousFromURI = SIPConstants.SIP_DEFAULT_FROMURI;
        private static readonly string m_switchboardUserAgentPrefix = SIPConstants.SWITCHBOARD_USER_AGENT_PREFIX;

        private string m_username;
        private SIPAccount m_sipAccount;
        private List<SIPProvider> m_sipProviders;
        private SIPMonitorLogDelegate Log_External;
        private SIPAssetGetDelegate<SIPAccount> GetSIPAccount_External;
        private SIPAssetGetListDelegate<SIPRegistrarBinding> GetRegistrarBindings_External;
        private GetCanonicalDomainDelegate GetCanonicalDomain_External;
        private SIPTransport m_sipTransport;
        private string m_dialPlanName;

        public static IPAddress PublicIPAddress;    // If the app server is behind a NAT then it can set this address to be used in mangled SDP.

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sipTransport"></param>
        /// <param name="owner"></param>
        /// <param name="sipAccount">The SIP account that was called and resulted in the dialplan executing. Will be null if the
        /// execution was not initated by a call to a SIP account call such as if a web callback occurs.</param>
        /// <param name="sipProviders"></param>
        /// <param name="getSIPAccount"></param>
        /// <param name="getRegistrarBindings"></param>
        /// <param name="getCanonicalDomainDelegate"></param>
        /// <param name="logDelegate"></param>
        /// <param name="dialplanName">Used to ensure that a call leg cannot create a B2B call that loops into the same dial plan.</param>
        public DialStringParser(
            SIPTransport sipTransport,
            string owner,
            SIPAccount sipAccount,
            List<SIPProvider> sipProviders,
            SIPAssetGetDelegate<SIPAccount> getSIPAccount,
            SIPAssetGetListDelegate<SIPRegistrarBinding> getRegistrarBindings,
            GetCanonicalDomainDelegate getCanonicalDomainDelegate,
            SIPMonitorLogDelegate logDelegate,
            string dialplanName)
        {
            m_sipTransport = sipTransport;
            m_username = owner;
            m_sipAccount = sipAccount;
            m_sipProviders = sipProviders;
            GetSIPAccount_External = getSIPAccount;
            GetRegistrarBindings_External = getRegistrarBindings;
            GetCanonicalDomain_External = getCanonicalDomainDelegate;
            Log_External = logDelegate;
            m_dialPlanName = dialplanName;
        }

        /// <summary>
        /// Parses a dial string that has been used in a dial plan Dial command. The format of the dial string is likely to continue to evolve, check the class
        /// summary for the different formats available. This method determines which format the dial string is in and passes off to the appropriate method to 
        /// build the call list.
        /// </summary>
        /// <param name="dialPlanType">The type of dialplan that generated the Dial command.</param>
        /// <param name="sipRequest">The SIP Request that originated this command. Can be null if the call was not initiated by a SIP request such as 
        /// by a HTTP web service.</param>
        /// <param name="command">The Dial command string being parsed.</param>
        /// <param name="customHeaders">If non-empty contains a list of custom SIP headers that will be added to the forwarded request.</param>
        /// <param name="customContentType">If set indicates a custom content type header is required on the forwarded request and it
        /// overrides any other value.</param>
        /// <param name="customContent">If set indicates a custom body is required on the forwarded request and it
        /// overrides any other value.</param>
        /// <param name="callersNetworkId">If the call originated from a locally administered account this will hold the account's
        /// networkid which is used to determine if two local accounts are on the same network and therefore should have their SDP
        /// left alone.</param>
        /// <param name="fromDisplayName">If set will be used the From header display name instead of the value from the originating SIP request.</param>
        /// <param name="fromUsername">If set will be used the From header user name instead of the value from the originating SIP request.</param>
        /// <param name="fromHost">If set will be used the From header host instead of the value from the originating SIP request.</param>
        /// <returns>A queue where each item is a list of calls. If there was only a single forward there would only be one item in the list which contained a 
        /// single call. For dial strings containing multiple forwards each queue item can be a list with multiple calls.</returns>
        public Queue<List<SIPCallDescriptor>> ParseDialString(
            DialPlanContextsEnum dialPlanType,
            SIPRequest sipRequest,
            string command,
            List<string> customHeaders,
            string customContentType,
            string customContent,
            string callersNetworkId,
            string fromDisplayName,
            string fromUsername,
            string fromHost,
            List<string> switchboardHeaders)
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

                    if (dialPlanType == DialPlanContextsEnum.Line || (!command.Contains("[") && Regex.Match(command, @"\S+,.*,\S+").Success))
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
                        prioritisedCallList = ParseScriptDialString(sipRequest, command, customHeaders, customContentType, customContent, callersNetworkId, fromDisplayName, fromUsername, fromHost, switchboardHeaders);
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
        private SIPCallDescriptor ParseAsteriskDialString(string data, SIPRequest sipRequest)
        {
            try
            {
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

                if (dataFields.Length > 3 && dataFields[3] != null)
                {
                    fromHeaderStr = dataFields[3].Trim();
                }

                if (dataFields.Length > 4 && dataFields[4] != null)
                {
                    sendToSocket = dataFields[4].Trim().Trim(new char[] { '"', '\'' });
                }

                if (dataFields.Length > 5 && dataFields[5] != null)
                {
                    Boolean.TryParse(dataFields[5].Trim(), out traceReqd);
                }

                forwardURI = SIPURI.ParseSIPURIRelaxed(SubstituteRequestVars(sipRequest, forwardURIStr));
                if (forwardURI != null)
                {
                    if (forwardURI.User == null)
                    {
                        forwardURI.User = sipRequest.URI.User;
                    }

                    SIPFromHeader callFromHeader = ParseFromHeaderOption(fromHeaderStr, sipRequest, username, forwardURI.Host);
                    string socket = (sendToSocket != null && sendToSocket.Trim().Length > 0) ? sendToSocket : null;
                    string content = sipRequest.Body;
                    string remoteUAStr = sipRequest.Header.ProxyReceivedFrom;
                    SIPEndPoint remoteUAAddress = (!remoteUAStr.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(remoteUAStr) : sipRequest.RemoteSIPEndPoint;
                    SIPCallDescriptor switchCallStruct = new SIPCallDescriptor(username, password, forwardURI.ToString(), callFromHeader.ToString(), forwardURI.ToString(), socket, null, null, SIPCallDirection.Out, sipRequest.Header.ContentType, content, remoteUAAddress.Address);

                    return switchCallStruct;
                }
                else
                {
                    logger.Warn("Could not parse SIP URI from " + forwardURIStr + " in ParseAsteriskDialString.");
                    return null;
                }
            }
            catch (Exception excp)
            {
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
            List<string> customHeaders,
            string customContentType,
            string customContent,
            string callersNetworkId,
            string fromDisplayName,
            string fromUsername,
            string fromHost,
            List<string> switchboardHeaders)
        {
            try
            {
                Queue<List<SIPCallDescriptor>> callsQueue = new Queue<List<SIPCallDescriptor>>();
                string[] followonLegs = command.Split(CALLLEG_FOLLOWON_SEPARATOR);

                foreach (string followOnLeg in followonLegs)
                {
                    List<SIPCallDescriptor> switchCalls = new List<SIPCallDescriptor>();
                    string[] callLegs = followOnLeg.Split(CALLLEG_SIMULTANEOUS_SEPARATOR);

                    foreach (string callLeg in callLegs)
                    {
                        if (!callLeg.IsNullOrBlank())
                        {
                            // Strip off the options string if present.
                            string options = null;
                            string callLegDestination = callLeg;

                            int optionsStartIndex = callLeg.IndexOf(CALLLEG_OPTIONS_START_CHAR);
                            int optionsEndIndex = callLeg.IndexOf(CALLLEG_OPTIONS_END_CHAR);
                            if (optionsStartIndex != -1)
                            {
                                callLegDestination = callLeg.Substring(0, optionsStartIndex);
                                options = callLeg.Substring(optionsStartIndex, optionsEndIndex - optionsStartIndex);
                            }

                            // Determine whether the call forward is for a local domain or not.
                            SIPURI callLegSIPURI = SIPURI.ParseSIPURIRelaxed(SubstituteRequestVars(sipRequest, callLegDestination));
                            if (callLegSIPURI != null && callLegSIPURI.User == null)
                            {
                                callLegSIPURI.User = sipRequest.URI.User;
                            }

                            if (callLegSIPURI != null)
                            {
                                string localDomain = GetCanonicalDomain_External(callLegSIPURI.Host, false);
                                if (localDomain != null)
                                {
                                    SIPAccount calledSIPAccount = GetSIPAccount_External(s => s.SIPUsername == callLegSIPURI.User && s.SIPDomain == localDomain);
                                    if (calledSIPAccount == null && callLegSIPURI.User.Contains("."))
                                    {
                                        // A full lookup failed. Now try a partial lookup if the incoming username is in a dotted domain name format.
                                        string sipUsernameSuffix = callLegSIPURI.User.Substring(callLegSIPURI.User.LastIndexOf(".") + 1);
                                        calledSIPAccount = GetSIPAccount_External(s => s.SIPUsername == sipUsernameSuffix && s.SIPDomain == localDomain);
                                    }
                                    if (calledSIPAccount != null)
                                    {
                                        // An incoming dialplan won't be used if it's invoked from itself.
                                        if (calledSIPAccount.InDialPlanName.IsNullOrBlank() || (m_username == calledSIPAccount.Owner && m_dialPlanName == calledSIPAccount.InDialPlanName))
                                        {
                                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call leg is for local domain looking up bindings for " + callLegSIPURI.User + "@" + localDomain + " for call leg " + callLegDestination + ".", m_username));
                                            switchCalls.AddRange(GetForwardsForLocalLeg(sipRequest, calledSIPAccount, customHeaders, customContentType, customContent, options, callersNetworkId, fromDisplayName, fromUsername, fromHost, switchboardHeaders));
                                        }
                                        else
                                        {
                                            // An incoming call for a SIP account that has an incoming dialplan specified.
                                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call leg is for local domain forwarding to incoming dialplan for " + callLegSIPURI.User + "@" + localDomain + ".", m_username));
                                            string sipUsername = (m_sipAccount != null) ? m_sipAccount.SIPUsername : m_username;
                                            string sipDomain = (m_sipAccount != null) ? m_sipAccount.SIPDomain : GetCanonicalDomain_External(SIPDomainManager.DEFAULT_LOCAL_DOMAIN, false);
                                            SIPFromHeader fromHeader = ParseFromHeaderOption(null, sipRequest, sipUsername, sipDomain);

                                            string content = customContent;
                                            string contentType = customContentType;
                                            if (contentType.IsNullOrBlank())
                                            {
                                                contentType = sipRequest.Header.ContentType;
                                            }

                                            if (content.IsNullOrBlank())
                                            {
                                                content = sipRequest.Body;
                                            }

                                            SIPCallDescriptor loopbackCall = new SIPCallDescriptor(calledSIPAccount, callLegSIPURI.ToString(), fromHeader.ToString(), contentType, content);
                                            loopbackCall.SetGeneralFromHeaderFields(fromDisplayName, fromUsername, fromHost);
                                            loopbackCall.MangleIPAddress = (PublicIPAddress != null) ? PublicIPAddress : SIPPacketMangler.GetRequestIPAddress(sipRequest);
                                            loopbackCall.ParseCallOptions(options);
                                            switchCalls.Add(loopbackCall);
                                        }
                                    }
                                    else
                                    {
                                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "No sip account could be found for local call leg " + callLeg + ".", m_username));
                                    }
                                }
                                else
                                {
                                    // Construct a call forward for a remote destination.
                                    SIPCallDescriptor sipCallDescriptor = GetForwardsForExternalLeg(sipRequest, callLegSIPURI, customContentType, customContent, fromDisplayName, fromUsername, fromHost);

                                    if (sipCallDescriptor != null)
                                    {
                                        // Add and provided custom headers to those already present in the call descriptor and overwrite if an existing
                                        // header is already present.
                                        if (customHeaders != null && customHeaders.Count > 0)
                                        {
                                            foreach (string customHeader in customHeaders)
                                            {
                                                string customHeaderValue = SubstituteRequestVars(sipRequest, customHeader);
                                                sipCallDescriptor.CustomHeaders.Add(customHeaderValue);
                                            }
                                        }

                                        sipCallDescriptor.ParseCallOptions(options);
                                        switchCalls.Add(sipCallDescriptor);
                                    }
                                }
                            }
                            else
                            {
                                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Could not parse a SIP URI from " + callLeg + " in ParseMultiDialString.", m_username));
                            }
                        }
                    }

                    // Calls will not be added if the URI is already in the queue!
                    List<SIPCallDescriptor> callList = new List<SIPCallDescriptor>();
                    callsQueue.Enqueue(callList);
                    foreach (SIPCallDescriptor callToAdd in switchCalls)
                    {
                        if (!IsURIInQueue(callsQueue, callToAdd.Uri))
                        {
                            callList.Add(callToAdd);
                        }
                        else
                        {
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call leg " + callToAdd.Uri.ToString() + " already added duplicate ignored.", m_username));
                        }
                    }
                }

                return callsQueue;
            }
            catch (Exception excp)
            {
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
            List<string> customHeaders,
            string customContentType,
            string customContent,
            string options,
            string callersNetworkId,
            string fromDisplayName,
            string fromUsername,
            string fromHost,
            List<string> switchboardHeaders)
        {
            List<SIPCallDescriptor> localUserSwitchCalls = new List<SIPCallDescriptor>();

            try
            {
                if (sipAccount == null)
                {
                    throw new ApplicationException("Cannot lookup forwards for a null SIP account.");
                }

                List<SIPRegistrarBinding> bindings = GetRegistrarBindings_External(b => b.SIPAccountId == sipAccount.Id, null, 0, Int32.MaxValue);

                if (bindings != null)
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, bindings.Count + " found for " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + ".", m_username));

                    // Build list of registered contacts.
                    for (int index = 0; index < bindings.Count; index++)
                    {
                        SIPRegistrarBinding binding = bindings[index];
                        SIPURI contactURI = binding.MangledContactSIPURI;

                        // Determine the content based on a custom request, caller's network id and whether mangling is required.
                        string contentType = (sipRequest != null) ? sipRequest.Header.ContentType : null;
                        string content = (sipRequest != null) ? sipRequest.Body : null;

                        if (!customContentType.IsNullOrBlank())
                        {
                            contentType = customContentType;
                        }

                        if (!customContent.IsNullOrBlank())
                        {
                            content = customContent;
                        }

                        IPAddress publicSDPAddress = PublicIPAddress;
                        if (publicSDPAddress == null && sipRequest != null)
                        {
                            publicSDPAddress = SIPPacketMangler.GetRequestIPAddress(sipRequest);
                        }

                        string fromHeader = (sipRequest != null) ? sipRequest.Header.From.ToString() : m_anonymousFromURI;

                        // If the binding for the call is a switchboard add the custom switchboard headers.
                        List<string> customSwitchboardHeaders = null;
                        if (!binding.UserAgent.IsNullOrBlank() && binding.UserAgent.StartsWith(m_switchboardUserAgentPrefix))
                        {
                            customSwitchboardHeaders = new List<string>();

                            if (customHeaders != null && customHeaders.Count > 0)
                            {
                                customSwitchboardHeaders.AddRange(customHeaders);
                            }

                            customSwitchboardHeaders.Add(SIPHeaders.SIP_HEADER_SWITCHBOARD_CALLID + ": " + sipRequest.Header.CallId);
                            customSwitchboardHeaders.Add(SIPHeaders.SIP_HEADER_SWITCHBOARD_TO + ": " + sipRequest.Header.To.ToString());
                            customSwitchboardHeaders.Add(SIPHeaders.SIP_HEADER_SWITCHBOARD_FROM + ": " + sipRequest.Header.From.ToString());

                            if (switchboardHeaders != null && switchboardHeaders.Count > 0)
                            {
                                foreach (string switchboardHeader in switchboardHeaders)
                                {
                                    customSwitchboardHeaders.Add(switchboardHeader);
                                }
                            }
                        }

                        SIPCallDescriptor switchCall = new SIPCallDescriptor(
                            null,
                            null,
                            contactURI.ToString(),
                            fromHeader,
                            new SIPToHeader(null, SIPURI.ParseSIPURIRelaxed(sipAccount.SIPUsername + "@" + sipAccount.SIPDomain), null).ToString(),
                            null,
                            customSwitchboardHeaders ?? customHeaders,
                            null,
                            SIPCallDirection.In,
                            contentType,
                            content,
                            publicSDPAddress);
                        // If the binding has a proxy socket defined set the header to ask the upstream proxy to use it.
                        if (binding.ProxySIPEndPoint != null)
                        {
                            switchCall.ProxySendFrom = binding.ProxySIPEndPoint.ToString();
                        }
                        switchCall.ParseCallOptions(options);
                        if (sipAccount != null && !sipAccount.NetworkId.IsNullOrBlank() && sipAccount.NetworkId == callersNetworkId)
                        {
                            switchCall.MangleResponseSDP = false;
                        }
                        switchCall.SetGeneralFromHeaderFields(fromDisplayName, fromUsername, fromHost);
                        localUserSwitchCalls.Add(switchCall);
                    }
                }
                else
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "No current bindings found for local SIP account " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + ".", m_username));
                }

                return localUserSwitchCalls;
            }
            catch (Exception excp)
            {
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
            string customContent,
            string fromDisplayName,
            string fromUsername,
            string fromHost)
        {
            try
            {
                SIPCallDescriptor sipCallDescriptor = null;

                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Attempting to locate a provider for call leg: " + callLegURI.ToString() + ".", m_username));
                bool providerFound = false;

                string contentType = (sipRequest != null) ? sipRequest.Header.ContentType : null;
                string content = (sipRequest != null) ? sipRequest.Body : null;

                if (!customContentType.IsNullOrBlank())
                {
                    contentType = customContentType;
                }

                if (!customContent.IsNullOrBlank())
                {
                    content = customContent;
                }

                IPAddress publicSDPAddress = PublicIPAddress;
                if (publicSDPAddress == null && sipRequest != null)
                {
                    publicSDPAddress = SIPPacketMangler.GetRequestIPAddress(sipRequest);
                }

                if (m_sipProviders != null)
                {
                    foreach (SIPProvider provider in m_sipProviders)
                    {
                        if (callLegURI.Host.ToUpper() == provider.ProviderName.ToUpper())
                        {
                            SIPURI providerURI = SIPURI.ParseSIPURI(provider.ProviderServer);
                            if (providerURI != null)
                            {
                                providerURI.User = callLegURI.User;

                                if (callLegURI.Parameters.Count > 0)
                                {
                                    foreach (string parameterName in callLegURI.Parameters.GetKeys())
                                    {
                                        if (!providerURI.Parameters.Has(parameterName))
                                        {
                                            providerURI.Parameters.Set(parameterName, callLegURI.Parameters.Get(parameterName));
                                        }
                                    }
                                }

                                if (callLegURI.Headers.Count > 0)
                                {
                                    foreach (string headerName in callLegURI.Headers.GetKeys())
                                    {
                                        if (!providerURI.Headers.Has(headerName))
                                        {
                                            providerURI.Headers.Set(headerName, callLegURI.Headers.Get(headerName));
                                        }
                                    }
                                }

                                SIPFromHeader fromHeader = ParseFromHeaderOption(provider.ProviderFrom, sipRequest, provider.ProviderUsername, providerURI.Host);

                                sipCallDescriptor = new SIPCallDescriptor(
                                    provider.ProviderUsername,
                                    provider.ProviderPassword,
                                    providerURI.ToString(),
                                    fromHeader.ToString(),
                                    providerURI.ToString(),
                                    null,
                                    SIPCallDescriptor.ParseCustomHeaders(SubstituteRequestVars(sipRequest, provider.CustomHeaders)),
                                    provider.ProviderAuthUsername,
                                    SIPCallDirection.Out,
                                    contentType,
                                    content,
                                    publicSDPAddress);

                                if (!provider.ProviderOutboundProxy.IsNullOrBlank())
                                {
                                    sipCallDescriptor.ProxySendFrom = provider.ProviderOutboundProxy.Trim();
                                }

                                providerFound = true;

                                if (provider.ProviderFrom.IsNullOrBlank())
                                {
                                    // If there is already a custom From header set on the provider that overrides the general settings.
                                    sipCallDescriptor.SetGeneralFromHeaderFields(fromDisplayName, fromUsername, fromHost);
                                }

                                break;
                            }
                            else
                            {
                                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Could not parse SIP URI from Provider Server " + provider.ProviderServer + " in GetForwardsForExternalLeg.", m_username));
                            }
                        }
                    }
                }

                if (!providerFound)
                {
                    // Treat as an anonymous SIP URI.

                    // Copy the From header so the tag can be removed before adding to the forwarded request.
                    string fromHeaderStr = (sipRequest != null) ? sipRequest.Header.From.ToString() : m_anonymousFromURI;
                    SIPFromHeader forwardedFromHeader = SIPFromHeader.ParseFromHeader(fromHeaderStr);
                    forwardedFromHeader.FromTag = null;

                    sipCallDescriptor = new SIPCallDescriptor(
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
                        publicSDPAddress);

                    sipCallDescriptor.SetGeneralFromHeaderFields(fromDisplayName, fromUsername, fromHost);
                }

                return sipCallDescriptor;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetForwardsForExternalLeg. " + excp.Message);
                return null;
            }
        }

        /// <summary>
        /// The From header on forwarded calls can be customised. This method parses the dial plan option for
        /// the From header field or lack of it field and produces the From header string that will be used for
        /// forwarded calls.
        /// </summary>
        /// <param name="customFromHeader">A string that allows the From header on the call descriptor to be customised. It
        /// can contain special ${xyz} sequences that will extract information from the originating SIP request.</param>
        /// <param name="sipRequest">The SIP request if any that the dial string is being constructed with.</param>
        /// <param name="username">The username of the account that owns this operation.</param>
        /// <param name="forwardURIHost">Used as the default host value on the From header URI.</param>
        /// <returns></returns>
        private SIPFromHeader ParseFromHeaderOption(string customFromHeader, SIPRequest sipRequest, string username, string forwardURIHost)
        {
            SIPFromHeader fromHeader = null;

            if (!customFromHeader.IsNullOrBlank())
            {
                SIPFromHeader dialplanFrom = SIPFromHeader.ParseFromHeader(customFromHeader);

                if (sipRequest != null)
                {
                    fromHeader = SIPFromHeader.ParseFromHeader(SubstituteRequestVars(sipRequest, customFromHeader));
                }
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

        /// <summary>
        /// Substitutes the special characters with variables from the SIP request.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="substituteString"></param>
        /// <returns></returns>
        public static string SubstituteRequestVars(SIPRequest request, string substituteString)
        {
            try
            {
                if (substituteString == null || substituteString.Trim().Length == 0 || request == null)
                {
                    return substituteString;
                }
                else
                {
                    string resultString = substituteString;

                    #region Replacing ${dst} and ${exten} with request URI.

                    if (Regex.Match(substituteString, @"\$\{(dst|exten)(:|\})", RegexOptions.IgnoreCase).Success)
                    {
                        MatchCollection matches = Regex.Matches(substituteString, @"\$\{(dst|exten):\d+?\}", RegexOptions.IgnoreCase);
                        foreach (Match match in matches)
                        {
                            int trimCharCount = Convert.ToInt32(Regex.Match(match.Value, @"\$\{(dst|exten):(?<count>\d+)\}", RegexOptions.IgnoreCase).Result("${count}"));

                            if (trimCharCount < request.URI.User.Length)
                            {
                                string trimmedDst = request.URI.User.Substring(trimCharCount);
                                resultString = Regex.Replace(resultString, Regex.Escape(match.Value), trimmedDst, RegexOptions.IgnoreCase);
                            }
                            else
                            {
                                logger.Warn("A SIP destination could not be trimmed " + request.URI.User + " " + substituteString + ", original destination being used.");
                            }
                        }

                        resultString = Regex.Replace(resultString, @"\$\{(dst|exten).*?\}", request.URI.User, RegexOptions.IgnoreCase);
                    }

                    #endregion

                    #region Replacing ${fromname} with the request From header name.

                    if (request.Header.From != null && request.Header.From.FromName != null && Regex.Match(substituteString, @"\$\{fromname\}", RegexOptions.IgnoreCase).Success)
                    {
                        resultString = Regex.Replace(resultString, @"\$\{fromname\}", request.Header.From.FromName, RegexOptions.IgnoreCase);
                    }

                    #endregion

                    #region Replacing ${fromuriuser} with the request From URI user.

                    if (request.Header.From != null && request.Header.From.FromURI != null && request.Header.From.FromURI.User != null && Regex.Match(substituteString, @"\$\{fromuriuser\}", RegexOptions.IgnoreCase).Success)
                    {
                        resultString = Regex.Replace(resultString, @"\$\{fromuriuser\}", request.Header.From.FromURI.User, RegexOptions.IgnoreCase);
                    }

                    #endregion

                    /*#region Replacing ${username} with the switch command username.

                    if (Regex.Match(substituteString, @"\$\{username\}", RegexOptions.IgnoreCase).Success && request.Header.From != null)
                    {
                        resultString = Regex.Replace(resultString, @"\$\{username\}", swCommand.Username, RegexOptions.IgnoreCase);
                    }

                    #endregion*/

                    // If parts of the From header were empty replace them with an empty string.
                    resultString = Regex.Replace(resultString, @"\$\{fromname\}", "", RegexOptions.IgnoreCase);
                    resultString = Regex.Replace(resultString, @"\$\{fromuriuser\}", "", RegexOptions.IgnoreCase);

                    return resultString.Trim();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SubstituteRequestVars. " + excp.Message);
                return substituteString;
            }
        }

        private bool IsURIInQueue(Queue<List<SIPCallDescriptor>> callQueue, string callURI)
        {
            try
            {
                if (callQueue == null || callQueue.Count == 0 || callURI.IsNullOrBlank())
                {
                    return false;
                }
                else
                {
                    List<SIPCallDescriptor>[] callLegs = callQueue.ToArray();
                    if (callLegs == null || callLegs.Length == 0)
                    {
                        return false;
                    }
                    else
                    {
                        for (int index = 0; index < callLegs.Length; index++)
                        {
                            List<SIPCallDescriptor> callList = callLegs[index];
                            foreach (SIPCallDescriptor callDescriptor in callList)
                            {
                                if (callDescriptor.Uri == null)
                                {
                                    return false;
                                }
                                else if (callDescriptor.Uri.ToString() == callURI)
                                {
                                    return true;
                                }
                            }
                        }

                        return false;
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception isURIInQueue. " + excp.Message);
                return false;
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

                DialStringParser dialStringParser = new DialStringParser(null, "test", null, providers, delegate {return null;}, null, (host, wildcard) => { return null; }, null);
                Queue<List<SIPCallDescriptor>> callQueue = dialStringParser.ParseDialString(DialPlanContextsEnum.Script, inviteRequest, "blueface", null, null, null, null, null);

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

                DialStringParser dialStringParser = new DialStringParser(null, "test", null, providers, delegate { return null; }, null, (host, wildcard) => { return null; }, null);
                Queue<List<SIPCallDescriptor>> callQueue = dialStringParser.ParseDialString(DialPlanContextsEnum.Script, inviteRequest, "303@blueface", null, null, null, null, null);

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

                DialStringParser dialStringParser = new DialStringParser(null, "test", null, providers, delegate { return null; }, null, (host, wildcard) => { return null; }, null);
                Queue<List<SIPCallDescriptor>> callQueue = dialStringParser.ParseDialString(DialPlanContextsEnum.Script, inviteRequest, "303@noprovider", null, null, null, null, null);

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

                DialStringParser dialStringParser = new DialStringParser(null, "test", null,  null, (where) => { return null; }, (where, offset, count, orderby) => { return null; }, (host, wildcard) => { return host; }, null);
                Queue<List<SIPCallDescriptor>> callList = dialStringParser.ParseDialString(DialPlanContextsEnum.Script, null, "aaron@local", null, null, null, null, null);

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

                DialStringParser dialStringParser = new DialStringParser(null, "test", null, providers, (where) => { return null; }, (where, offset, count, orderby) => { return null; }, (host, wildcard) => { return null; }, null);
                Queue<List<SIPCallDescriptor>> callQueue = dialStringParser.ParseDialString(DialPlanContextsEnum.Script, inviteRequest, "provider1&provider2", null, null, null, null, null);

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

                DialStringParser dialStringParser = new DialStringParser(null, "test", null, providers, (where) => { return null; }, (where, offset, count, orderby) => { return null; }, (host, wildcard) => { return null; }, null);
                Queue<List<SIPCallDescriptor>> callQueue = dialStringParser.ParseDialString(DialPlanContextsEnum.Script, inviteRequest, "local&1234@provider2", null, null, null, null, null);

                Assert.IsNotNull(callQueue, "The call list should have contained a call.");
                Assert.IsTrue(callQueue.Count == 1, "The call queue list should have contained one leg.");

                List<SIPCallDescriptor> firstLeg = callQueue.Dequeue();

                Assert.IsNotNull(firstLeg, "The first call leg should exist.");
                Assert.IsTrue(firstLeg.Count == 2, "The first call leg should have had two switch calls.");

                Console.WriteLine("First destination uri=" + firstLeg[0].Uri.ToString());
                Console.WriteLine("Second destination uri=" + firstLeg[1].Uri.ToString());

                Console.WriteLine("---------------------------------");
            }

          [Test]
            public void SubstitueDstVarTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
               
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:380@sip.mysipswitch.com"));
                request.Header = new SIPHeader();
                string substitutedString = SubstituteRequestVars(request, "${dst}123");

                Console.Write("Substituted string=" + substitutedString + ".");
 
                Assert.IsTrue(substitutedString == "380123", "The destination was not substituted correctly.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void SubstitueEmptyFromNameTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:380@sip.mysipswitch.com"));
                request.Header = new SIPHeader();
                string substitutedString = SubstituteRequestVars(request, "${fromname} <sip:user@provider>");

                Console.Write("Substituted string=" + substitutedString + ".");

                Assert.IsTrue(substitutedString == "<sip:user@provider>", "The from header was not substituted correctly.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void SubstitueDoubleDstVarTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:380@sip.mysipswitch.com"));
                request.Header = new SIPHeader();
                string substitutedString = SubstituteRequestVars(request, "${dst}123${dst}");

                Console.Write("Substituted string=" + substitutedString + ".");

                Assert.IsTrue(substitutedString == "380123380", "The destination was not substituted correctly.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void SubstitueDoubleDstSubStrVarTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:380@sip.mysipswitch.com"));
                request.Header = new SIPHeader();
                string substitutedString = SubstituteRequestVars(request, "${dst:1}123${dst:2}");

                Console.Write("Substituted string=" + substitutedString + ".");

                Assert.IsTrue(substitutedString == "801230", "The destination was not substituted correctly.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void SubstitueMixedDstSubStrVarTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:380@sip.mysipswitch.com"));
                request.Header = new SIPHeader();
                string substitutedString = SubstituteRequestVars(request, "${dst:1}123${dst}000");

                Console.Write("Substituted string=" + substitutedString + ".");

                Assert.IsTrue(substitutedString == "80123380000", "The destination was not substituted correctly.");

                Console.WriteLine("---------------------------------");
            }
        }

#endif

        #endregion
    }
}
