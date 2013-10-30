//-----------------------------------------------------------------------------
// Filename: GoogleVoiceCall.cs
//
// Description: A dial plan command that places HTTP request to initiate a call 
// through the Google Voice service and bridges the callback with the original caller.
// 
// History:
// 11 Aug 2009	    Aaron Clauson	    Created.
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.AppServer.DialPlan
{
    public class GoogleVoiceCall
    {
        private const string GOOGLE_COM_URL = "https://www.google.com";
        private const string PRE_LOGIN_URL = "https://www.google.com/accounts/ServiceLogin";
        //private const string LOGIN_URL = "https://www.google.com/accounts/ServiceLoginAuth?service=grandcentral";
        private const string LOGIN_URL = "https://accounts.google.com/accounts/ServiceLogin?service=grandcentral";
        private const string VOICE_HOME_URL = "https://www.google.com/voice";
        private const string VOICE_CALL_URL = "https://www.google.com/voice/call/connect";
        private const string CANCEL_CALL_URL = "https://www.google.com/voice/call/cancel";
        private const int MIN_CALLBACK_TIMEOUT = 3;
        private const int MAX_CALLBACK_TIMEOUT = 60;
        private const int WAIT_FOR_CALLBACK_TIMEOUT = 30;
        private const int HTTP_REQUEST_TIMEOUT = 5;

        private static ILog logger = AppState.logger;
        private SIPMonitorLogDelegate Log_External;

        private SIPTransport m_sipTransport;
        private ISIPCallManager m_callManager;
        private string m_username;
        private string m_adminMemberId;
        private SIPEndPoint m_outboundProxy;

        private string m_forwardingNumber;
        private string m_fromURIUserRegexMatch;
        private string m_destinationNumber;
        private ManualResetEvent m_waitForCallback = new ManualResetEvent(false);
        private ISIPServerUserAgent m_callbackCall;
        private bool m_clientCallCancelled;
        private bool m_hasBeenCancelled;
        private CookieContainer m_cookies;
        private string m_rnrKey;

        internal event CallProgressDelegate CallProgress;

        public GoogleVoiceCall(
            SIPTransport sipTransport,
            ISIPCallManager callManager,
            SIPMonitorLogDelegate logDelegate,
            string username,
            string adminMemberId,
            SIPEndPoint outboundProxy)
        {
            m_sipTransport = sipTransport;
            m_callManager = callManager;
            Log_External = logDelegate;
            m_username = username;
            m_adminMemberId = adminMemberId;
            m_outboundProxy = outboundProxy;
        }

        /// <summary>
        /// Initiates a Google Voice callback by sending 3 HTTP requests and then waiting for the incoming SIP call.
        /// </summary>
        /// <param name="emailAddress">The Google Voice email address to login with.</param>
        /// <param name="password">The Google Voice password to login with.</param>
        /// <param name="forwardingNumber">The number to request Google Voice to do the intial callback on.</param>
        /// <param name="destinationNumber">The number to request Google Voice to dial out on. This is what Google will attempt to
        /// call once the callback on the forwardingNumber is answered.</param>
        /// <param name="fromUserToMatch">The FromURI user to match to recognise the incoming call. If null it will be assumed that
        /// Gizmo is being used and the X-GoogleVoice header will be used.</param>
        /// <param name="contentType">The content type of the SIP call into sipsorcery that created the Google Voice call. It is
        /// what will be sent in the Ok response to the initial incoming callback.</param>
        /// <param name="body">The content of the SIP call into sipsorcery that created the Google Voice call. It is
        /// what will be sent in the Ok response to the initial incoming callback.</param>
        /// <returns>If successful the dialogue of the established call otherwsie null.</returns>
        public SIPDialogue InitiateCall(string emailAddress, string password, string forwardingNumber, string destinationNumber, string fromUserRegexMatch, int phoneType, int waitForCallbackTimeout, string contentType, string body)
        {
            try
            {
                m_forwardingNumber = forwardingNumber;
                m_destinationNumber = destinationNumber;
                m_fromURIUserRegexMatch = fromUserRegexMatch;

                if (CallProgress != null)
                {
                    //CallProgress(SIPResponseStatusCodesEnum.Ringing, "Initiating Google Voice call", null, null, null);
                    CallProgress(SIPResponseStatusCodesEnum.Ringing, null, null, null, null, null);
                }

                m_cookies = new CookieContainer();
                m_rnrKey = Login(emailAddress, password);
                if (!m_rnrKey.IsNullOrBlank())
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call key " + m_rnrKey + " successfully retrieved for " + emailAddress + ", proceeding with callback.", m_username));
                    return SendCallRequest(forwardingNumber, destinationNumber, phoneType, waitForCallbackTimeout, contentType, body);
                }
                else
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call key was not retrieved for " + emailAddress + " callback cannot proceed.", m_username));
                    return null;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GoogleVoiceCall InitiateCall. " + excp.Message);
                throw;
            }
        }

        private string Login(string emailAddress, string password)
        {
            try
            {
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Logging into google.com for " + emailAddress + ".", m_username));

                // Fetch GALX
                HttpWebRequest galxRequest = (HttpWebRequest)WebRequest.Create(PRE_LOGIN_URL);
                galxRequest.ConnectionGroupName = "prelogin";
                galxRequest.CookieContainer = m_cookies;

                HttpWebResponse galxResponse = (HttpWebResponse)galxRequest.GetResponse();
                if (galxResponse.StatusCode != HttpStatusCode.OK)
                {
                    galxResponse.Close();
                    throw new ApplicationException("Load of the Google Voice pre-login page failed with response " + galxResponse.StatusCode + ".");
                }
                else
                {
                    // The pre login URL can redirect to a different URL, such as accounts.google.com, need to use the cookies from that redirect when accessing www.google.com.
                    m_cookies.Add(new Uri(GOOGLE_COM_URL), galxResponse.Cookies);
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Google Voice pre-login page loaded successfully.", m_username));
                }

                StreamReader galxReader = new StreamReader(galxResponse.GetResponseStream());
                string galxResponseFromServer = galxReader.ReadToEnd();
                galxResponse.Close();

                Match galxMatch = Regex.Match(galxResponseFromServer, @"name=""GALX""[^>]+value=""(?<galxvalue>.*?)""");
                if (galxMatch.Success)
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "GALX key " + galxMatch.Result("${galxvalue}") + " successfully retrieved.", m_username));
                }
                else
                {
                    throw new ApplicationException("Could not find GALX key on your Google Voice pre-login page, callback cannot proceed.");
                }

                // Build login request.
                string loginData = "Email=" + Uri.EscapeDataString(emailAddress) + "&Passwd=" + Uri.EscapeDataString(password) + "&GALX=" + Uri.EscapeDataString(galxMatch.Result("${galxvalue}"));
                HttpWebRequest loginRequest = (HttpWebRequest)WebRequest.Create(LOGIN_URL);
                loginRequest.CookieContainer = m_cookies;
                loginRequest.ConnectionGroupName = "login";
                loginRequest.AllowAutoRedirect = true;
                loginRequest.Method = "POST";
                loginRequest.ContentType = "application/x-www-form-urlencoded;charset=utf-8";
                loginRequest.ContentLength = loginData.Length;
                loginRequest.GetRequestStream().Write(Encoding.UTF8.GetBytes(loginData), 0, loginData.Length);
                loginRequest.Timeout = HTTP_REQUEST_TIMEOUT * 1000;

                // Send login request and read response stream.
                HttpWebResponse response = (HttpWebResponse)loginRequest.GetResponse();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    response.Close();
                    throw new ApplicationException("Login to google.com failed for " + emailAddress + " with response " + response.StatusCode + ".");
                }

                StreamReader loginResponseReader = new StreamReader(response.GetResponseStream());
                string loginResponseFromServer = loginResponseReader.ReadToEnd();
                response.Close();

                if (Regex.Match(loginResponseFromServer, @"\<title/\>Google Accounts\</title/\>").Success)
                {
                    throw new ApplicationException("Login to google.com appears to have failed for " + emailAddress + ".");
                }

                // We're now logged in. Need to load up the Google Voice page to get the rnr hidden input value which is needed for
                // the HTTP call requests.
                HttpWebRequest rnrRequest = (HttpWebRequest)WebRequest.Create(VOICE_HOME_URL);
                rnrRequest.ConnectionGroupName = "call";
                rnrRequest.CookieContainer = m_cookies;

                // Send the Google Voice account page request and read response stream.
                response = (HttpWebResponse)rnrRequest.GetResponse();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    response.Close();
                    throw new ApplicationException("Load of the Google Voice account page failed for " + emailAddress + " with response " + response.StatusCode + ".");
                }
                else
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Google Voice home page loaded successfully.", m_username));
                }

                StreamReader reader = new StreamReader(response.GetResponseStream());
                string responseFromServer = reader.ReadToEnd();
                response.Close();

                // Extract the rnr field from the HTML.
                Match rnrMatch = Regex.Match(responseFromServer, @"name=""_rnr_se"".*?value=""(?<rnrvalue>.*?)""");
                if (rnrMatch.Success)
                {
                    return rnrMatch.Result("${rnrvalue}");
                }
                else
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan,"Could not find _rnr_se key on your Google Voice account page, callback cannot proceed.", m_username));
                    throw new ApplicationException("Could not find _rnr_se key on your Google Voice account page, callback cannot proceed.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GoogleVoiceCall Login. " + excp.Message);
                throw;
            }
        }

        private SIPDialogue SendCallRequest(string forwardingNumber, string destinationNumber, int phoneType, int waitForCallbackTimeout, string contentType, string body)
        {
            try
            {
                int callbackTimeout = (waitForCallbackTimeout < MIN_CALLBACK_TIMEOUT || waitForCallbackTimeout > MAX_CALLBACK_TIMEOUT) ? WAIT_FOR_CALLBACK_TIMEOUT : waitForCallbackTimeout;

                CallbackWaiter callbackWaiter = new CallbackWaiter(m_username, CallbackWaiterEnum.GoogleVoice, forwardingNumber, MatchIncomingCall);
                m_callManager.AddWaitingApplication(callbackWaiter);

                string callData = "outgoingNumber=" + Uri.EscapeDataString(destinationNumber) + "&forwardingNumber=" + Uri.EscapeDataString(forwardingNumber) +
                    "&subscriberNumber=undefined&remember=0&_rnr_se=" + Uri.EscapeDataString(m_rnrKey) + "&phoneType=" + phoneType;
                //logger.Debug("call data=" + callData + ".");

                // Build the call request.
                HttpWebRequest callRequest = (HttpWebRequest)WebRequest.Create(VOICE_CALL_URL);
                callRequest.ConnectionGroupName = "call";
                callRequest.CookieContainer = m_cookies;
                callRequest.Method = "POST";
                callRequest.ContentType = "application/x-www-form-urlencoded;charset=utf-8";
                callRequest.ContentLength = callData.Length;
                callRequest.GetRequestStream().Write(Encoding.UTF8.GetBytes(callData), 0, callData.Length);
                callRequest.Timeout = HTTP_REQUEST_TIMEOUT * 1000;

                HttpWebResponse response = (HttpWebResponse)callRequest.GetResponse();
                HttpStatusCode responseStatus = response.StatusCode;
                response.Close();
                if (responseStatus != HttpStatusCode.OK)
                {
                    throw new ApplicationException("The call request failed with a " + responseStatus + " response.");
                }
                else
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Google Voice Call to " + destinationNumber + " initiated, callback #" + forwardingNumber + ", phone type " + phoneType + ", timeout " + callbackTimeout + "s.", m_username));
                }

                if (m_waitForCallback.WaitOne(callbackTimeout * 1000))
                {
                    if (!m_hasBeenCancelled)
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Google Voice Call callback received.", m_username));
                        return m_callbackCall.Answer(contentType, body, null, SIPDialogueTransferModesEnum.Default);
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Google Voice Call timed out waiting for callback.", m_username));
                    CancelCall();
                    return null;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GoogleVoiceCall SendCallRequest. " + excp.Message);
                throw;
            }
        }

        private void CancelCall()
        {
            try
            {
                if (!m_hasBeenCancelled)
                {
                    m_hasBeenCancelled = true;
                    m_waitForCallback.Set();

                    string callData = "outgoingNumber=undefined&forwardingNumber=undefined&_rnr_se=" + Uri.EscapeDataString(m_rnrKey);

                    // Build the call request.
                    HttpWebRequest cancelRequest = (HttpWebRequest)WebRequest.Create(CANCEL_CALL_URL);
                    cancelRequest.ConnectionGroupName = "cancel";
                    cancelRequest.CookieContainer = m_cookies;
                    cancelRequest.Method = "POST";
                    cancelRequest.ContentType = "application/x-www-form-urlencoded;charset=utf-8";
                    cancelRequest.ContentLength = callData.Length;
                    cancelRequest.GetRequestStream().Write(Encoding.UTF8.GetBytes(callData), 0, callData.Length);
                    cancelRequest.Timeout = HTTP_REQUEST_TIMEOUT * 1000;

                    HttpWebResponse response = (HttpWebResponse)cancelRequest.GetResponse();
                    HttpStatusCode responseStatus = response.StatusCode;
                    response.Close();
                    if (responseStatus != HttpStatusCode.OK)
                    {
                       logger.Warn("The GoogleVoiceCall cancel request failed with a " + responseStatus + " response.");
                    }
                    else
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Google Voice Call to " + m_destinationNumber + " was successfully cancelled.", m_username));
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GoogleVoiceCall CancelCall. " + excp.Message);
                throw;
            }
        }

        private bool MatchIncomingCall(ISIPServerUserAgent incomingCall)
        {
            try
            {
                if (incomingCall.SIPAccount.Owner != m_username)
                {
                    return false;
                }
                else if (m_clientCallCancelled)
                {
                    // If the call has been cancelled then don't match to avoid chance of a new incoming call matching a dead Google Voice call.
                    return false;
                }

                SIPHeader callHeader = incomingCall.CallRequest.Header;
                bool matchedCall = false;

                if (!m_fromURIUserRegexMatch.IsNullOrBlank())
                {
                    if (Regex.Match(callHeader.From.FromURI.User, m_fromURIUserRegexMatch).Success)
                    {
                        matchedCall = true;
                    }
                }
                else if (callHeader.UnknownHeaders.Contains("X-GoogleVoice: true") && callHeader.To.ToURI.User == m_forwardingNumber.Substring(1))
                {
                    matchedCall = true;
                }

                if (matchedCall)
                {
                    m_callbackCall = incomingCall;
                    m_callbackCall.SetOwner(m_username, m_adminMemberId);
                    m_waitForCallback.Set();
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GoogleVoiceCall MatchIncomingCall. " + excp.Message);
                return false;
            }
        }

        public void ClientCallTerminated(CallCancelCause cancelCause)
        {
            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "GoogleVoiceCall client call cancelled, " + cancelCause + ".", m_username));
            m_clientCallCancelled = true;
            CancelCall();
        }
    }
}
