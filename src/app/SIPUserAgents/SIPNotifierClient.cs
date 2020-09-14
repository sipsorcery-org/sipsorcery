// ============================================================================
// FileName: SIPNotifierClient.cs
//
// Description:
// A SIP client for a SIP notifier server. The client establishes subscriptions to
// the notifier server as per RFC3265.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 23 Feb 2010  Aaron Clauson   Created, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
    /// <summary>
    /// This class represent a client for a SIP notifier server. The client can subscribe to notifications from the
    /// server as outlined in RFC3265. The generic parameter is used to set the type of notification the client will
    /// generate. Different SIP event packages have different ways of representing their data. For example RFC4235
    /// uses XML to convey dialog notifications, RFC3842 uses plain text to convey message waiting indications.
    /// </summary>
    public class SIPNotifierClient<T> where T : SIPEvent, new()
    {
        private const int DEFAULT_SUBSCRIBE_EXPIRY = 300;       // The default value to request on subscription requests.
                                                                //private const int RETRY_POST_FAILURE_INTERVAL = 300;    // The interval to retry the subscription after a failure response or timeout.
        private const int RESCHEDULE_SUBSCRIBE_MARGIN = 10;     // Reschedule subsequent subscriptions with a small margin to try and ensure there is no gap.
        private const int MAX_SUBSCRIBE_ATTEMPTS = 4;           // The maximum number of subscribe attempts that will be made without a failure condition before incurring a temporary failure.

        private static readonly string m_filterTextType = SIPMIMETypes.MWI_TEXT_TYPE;

        private static ILogger logger = Log.Logger;

        private SIPTransport m_sipTransport;
        private SIPEndPoint m_outboundProxy;
        private SIPEventPackage m_sipEventPackage;

        private SIPURI m_resourceURI;
        private string m_authUsername;
        private string m_authDomain;
        private string m_authPassword;
        private string m_filter;
        private int m_expiry;
        private int m_localCSeq;
        private int m_remoteCSeq;
        private string m_subscribeCallID;
        private string m_subscriptionFromTag;
        private string m_subscriptionToTag;
        private bool m_subscribed;
        private int m_attempts;
        private ManualResetEvent m_waitForSubscribeResponse = new ManualResetEvent(false);
        private ManualResetEvent m_waitForNextSubscribe = new ManualResetEvent(false);
        private bool m_exit;

        public DateTime LastSubscribeAttempt { get; private set; }

        public string CallID
        {
            get { return m_subscribeCallID; }
        }

        public event Action<T> NotificationReceived;
        public event Action<SIPURI, SIPResponseStatusCodesEnum, string> SubscriptionFailed;
        public event Action<SIPURI> SubscriptionSuccessful;

        public SIPNotifierClient(
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            SIPEventPackage sipEventPackage,
            SIPURI resourceURI,
            string authUsername,
            string authDomain,
            string authPassword,
            int expiry,
            string filter)
        {
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            m_sipEventPackage = sipEventPackage;
            m_authUsername = authUsername;
            m_authDomain = authDomain;
            m_authPassword = authPassword;
            m_expiry = (expiry > 0) ? expiry : DEFAULT_SUBSCRIBE_EXPIRY;
            m_filter = filter;
            m_resourceURI = resourceURI.CopyOf();
            m_subscribeCallID = CallProperties.CreateNewCallId();
            m_subscriptionFromTag = CallProperties.CreateNewTag();
        }

        public void Start()
        {
            m_exit = false;
            ThreadPool.QueueUserWorkItem(delegate
            { StartSubscription(); });
        }

        public void Stop()
        {
            try
            {
                if (!m_exit)
                {
                    logger.LogDebug("Stopping SIP notifier user agent for user " + m_authUsername + " and resource URI " + m_resourceURI.ToString() + ".");

                    m_exit = true;
                    m_attempts = 0;
                    ThreadPool.QueueUserWorkItem(delegate
                    { Subscribe(m_resourceURI, 0, m_sipEventPackage, m_subscribeCallID, null); });
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPNotifierClient Stop. " + excp.Message);
            }
        }

        public async Task GotNotificationRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            try
            {
                logger.LogDebug("SIPNotifierClient GotNotificationRequest for " + sipRequest.Method + " " + sipRequest.URI.ToString() + " " + sipRequest.Header.CSeq + ".");

                SIPResponse okResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                await m_sipTransport.SendResponseAsync(okResponse).ConfigureAwait(false);

                //logger.LogDebug(sipRequest.ToString());

                if (sipRequest.Method == SIPMethodsEnum.NOTIFY && sipRequest.Header.CallId == m_subscribeCallID &&
                     sipRequest.Header.Event == m_sipEventPackage.ToString() && sipRequest.Body != null)
                {
                    if (sipRequest.Header.CSeq <= m_remoteCSeq)
                    {
                        logger.LogWarning("A duplicate NOTIFY request received by SIPNotifierClient for subscription Call-ID " + m_subscribeCallID + ".");
                    }
                    else
                    {
                        //logger.LogDebug("New dialog info notification request received.");
                        m_remoteCSeq = sipRequest.Header.CSeq;
                        T sipEvent = new T();
                        sipEvent.Load(sipRequest.Body);
                        NotificationReceived(sipEvent);
                    }
                }
                else
                {
                    logger.LogWarning("A request received by SIPNotifierClient did not match the subscription details, request Call-ID=" + sipRequest.Header.CallId + ", subscribed Call-ID=" + m_subscribeCallID + ".");
                    logger.LogDebug(sipRequest.ToString());
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception GotNotificationRequest. " + excp.Message);
            }
        }

        /// <summary>
        /// If the client is waiting for the timeout until the next subscribe is due calling this method will result
        /// in an immediate attempt to re-subscribe. When a subscribe request is received the notification server should
        /// send a full state notification so this method is useful to refresh client state.
        /// </summary>
        public void Resubscribe()
        {
            m_waitForNextSubscribe.Set();
        }

        private void StartSubscription()
        {
            try
            {
                logger.LogDebug("SIPNotifierClient starting for " + m_resourceURI.ToString() + " and event package " + m_sipEventPackage.ToString() + ".");

                while (!m_exit)
                {
                    m_attempts = 0;
                    m_subscribed = false;
                    m_waitForSubscribeResponse.Reset();

                    Subscribe(m_resourceURI, m_expiry, m_sipEventPackage, m_subscribeCallID, null);

                    m_waitForSubscribeResponse.WaitOne();

                    if (!m_exit)
                    {
                        m_waitForNextSubscribe.Reset();

                        if (m_subscribed)
                        {
                            // Schedule the subscription based on its expiry.
                            logger.LogDebug("Rescheduling next attempt for a successful subscription to " + m_resourceURI.ToString() + " in " + (m_expiry - RESCHEDULE_SUBSCRIBE_MARGIN) + "s.");
                            m_waitForNextSubscribe.WaitOne((m_expiry - RESCHEDULE_SUBSCRIBE_MARGIN) * 1000);
                        }
                        else
                        {
                            //Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.NotifierClient, SIPMonitorEventTypesEnum.SubscribeFailed, "Rescheduling next attempt for a failed subscription to " + m_resourceURI.ToString() + " in " + RETRY_POST_FAILURE_INTERVAL + "s.", null));
                            //m_waitForNextSubscribe.WaitOne(RETRY_POST_FAILURE_INTERVAL * 1000);
                            break;
                        }
                    }
                }

                logger.LogWarning("Subscription attempts to " + m_resourceURI.ToString() + " for " + m_sipEventPackage.ToString() + " have been halted.");
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPNotifierClient StartSubscription. " + excp.Message);
            }
        }

        /// <summary>
        /// Initiates a SUBSCRIBE request to a notification server.
        /// </summary>
        /// <param name="subscribeToURI">The SIP user that dialog notifications are being subscribed to.</param>
        public void Subscribe(SIPURI subscribeURI, int expiry, SIPEventPackage sipEventPackage, string subscribeCallID, SIPURI contactURI)
        {
            try
            {
                if (m_attempts >= MAX_SUBSCRIBE_ATTEMPTS)
                {
                    logger.LogWarning("Subscription to " + subscribeURI.ToString() + " reached the maximum number of allowed attempts without a failure condition.");
                    m_subscribed = false;
                    SubscriptionFailed(subscribeURI, SIPResponseStatusCodesEnum.InternalServerError, "Subscription reached the maximum number of allowed attempts.");
                    m_waitForSubscribeResponse.Set();
                }
                else
                {
                    m_attempts++;
                    m_localCSeq++;

                    SIPRequest subscribeRequest = SIPRequest.GetRequest(
                        SIPMethodsEnum.SUBSCRIBE,
                        m_resourceURI,
                        new SIPToHeader(null, subscribeURI, m_subscriptionToTag),
                        null);


                    if (contactURI != null)
                    {
                        subscribeRequest.Header.Contact = new List<SIPContactHeader>() { new SIPContactHeader(null, contactURI) };
                    }

                    subscribeRequest.Header.From = new SIPFromHeader(null, new SIPURI(m_authUsername, m_authDomain, null, SIPSchemesEnum.sip, SIPProtocolsEnum.udp), m_subscriptionFromTag);
                    subscribeRequest.Header.CSeq = m_localCSeq;
                    subscribeRequest.Header.Expires = expiry;
                    subscribeRequest.Header.Event = sipEventPackage.ToString();
                    subscribeRequest.Header.CallId = subscribeCallID;

                    if (!m_filter.IsNullOrBlank())
                    {
                        subscribeRequest.Body = m_filter;
                        subscribeRequest.Header.ContentLength = m_filter.Length;
                        subscribeRequest.Header.ContentType = m_filterTextType;
                    }

                    SIPNonInviteTransaction subscribeTransaction = new SIPNonInviteTransaction(m_sipTransport, subscribeRequest, m_outboundProxy);
                    subscribeTransaction.NonInviteTransactionFinalResponseReceived += SubscribeTransactionFinalResponseReceived;
                    subscribeTransaction.NonInviteTransactionTimedOut += SubsribeTransactionTimedOut;

                    m_sipTransport.SendTransaction(subscribeTransaction);

                    LastSubscribeAttempt = DateTime.Now;
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPNotifierClient Subscribe. " + excp.Message);
                SubscriptionFailed(m_resourceURI, SIPResponseStatusCodesEnum.InternalServerError, "Exception Subscribing. " + excp.Message);
                m_waitForSubscribeResponse.Set();
            }
        }

        private void SubsribeTransactionTimedOut(SIPTransaction sipTransaction)
        {
            if (SubscriptionFailed != null)
            {
                SubscriptionFailed(m_resourceURI, SIPResponseStatusCodesEnum.ServerTimeout, "Subscription request to " + m_resourceURI.ToString() + " timed out.");
            }

            m_waitForSubscribeResponse.Set();
        }

        private Task<SocketError> SubscribeTransactionFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                if (sipResponse.Status == SIPResponseStatusCodesEnum.IntervalTooBrief)
                {
                    // The expiry interval used was too small. Adjust and try again.
                    m_expiry = (sipResponse.Header.MinExpires > 0) ? sipResponse.Header.MinExpires : m_expiry * 2;
                    logger.LogWarning("A subscribe request was rejected with IntervalTooBrief, adjusting expiry to " + m_expiry + " and trying again.");
                    Subscribe(m_resourceURI, m_expiry, m_sipEventPackage, m_subscribeCallID, null);
                }
                else if (sipResponse.Status == SIPResponseStatusCodesEnum.Forbidden)
                {
                    // The subscription is never going to succeed so cancel it.
                    SubscriptionFailed(m_resourceURI, sipResponse.Status, "A Forbidden response was received on a subscribe attempt to " + m_resourceURI.ToString() + " for user " + m_authUsername + ".");
                    m_exit = true;
                    m_waitForSubscribeResponse.Set();
                }
                else if (sipResponse.Status == SIPResponseStatusCodesEnum.BadEvent)
                {
                    // The subscription is never going to succeed so cancel it.
                    SubscriptionFailed(m_resourceURI, sipResponse.Status, "A BadEvent response was received on a subscribe attempt to " + m_resourceURI.ToString() + " for event package " + m_sipEventPackage.ToString() + ".");
                    m_exit = true;
                    m_waitForSubscribeResponse.Set();
                }
                else if (sipResponse.Status == SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist)
                {
                    // The notifier server does not have a record for the existing subscription.
                    SubscriptionFailed(m_resourceURI, sipResponse.Status, "Subscribe failed with response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");
                    m_waitForSubscribeResponse.Set();
                }
                else if (sipResponse.Status == SIPResponseStatusCodesEnum.ProxyAuthenticationRequired || sipResponse.Status == SIPResponseStatusCodesEnum.Unauthorised)
                {
                    if (m_authUsername.IsNullOrBlank() || m_authPassword.IsNullOrBlank())
                    {
                        // No point trying to authenticate if there are no credentials to use.
                        SubscriptionFailed(m_resourceURI, sipResponse.Status, "Authentication requested on subscribe request when no credentials available.");
                        m_waitForSubscribeResponse.Set();
                    }
                    else if (sipResponse.Header.AuthenticationHeader != null)
                    {
                        if (m_attempts >= MAX_SUBSCRIBE_ATTEMPTS)
                        {
                            m_subscribed = false;
                            SubscriptionFailed(m_resourceURI, SIPResponseStatusCodesEnum.InternalServerError, "Subscription reached the maximum number of allowed attempts.");
                            m_waitForSubscribeResponse.Set();
                        }
                        else
                        {
                            logger.LogDebug("Attempting authentication for subscribe request for event package " + m_sipEventPackage.ToString() + " and " + m_resourceURI.ToString() + ".");

                            m_attempts++;

                            // Resend SUBSCRIBE with credentials.
                            SIPAuthorisationDigest authRequest = sipResponse.Header.AuthenticationHeader.SIPDigest;
                            authRequest.SetCredentials(m_authUsername, m_authPassword, m_resourceURI.ToString(), SIPMethodsEnum.SUBSCRIBE.ToString());

                            SIPRequest authSubscribeRequest = sipTransaction.TransactionRequest;
                            authSubscribeRequest.Header.AuthenticationHeader = new SIPAuthenticationHeader(authRequest);
                            authSubscribeRequest.Header.AuthenticationHeader.SIPDigest.Response = authRequest.Digest;
                            authSubscribeRequest.Header.Vias.TopViaHeader.Branch = CallProperties.CreateBranchId();
                            m_localCSeq = sipTransaction.TransactionRequest.Header.CSeq + 1;
                            authSubscribeRequest.Header.CSeq = m_localCSeq;
                            authSubscribeRequest.Header.CallId = m_subscribeCallID;

                            if (!m_filter.IsNullOrBlank())
                            {
                                authSubscribeRequest.Body = m_filter;
                                authSubscribeRequest.Header.ContentLength = m_filter.Length;
                                authSubscribeRequest.Header.ContentType = m_filterTextType;
                            }

                            // Create a new transaction to establish the authenticated server call.
                            SIPNonInviteTransaction subscribeTransaction = new SIPNonInviteTransaction(m_sipTransport, authSubscribeRequest, m_outboundProxy);
                            subscribeTransaction.NonInviteTransactionFinalResponseReceived += SubscribeTransactionFinalResponseReceived;
                            subscribeTransaction.NonInviteTransactionTimedOut += SubsribeTransactionTimedOut;

                            m_sipTransport.SendTransaction(subscribeTransaction);
                        }
                    }
                    else
                    {
                        SubscriptionFailed(sipTransaction.TransactionRequestURI, sipResponse.Status, "Subscribe requested authentication but did not provide an authentication header.");
                        m_waitForSubscribeResponse.Set();
                    }
                }
                else if (sipResponse.StatusCode >= 200 && sipResponse.StatusCode <= 299)
                {
                    logger.LogDebug("Authenticating subscribe request for event package " + m_sipEventPackage.ToString() + " and " + m_resourceURI.ToString() + " was successful.");

                    m_subscribed = true;
                    m_subscriptionToTag = sipResponse.Header.To.ToTag;
                    SubscriptionSuccessful(m_resourceURI);
                    m_waitForSubscribeResponse.Set();
                }
                else
                {
                    SubscriptionFailed(m_resourceURI, sipResponse.Status, "Subscribe failed with response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");
                    m_waitForSubscribeResponse.Set();
                }

                return Task.FromResult(SocketError.Success);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SubscribeTransactionFinalResponseReceived. " + excp.Message);
                SubscriptionFailed(m_resourceURI, SIPResponseStatusCodesEnum.InternalServerError, "Exception processing subscribe response. " + excp.Message);
                m_waitForSubscribeResponse.Set();

                return Task.FromResult(SocketError.Fault);
            }
        }
    }
}
