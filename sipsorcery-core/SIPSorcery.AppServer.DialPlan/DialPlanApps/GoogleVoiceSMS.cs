//-----------------------------------------------------------------------------
// Filename: GoogleVoiceSMS.cs
//
// Description: A dial plan command that places HTTP request to initiate an SMS 
// through the Google Voice service.
// 
// History:
// 26 Aug 2010	    Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Ltd, Hobart, Tasmania, Australia
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Ltd. 
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
    public class GoogleVoiceSMS
    {
        private const string GOOGLE_COM_URL = "https://www.google.com";
        private const string PRE_LOGIN_URL = "https://www.google.com/accounts/ServiceLogin";
        //private const string LOGIN_URL = "https://www.google.com/accounts/ServiceLoginAuth?service=grandcentral";
        private const string LOGIN_URL = "https://accounts.google.com/accounts/ServiceLogin?service=grandcentral";
        private const string VOICE_HOME_URL = "https://www.google.com/voice";
        private const string SMS_SEND_URL = "https://www.google.com/voice/sms/send";
        private const int HTTP_REQUEST_TIMEOUT = 5;

        private static ILog logger = AppState.logger;
        private SIPMonitorLogDelegate Log_External;

        private string m_username;
        private string m_adminMemberId;

        private string m_destinationNumber;
        private CookieContainer m_cookies;
        private string m_rnrKey;

        public GoogleVoiceSMS(
            SIPMonitorLogDelegate logDelegate,
            string username,
            string adminMemberId)
        {
            Log_External = logDelegate;
            m_username = username;
            m_adminMemberId = adminMemberId;
        }

        public void SendSMS(string emailAddress, string password, string destinationNumber, string message)
        {
            try
            {
                m_destinationNumber = destinationNumber;

                m_cookies = new CookieContainer();
                m_rnrKey = Login(emailAddress, password);
                if (!m_rnrKey.IsNullOrBlank())
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call key " + m_rnrKey + " successfully retrieved for " + emailAddress + ", proceeding with SMS.", m_username));
                    SendSMS(destinationNumber, message);
                }
                else
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call key was not retrieved for " + emailAddress + " SMS cannot proceed.", m_username));
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GoogleVoiceSMS SendSMS. " + excp.Message);
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

                Match galxMatch = Regex.Match(galxResponseFromServer, @"name=""GALX""\s+?value=""(?<galxvalue>.*?)""");
                if (galxMatch.Success)
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "GALX key " + galxMatch.Result("${galxvalue}") + " successfully retrieved.", m_username));
                }
                else
                {
                    throw new ApplicationException("Could not find GALX key on your Google Voice pre-login page, SMS cannot proceed.");
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
                response.Close();

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
                    throw new ApplicationException("Could not find _rnr_se key on your Google Voice account page, SMS cannot proceed.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GoogleVoiceSMS Login. " + excp.Message);
                throw;
            }
        }

        private void SendSMS(string destinationNumber, string message)
        {
            try
            {
                string callData = "phoneNumber=" + Uri.EscapeDataString(destinationNumber) + "&text=" + Uri.EscapeDataString(message) +
                   "&_rnr_se=" + Uri.EscapeDataString(m_rnrKey);
                //logger.Debug("call data=" + callData + ".");

                // Build the call request.
                HttpWebRequest callRequest = (HttpWebRequest)WebRequest.Create(SMS_SEND_URL);
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
                    throw new ApplicationException("The SMS request failed with a " + responseStatus + " response.");
                }
                else
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Google Voice SMS to " + destinationNumber + " successfully sent.", m_username));
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GoogleVoiceSMS SendSMS. " + excp.Message);
                throw;
            }
        }
    }
}
