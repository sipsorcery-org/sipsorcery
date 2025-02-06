// ============================================================================
// FileName: SIPNonInviteClientUserAgent.cs
//
// Description:
// A user agent that can send non-INVITE requests. The main need for this class
// is for sending non-INVITE requests that require authentication.
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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
    public class SIPNonInviteClientUserAgent
    {
        private static ILogger logger = Log.Logger;

        private SIPTransport m_sipTransport;
        private SIPEndPoint m_outboundProxy;
        private SIPCallDescriptor m_callDescriptor;

        public event Action<SIPResponse> ResponseReceived;

        public SIPNonInviteClientUserAgent(
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            SIPCallDescriptor callDescriptor)
        {
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            m_callDescriptor = callDescriptor;
        }

        public void SendRequest(SIPMethodsEnum method)
        {
            try
            {
                SIPRequest req = GetRequest(method);
                SIPNonInviteTransaction tran = new SIPNonInviteTransaction(m_sipTransport, req, m_outboundProxy);

                ManualResetEvent waitForResponse = new ManualResetEvent(false);
                tran.NonInviteTransactionFailed += TransactionFailed;
                tran.NonInviteTransactionFinalResponseReceived += ServerResponseReceived;
                tran.SendRequest();
            }
            catch (Exception excp)
            {
                logger.LogError(excp, "Exception SIPNonInviteClientUserAgent SendRequest to {Uri}. {ErrorMessage}", m_callDescriptor.Uri, excp.Message);
                throw;
            }
        }

        private void TransactionFailed(SIPTransaction sipTransaction, SocketError failureReason)
        {
            logger.LogWarning("Attempt to send {Method} to {Uri} failed with {FailureReason}.", sipTransaction.TransactionRequest.Method, m_callDescriptor.Uri, failureReason);
        }

        private Task<SocketError> ServerResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                string reasonPhrase = (sipResponse.ReasonPhrase.IsNullOrBlank()) ? sipResponse.Status.ToString() : sipResponse.ReasonPhrase;
                logger.LogDebug("Server response {StatusCode} {ReasonPhrase} received for {Method} to {Uri}.", sipResponse.StatusCode, reasonPhrase, sipTransaction.TransactionRequest.Method, m_callDescriptor.Uri);

                if (sipResponse.Status == SIPResponseStatusCodesEnum.ProxyAuthenticationRequired || sipResponse.Status == SIPResponseStatusCodesEnum.Unauthorised)
                {
                    if (sipResponse.Header.HasAuthenticationHeader)
                    {
                        if ((m_callDescriptor.Username != null || m_callDescriptor.AuthUsername != null) && m_callDescriptor.Password != null)
                        {
                            string username = (m_callDescriptor.AuthUsername != null) ? m_callDescriptor.AuthUsername : m_callDescriptor.Username;
                            SIPRequest authenticatedRequest = sipTransaction.TransactionRequest.DuplicateAndAuthenticate(
                                sipResponse.Header.AuthenticationHeaders, username, m_callDescriptor.Password);

                            SIPNonInviteTransaction authTransaction = new SIPNonInviteTransaction(m_sipTransport, authenticatedRequest, m_outboundProxy);
                            authTransaction.NonInviteTransactionFinalResponseReceived += AuthResponseReceived;
                            authTransaction.NonInviteTransactionFailed += TransactionFailed;
                            authTransaction.SendRequest();
                        }
                        else
                        {
                            logger.LogDebug("Send request received an authentication required response but no credentials were available.");
                            ResponseReceived?.Invoke(sipResponse);
                        }
                    }
                    else
                    {
                        logger.LogDebug("Send request failed with {StatusCode} but no authentication header was supplied for {Method} to {Uri}.", sipResponse.StatusCode, sipTransaction.TransactionRequest.Method, m_callDescriptor.Uri);
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
                logger.LogError(excp, "Exception SIPNonInviteClientUserAgent ServerResponseReceived ({RemoteEndPoint}). {ErrorMessage}", remoteEndPoint, excp.Message);
            }

            return Task.FromResult(SocketError.Success);
        }

        /// <summary>
        /// The event handler for responses to the authenticated register request.
        /// </summary>
        private Task<SocketError> AuthResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            logger.LogDebug("Server response {StatusCode} {ReasonPhrase} received for authenticated {Method} to {Uri}.", sipResponse.Status, (sipResponse.ReasonPhrase.IsNullOrBlank()) ? sipResponse.Status.ToString() : sipResponse.ReasonPhrase, sipTransaction.TransactionRequest.Method, m_callDescriptor.Uri);

            if (ResponseReceived != null)
            {
                ResponseReceived(sipResponse);
            }

            return Task.FromResult(SocketError.Success);
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
                header.UserAgent = SIPConstants.SipUserAgentVersionString;
                request.Header = header;

                header.Vias.PushViaHeader(SIPViaHeader.GetDefaultSIPViaHeader());

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
                    logger.LogError(excp, "Exception Parsing CustomHeader for SIPNonInviteClientUserAgent GetRequest. {ErrorMessage} {CustomHeaders}", excp.Message, m_callDescriptor.CustomHeaders);
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
                logger.LogError(excp, "Exception SIPNonInviteClientUserAgent GetRequest. {ErrorMessage}", excp.Message);
                throw;
            }
        }
    }
}
