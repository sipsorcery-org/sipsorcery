//-----------------------------------------------------------------------------
// Filename: SIPManagerWebService.cs
//
// Description: Web service interface into the transient data structures such as calls in progress, registrations,
// bindings etc. The provisioning web service deals with persistent structures such as updating SIP accounts and
// extensions whereas this one deals with the non-persistent structures as well as exposing methods to initiate
// actions by the server such as sending a SIP notification or hanging up a call.
// 
// History:
// 22 Nov 2006	Aaron Clauson	    Created.
// 24 Apr 2007  Guillaume Bonnet    Edited : switchboard functions: call display, hold/resume, transfer/forward
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
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Description;
using System.ServiceModel.Channels;
//using System.ServiceModel.Web;
using System.Text;
using System.Data;
using System.IO;
using System.Text.RegularExpressions;
using System.Timers;
using System.Threading;
//using System.Web.Services;
using System.Xml;
using BlueFace.Sys;
using BlueFace.Sys.WebServices;
using BlueFace.Sys.Net;
using BlueFace.VoIP.App;
using BlueFace.VoIP.App.SIP;
using BlueFace.VoIP.Net;
using BlueFace.VoIP.Net.SIP;
using BlueFace.VoIP.SIPServer;
using BlueFace.VoIP.SIPServerCores.StatefulProxy.DialPlanApps;
using BlueFace.VoIP.SIP.StatefulProxy;
using log4net;

namespace SIPSorcery.WebServices
{   
    [ServiceContract(Namespace = "http://www.sipsorcery.com")]
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
    public class SIPManagerWebService
    {
        private const string SERVER_STRING = "sip.sipsorcery.com";
        
        private static string m_CRLF = SIPConstants.CRLF;
        
        private ILog logger = log4net.LogManager.GetLogger("manager");

        public ProxyLogDelegate StatefulProxyLogEvent;
        public AuthenticateWebServiceDelegate AuthenticateWebService_External;
        public AuthenticateTokenDelegate AuthenticateToken_External;

        private SIPRegistrations m_registrationsStore;
        public SIPRegistrations RegistrationsStore
        {
            set { m_registrationsStore = value; }
        }

        private SIPRegistrationAgent m_regAgent;
        public SIPRegistrationAgent SIPRegistrationAgent
        {
            set { m_regAgent = value; }
        }

        private SIPTransport m_sipTransport;
        public SIPTransport SIPTransport
        {
            set { m_sipTransport = value; }
        }

        [OperationContract]
        public bool IsAlive()
        {
            string authId = OperationContext.Current.IncomingMessageHeaders.GetHeader<string>("authid", "");
            logger.Debug("SIPManagerWebService IsAlive (authid=" + authId + ")");

            return true;
        }

        /// <summary>
        /// Gets the record that the SIP Registrar holds for this SIP account. Note that this method should only
        /// be used for single server deployments such as small deployments using a XML persistence approach. For
        /// larger deployments the SIP Registrar records should be persisted to a database and read from there.
        /// </summary>
        /// <param name="username">The username of the SIP account to retrieve the record for.</param>
        /// <param name="domain">The domain of the SIP account to retrieve the record for</param>
        /// <returns>The record held by the SIP Registrar for the SIP account or null if it does not have one.</returns>
        [OperationContract]
        public SIPRegistrarRecord GetRegistrarRecord(string username, string domain)
        {
            try
            {
                logger.Warn("GetRegistrarRecord called for " + username + "@" + domain + ".");

                SIPParameterlessURI addressOfRecord = new SIPParameterlessURI(SIPSchemesEnum.sip, domain, username);

                if (addressOfRecord != null)
                {
                    SIPRegistrarRecord registrarRecord = m_registrationsStore.Get(addressOfRecord);

                    if (registrarRecord != null)
                    {
                        logger.Debug(registrarRecord.Bindings.Count + " bindings found for " + username + "@" + domain + ".");
                        return registrarRecord;
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    logger.Warn("GetRegistrarRecord was called with an empty address-of-record.");
                    return null;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetRegistrarRecord. " + excp.Message);
                throw excp;
            }
        }

        /// <summary>
        /// Gets a list of all the SIP Registrar records for the specified owner, an owner can have multiple SIP accounts.
        /// Note that this method should only be used for single server deployments such as small deployments using a XML 
        /// persistence approach. For larger deployments the SIP Registrar records should be persisted to a database and 
        /// read from there.
        /// </summary>
        /// <param name="owner">The name of the owner to retrieve the SIP Registrar records for.</param>
        /// <returns>The list of records held by the SIP Registrar for the owner's SIP accounts or null if there are none.</returns>
        [OperationContract]
        public List<SIPRegistrarRecord> GetRegistrarRecords(string owner)
        {
            try
            {
                if (owner != null && owner.Trim().Length > 0)
                {
                    return m_registrationsStore.Get(owner);
                }
                else
                {
                    logger.Warn("GetRegistrarRecords was called with an empty owner.");
                    return null;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetRegistrarRecords. " + excp.Message);
                throw excp;
            }
        }

        /// <summary>
        /// Returns an XML wrapped list of the calls that are currently in progress for the specified user.
        /// </summary>
        [OperationContract]
        public XMLServiceResultStruct GetActiveCalls(string username)
        {
            XMLServiceResultStruct opResult = new XMLServiceResultStruct(true, "Sorry an unknown error has occurred processing your request. Please try reloading the page and if the error persists contact support@blueface.ie.", null);
            
            /*try
            {
                List<SIPCallDescriptor> userCalls = SIPCallDescriptor.GetCurrentCalls(username);

                if (userCalls != null && userCalls.Count > 0)
                {
                    string callURIs = "<callsinprogress>";

                    foreach (SIPCallDescriptor userCall in userCalls)
                    {
                        // Timer update
                        SIPCallDescriptor call = SIPCallDescriptor.GetCall(username, userCall.SwitchCallId);    // Retreive call information

                        string arrow = (call.SwitchedCall.IsOutgoingCall == true) ? "rightarrow.png" : "leftarrow.png";

                        string callStatus = (call.m_callInProgress == true) ? "In Progress" : "Active";

                        callURIs +=
                            " <call>" +
                            "  <switchcallid>" + userCall.SwitchCallId + "</switchcallid>" + 
                            "  <clienturi>" + userCall.ClientTransaction.TransactionRequestURI.CanonicalAddress + "</clienturi>" +
                            "  <clientfromname>" + userCall.ClientTransaction.TransactionRequestFrom.Name + "</clientfromname>" +
                            "  <clientfromuri>" + userCall.ClientTransaction.TransactionRequestFrom.URI.CanonicalAddress + "</clientfromuri>" +
                            "  <clientsocket>" + userCall.ClientTransaction.RemoteEndPoint + "</clientsocket>" +
                            "  <arrow>" + arrow + "</arrow>" +
                            "  <callStatus>" + callStatus + "</callStatus>" +
                            "  <serveruri>" + SIPURI.ParseSIPURI(userCall.SwitchedCall.Uri).CanonicalAddress + "</serveruri>" +
                            "  <serverfromname>" + userCall.ServerTransaction.TransactionRequestFrom.Name + "</serverfromname>" +
                            "  <serverfromuri>" + userCall.ServerTransaction.TransactionRequestFrom.URI.CanonicalAddress + "</serverfromuri>" +
                            "  <serversocket>" + userCall.ServerEndPoint + "</serversocket>" +
                            "  <duration>0</duration>" +
                            " </call>"; 
                    } // end foreach
                    callURIs += "</callsinprogress>";

                    string resultMessage = null;
                    if (userCalls.Count == 1)
                    {
                        resultMessage = "1 active call found for " + username + ".";
                    }
                    else
                    {
                        resultMessage = userCalls.Count + " calls found for " + username + ".";
                    }

                    opResult = new XMLServiceResultStruct(false, resultMessage, callURIs);
                }
                else
                {
                    opResult = new XMLServiceResultStruct(false, "No active calls found for " + username + ".", "<callsinprogress/>");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetActiveCalls. " + excp.Message);
                opResult.Message = "Exception. " + excp.Message;
            }*/

            return opResult;
        }
        
        [OperationContract]
        public List<UserRegistration> GetRegistrationAgentRegistrations(string owner)
        {
            try
            {
                return m_regAgent.GetOwnerRegistrations(owner);
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetRegistrationAgentRegistrations. " + excp.Message);
                throw excp;
            }
        }

        [OperationContract]
        public void RemoveRegistrationAgentContact(Guid registrationId, string contactToRemove)
        {
            try
            {
                logger.Debug("RemoveRegistrarContact webservice called for " + registrationId + " and " + contactToRemove + ".");

                UserRegistration registration = m_regAgent.Get(registrationId);

                if (registration != null)
                {
                    // Look through the current contacts to try and find one to match this contact.
                    foreach (SIPContactHeader contact in registration.ContactsList)
                    {
                        logger.Debug("Comparing: " + contact.ContactURI.ToString() + " to " + contactToRemove);

                        if (contactToRemove == null || contact.ContactURI.ToString() == contactToRemove)
                        {
                            logger.Debug("Attempting removal of registrar contact " + contact.ContactURI.ToString() + ".");
                            m_regAgent.SendContactRemovalRequest(registrationId, contact.ContactURI);
                        }
                    }
                }
                else
                {
                    logger.Warn("Could not find the registration record for " + registrationId + " in RemoveSIPContact.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception RemoveRegistrarContact. " + excp.Message);
                throw excp;
            }
        }

        [OperationContract]
        public void RefreshRegistrationAgentContact(Guid registrationId)
        {
            try
            {
                logger.Debug("RefreshRegistrationView webservice called for " + registrationId + ".");

                UserRegistration registration = m_regAgent.Get(registrationId);

                if (registration != null)
                {
                    m_regAgent.SendEmptyRegistrationRequest(registrationId);
                }
                else
                {
                    logger.Warn("Could not find the registration record for " + registrationId + " in RefreshRegistrationView.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception RefreshRegistrationView. " + excp.Message);
                throw excp;
            }
        }

        /// <summary>
        /// Send a Message Waiting Indicator request to the specified user agent URI. If setMWI is true the number of messages indicated will
        /// be one otherwise 0.
        /// </summary>
        [OperationContract]
        public void SendMWIRequest(string userAgentURI, bool setMWI, string username)
        {
            try
            {
                logger.Debug("Attempting SendMWIRequest for " + userAgentURI + " as " + setMWI + ".");

                SIPURI clientURI = SIPURI.ParseSIPURI(userAgentURI);
                SIPRequest mwiRequest = SIPNotificationAgent.GetNotifyRequest(m_sipTransport.GetTransportContact(null), null, clientURI, username, setMWI);
                SIPNonInviteTransaction mwiTransaction = m_sipTransport.CreateNonInviteTransaction(mwiRequest, clientURI.GetURIEndPoint(), m_sipTransport.GetTransportContact(null), SIPProtocolsEnum.UDP);
                mwiTransaction.NonInviteTransactionFinalResponseReceived += new SIPTransactionResponseReceivedDelegate(MWIFinalResponseReceived);

                FireProxyLogEvent(new ProxyMonitorEvent(ProxyServerTypesEnum.StatefulProxy, ProxyEventTypesEnum.MWI, "Sending " + setMWI + " mwi to " + SIPURI.ParseSIPURI(userAgentURI).CanonicalAddress, username));

                m_sipTransport.SendRequest(mwiRequest, SIPProtocolsEnum.UDP);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendMWIRequest. " + excp.Message + ".");
            }
        }

        private void MWIFinalResponseReceived(IPEndPoint localEndPoint,  IPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                string username = sipResponse.Header.From.FromURI.User;

                logger.Debug("MWI response received from " + IPSocket.GetSocketString(remoteEndPoint) + " " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " for " + username + ".");

                FireProxyLogEvent(new ProxyMonitorEvent(ProxyServerTypesEnum.StatefulProxy, ProxyEventTypesEnum.MWI, "MWI response received from " + IPSocket.GetSocketString(remoteEndPoint) + " " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase, username));
            }
            catch (Exception excp)
            {
                logger.Error("Exception MWIFinalResponseReceived. " + excp.Message);
            }
        }

        /// <summary>
        /// Send a Message Waiting Indicator request to the specified user agent URI. If setMWI is true the number of messages indicated will
        /// be one otherwise 0.
        /// </summary>
        [OperationContract]
        public void SendInviteRequest(string userAgentURI, string callerIdName, string callerIdUser, string username)
        {
            try
            {
                logger.Debug("Attempting SendInviteRequest for " + userAgentURI + " as " + callerIdName + " and " + callerIdUser  + ".");

                SIPURI clientURI = SIPURI.ParseSIPURI(userAgentURI);
                SIPRequest inviteRequest = GetInviteRequest(clientURI, callerIdName, callerIdUser, SERVER_STRING);
                UACInviteTransaction inviteTransaction = m_sipTransport.CreateUACTransaction(inviteRequest, clientURI.GetURIEndPoint(), m_sipTransport.GetTransportContact(null), SIPProtocolsEnum.UDP);
                inviteTransaction.UACInviteTransactionFinalResponseReceived += new SIPTransactionResponseReceivedDelegate(InviteFinalResponseReceived);

                FireProxyLogEvent(new ProxyMonitorEvent(ProxyServerTypesEnum.StatefulProxy, ProxyEventTypesEnum.NewCall, "Sending INVITE to " + SIPURI.ParseSIPURI(userAgentURI).ToString(), username));

                inviteTransaction.SendInviteRequest(inviteRequest.GetRequestEndPoint(), inviteRequest);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendInviteRequest. " + excp.Message + ".");
            }
        }

        private void InviteFinalResponseReceived(IPEndPoint localEndPoint,  IPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                logger.Debug("FinalResponseReceived for INVITE to " + sipTransaction.TransactionRequest.URI.ToString() + " " + sipResponse.Status + " " + sipResponse.ReasonPhrase + ".");
            }
            catch (Exception excp)
            {
                logger.Error("Exception InviteFinalResponseReceived. " + excp.Message);
            }
        }

        private SIPRequest GetInviteRequest(SIPURI dstURI, string callerIdName, string callerIdUser, string callerIdHost)
        {
            SIPRequest inviteRequest = new SIPRequest(SIPMethodsEnum.INVITE, dstURI);

            IPEndPoint localEndPoint = m_sipTransport.GetTransportContact(null);
            string localAddressStr = localEndPoint.Address.ToString();

            SIPToHeader toHeader = SIPToHeader.ParseToHeader("<" + dstURI.ToString() + ">");

            SIPFromHeader fromHeader = SIPFromHeader.ParseFromHeader(callerIdName + " <sip:" + callerIdUser + "@" + callerIdHost + ">");
            fromHeader.FromTag = CallProperties.CreateNewTag();
            SIPContactHeader contact = SIPContactHeader.ParseContactHeader("sip:" + IPSocket.GetSocketString(localEndPoint))[0];

            SIPHeader inviteHeader = new SIPHeader(contact, fromHeader, toHeader, 1, CallProperties.CreateNewCallId());
            inviteHeader.CSeqMethod = SIPMethodsEnum.INVITE;
            inviteRequest.Header = inviteHeader;

            SIPViaHeader viaHeader = new SIPViaHeader(localEndPoint.Address.ToString(), localEndPoint.Port, CallProperties.CreateBranchId());
            inviteRequest.Header.Via.PushViaHeader(viaHeader);

            inviteRequest.Body =
                "v=0" + m_CRLF +
                "o=" + SERVER_STRING + " 613 888 IN IP4 " + localAddressStr + m_CRLF +
                "s=SIP Call" + m_CRLF +
                "c=IN IP4 " + localAddressStr + m_CRLF +
                "t=0 0" + m_CRLF +
                "m=audio 10000 RTP/AVP 0" + m_CRLF +
                "a=rtpmap:0 PCMU/8000" + m_CRLF;

            inviteRequest.Header.ContentLength = inviteRequest.Body.Length;
            inviteRequest.Header.ContentType = "application/sdp";

            return inviteRequest;
        }
        
        private void FireProxyLogEvent(ProxyMonitorEvent monitorEvent)
        {                       
            if (StatefulProxyLogEvent != null)
            {
                try
                {
                    StatefulProxyLogEvent(monitorEvent);
                }
                catch (Exception excp)
                {
                    logger.Error("Exception FireProxyLogEvent SIPProxyWebService. " + excp.Message);
                }
            }
        }
    }
}

