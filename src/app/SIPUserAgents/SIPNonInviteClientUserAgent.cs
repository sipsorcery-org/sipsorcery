﻿// ============================================================================
// FileName: SIPNonInviteClientUserAgent.cs
//
// Description:
// A user agent that can send non-INVITE requests. The main need for this class is for
// sending non-INVITE requests that require authentication.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 26 Apr 2011	Aaron Clauson	Created, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
    public class SIPNonInviteClientUserAgent
    {
        private static ILogger logger = Log.Logger;

        private static readonly string m_userAgent = SIPConstants.SIP_USERAGENT_STRING;

        private SIPMonitorLogDelegate Log_External;

        private SIPTransport m_sipTransport;
        private SIPEndPoint m_outboundProxy;
        private SIPCallDescriptor m_callDescriptor;
        private string m_owner;
        private string m_adminMemberID;
        private string m_lastServerNonce;

        public event Action<SIPResponse> ResponseReceived;

        public SIPNonInviteClientUserAgent(
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            SIPCallDescriptor callDescriptor,
            string owner,
            string adminMemberID,
            SIPMonitorLogDelegate logDelegate)
        {
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            m_callDescriptor = callDescriptor;
            m_owner = owner;
            m_adminMemberID = adminMemberID;

            Log_External = logDelegate;
        }

        public void SendRequest(SIPMethodsEnum method)
        {
            try
            {
                SIPRequest req = GetRequest(method);
                SIPNonInviteTransaction tran = new SIPNonInviteTransaction(m_sipTransport, req, m_outboundProxy);

                ManualResetEvent waitForResponse = new ManualResetEvent(false);
                tran.NonInviteTransactionTimedOut += RequestTimedOut;
                tran.NonInviteTransactionFinalResponseReceived += ServerResponseReceived;
                tran.SendReliableRequest();
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPNonInviteClientUserAgent SendRequest to " + m_callDescriptor.Uri + ". " + excp.Message);
                throw;
            }
        }

        private void RequestTimedOut(SIPTransaction sipTransaction)
        {
            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Attempt to send " + sipTransaction.TransactionRequest.Method + " to " + m_callDescriptor.Uri + " timed out.", m_owner));
        }

        private void ServerResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                string reasonPhrase = (sipResponse.ReasonPhrase.IsNullOrBlank()) ? sipResponse.Status.ToString() : sipResponse.ReasonPhrase;
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Server response " + sipResponse.StatusCode + " " + reasonPhrase + " received for " + sipTransaction.TransactionRequest.Method + " to " + m_callDescriptor.Uri + ".", m_owner));

                if (sipResponse.Status == SIPResponseStatusCodesEnum.ProxyAuthenticationRequired || sipResponse.Status == SIPResponseStatusCodesEnum.Unauthorised)
                {
                    if (sipResponse.Header.AuthenticationHeader != null)
                    {
                        if ((m_callDescriptor.Username != null || m_callDescriptor.AuthUsername != null) && m_callDescriptor.Password != null)
                        {
                            SIPRequest authenticatedRequest = GetAuthenticatedRequest(sipTransaction.TransactionRequest, sipResponse);
                            SIPNonInviteTransaction authTransaction = new SIPNonInviteTransaction(m_sipTransport, authenticatedRequest, m_outboundProxy);
                            authTransaction.NonInviteTransactionFinalResponseReceived += AuthResponseReceived;
                            authTransaction.NonInviteTransactionTimedOut += RequestTimedOut;
                            m_sipTransport.SendReliable(authTransaction);
                        }
                        else
                        {
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Send request received an authentication required response but no credentials were available.", m_owner));

                            ResponseReceived?.Invoke(sipResponse);
                        }
                    }
                    else
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Send request failed with " + sipResponse.StatusCode + " but no authentication header was supplied for " + sipTransaction.TransactionRequest.Method + " to " + m_callDescriptor.Uri + ".", m_owner));

                        ResponseReceived?.Invoke(sipResponse);
                    }
                }
                else
                {
                    ResponseReceived?.Invoke(sipResponse);
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPNonInviteClientUserAgent ServerResponseReceived (" + remoteEndPoint + "). " + excp.Message);
            }
        }

        /// <summary>
        /// The event handler for responses to the authenticated register request.
        /// </summary>
        private void AuthResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            string reasonPhrase = (sipResponse.ReasonPhrase.IsNullOrBlank()) ? sipResponse.Status.ToString() : sipResponse.ReasonPhrase;
            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Server response " + sipResponse.Status + " " + reasonPhrase + " received for authenticated " + sipTransaction.TransactionRequest.Method + " to " + m_callDescriptor.Uri + ".", m_owner));

            if (ResponseReceived != null)
            {
                ResponseReceived(sipResponse);
            }
        }

        private SIPRequest GetRequest(SIPMethodsEnum method)
        {
            try
            {
                SIPURI uri = SIPURI.ParseSIPURIRelaxed(m_callDescriptor.Uri);

                SIPRequest request = new SIPRequest(method, uri);
                SIPFromHeader fromHeader = m_callDescriptor.GetFromHeader();
                fromHeader.FromTag = CallProperties.CreateNewTag();
                SIPToHeader toHeader = new SIPToHeader(null, uri, null);
                int cseq = Crypto.GetRandomInt(10000, 20000);

                SIPHeader header = new SIPHeader(fromHeader, toHeader, cseq, CallProperties.CreateNewCallId());
                header.CSeqMethod = method;
                header.UserAgent = m_userAgent;
                request.Header = header;
                request.Header.Vias.PushViaHeader(SIPViaHeader.GetDefaultSIPViaHeader());

                try
                {
                    if (m_callDescriptor.CustomHeaders != null && m_callDescriptor.CustomHeaders.Count > 0)
                    {
                        foreach (string customHeader in m_callDescriptor.CustomHeaders)
                        {
                            if (customHeader.IsNullOrBlank())
                            {
                                continue;
                            }
                            else if (customHeader.Trim().StartsWith(SIPHeaders.SIP_HEADER_USERAGENT))
                            {
                                request.Header.UserAgent = customHeader.Substring(customHeader.IndexOf(":") + 1).Trim();
                            }
                            else
                            {
                                request.Header.UnknownHeaders.Add(customHeader);
                            }
                        }
                    }
                }
                catch (Exception excp)
                {
                    logger.LogError("Exception Parsing CustomHeader for SIPNonInviteClientUserAgent GetRequest. " + excp.Message + m_callDescriptor.CustomHeaders);
                }

                if (!m_callDescriptor.Content.IsNullOrBlank())
                {
                    request.Body = m_callDescriptor.Content;
                    request.Header.ContentType = m_callDescriptor.ContentType;
                    request.Header.ContentLength = request.Body.Length;
                }

                return request;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPNonInviteClientUserAgent GetRequest. " + excp.Message);
                throw excp;
            }
        }

        private SIPRequest GetAuthenticatedRequest(SIPRequest originalRequest, SIPResponse sipResponse)
        {
            try
            {
                SIPAuthorisationDigest digest = sipResponse.Header.AuthenticationHeader.SIPDigest;
                m_lastServerNonce = digest.Nonce;
                string username = (m_callDescriptor.AuthUsername != null) ? m_callDescriptor.AuthUsername : m_callDescriptor.Username;
                digest.SetCredentials(username, m_callDescriptor.Password, originalRequest.URI.ToString(), originalRequest.Method.ToString());

                SIPRequest authRequest = originalRequest.Copy();
                authRequest.SetSendFromHints(originalRequest.LocalSIPEndPoint);
                authRequest.Header.Vias.TopViaHeader.Branch = CallProperties.CreateBranchId();
                authRequest.Header.From.FromTag = CallProperties.CreateNewTag();
                authRequest.Header.To.ToTag = null;
                authRequest.Header.CallId = CallProperties.CreateNewCallId();
                authRequest.Header.CSeq = originalRequest.Header.CSeq + 1;

                authRequest.Header.AuthenticationHeader = new SIPAuthenticationHeader(digest);
                authRequest.Header.AuthenticationHeader.SIPDigest.Response = digest.Digest;

                return authRequest;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPNonInviteClientUserAgent GetAuthenticatedRequest. " + excp.Message);
                throw excp;
            }
        }
    }
}
