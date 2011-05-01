//-----------------------------------------------------------------------------
// Filename: SIPSorceryAuthenticatedService.cs
//
// Description: This class servces as a base class for higher level services that
// require authentication.
// 
// History:
// 22 Feb 2010	Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), SIPSorcery Ltd, London, UK
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
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Activation;
using System.ServiceModel.Description;
using System.ServiceModel.Channels;
using System.Text.RegularExpressions;
using System.Web;
using SIPSorcery.CRM;
using SIPSorcery.Persistence;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorcery.Sys.Auth;
using log4net;

namespace SIPSorcery.Web.Services
{
    public class SIPSorceryAuthorisationService
    {
        private ILog logger = AppState.logger;

        private static readonly string m_authIDKey = ServiceAuthToken.AUTH_TOKEN_KEY;

        protected CustomerSessionManager CRMSessionManager;
        protected SIPAssetPersistor<Customer> CRMCustomerPersistor;

        public SIPSorceryAuthorisationService()
        {
            SIPSorceryConfiguration sipSorceryConfig = new SIPSorceryConfiguration();
            CRMSessionManager = new CustomerSessionManager(sipSorceryConfig);
            CRMCustomerPersistor = CRMSessionManager.CustomerPersistor;
        }

        public SIPSorceryAuthorisationService(CustomerSessionManager crmSessionManager)
        {
            CRMSessionManager = crmSessionManager;
            CRMCustomerPersistor = crmSessionManager.CustomerPersistor;
        }

        protected bool IsServiceAlive()
        {
            logger.Debug("IsAlive called from " + OperationContext.Current.Channel.RemoteAddress + ".");
            return true;
        }

        public string Authenticate(string username, string password)
        {
            logger.Debug("SIPSorceryAuthenticatedService Authenticate called for " + username + ".");

            if (username == null || username.Trim().Length == 0)
            {
                return null;
            }
            else
            {
                string ipAddress = null;

                if (OperationContext.Current != null)
                {
                    OperationContext context = OperationContext.Current;
                    MessageProperties properties = context.IncomingMessageProperties;
                    RemoteEndpointMessageProperty endpoint = properties[RemoteEndpointMessageProperty.Name] as RemoteEndpointMessageProperty;
                    if (endpoint != null)
                    {
                        ipAddress = endpoint.Address;
                    }
                }
                else if (HttpContext.Current != null)
                {
                    ipAddress = HttpContext.Current.Request.UserHostAddress;
                }

                CustomerSession customerSession = CRMSessionManager.Authenticate(username, password, ipAddress);
                if (customerSession != null)
                {
                    // If running in IIS add a cookie for javascript clients.
                    if (HttpContext.Current != null)
                    {
                        logger.Debug("Setting authid cookie for " + customerSession.CustomerUsername + ".");
                        HttpCookie authCookie = new HttpCookie(m_authIDKey, customerSession.SessionID);
                        authCookie.Secure = HttpContext.Current.Request.IsSecureConnection;
                        authCookie.HttpOnly = true;
                        authCookie.Expires = DateTime.Now.AddMinutes(customerSession.TimeLimitMinutes);
                        HttpContext.Current.Response.Cookies.Set(authCookie);
                    }

                    return customerSession.SessionID;
                }
                else
                {
                    return null;
                }
            }
        }

        protected Customer AuthoriseRequest()
        {
            try
            {
                string authId = ServiceAuthToken.GetAuthId();
                //logger.Debug("Authorising request for sessionid=" + authId + ".");

                if (!authId.IsNullOrBlank())
                {
                    CustomerSession customerSession = CRMSessionManager.Authenticate(authId);
                    if (customerSession == null)
                    {
                        logger.Warn("SIPSorceryAuthenticatedService AuthoriseRequest failed for " + authId + ".");
                        throw new UnauthorizedAccessException();
                    }
                    else
                    {
                        Customer customer = CRMCustomerPersistor.Get(c => c.CustomerUsername == customerSession.CustomerUsername);
                        return customer;
                    }
                }
                else
                {
                    string apiKey = ServiceAuthToken.GetAPIKey();

                    if (!apiKey.IsNullOrBlank())
                    {
                        Customer customer = CRMCustomerPersistor.Get(c => c.APIKey == apiKey);
                        return customer;
                    }
                    else
                    {
                        logger.Warn("SIPSorceryAuthenticatedService AuthoriseRequest failed no authid header.");
                        throw new UnauthorizedAccessException();
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception excp)
            {
                logger.Error("Exception AuthoriseRequest. " + excp.Message);
                throw new Exception("There was an exception authorising the request.");
            }
        }

        protected void ExpireSession()
        {
            try
            {
                Customer customer = AuthoriseRequest();

                logger.Debug("SIPSorceryAuthenticatedService ExpireSession called for " + customer.CustomerUsername + ".");

                CRMSessionManager.ExpireToken(ServiceAuthToken.GetAuthId());

                // If running in IIS remove the cookie.
                if (HttpContext.Current != null)
                {
                    HttpContext.Current.Request.Cookies.Remove(m_authIDKey);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // This exception will occur if the SIP Server agent is restarted and the client sends a previously valid token.
                //logger.Debug("An unauthorised exception was thrown in logout.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception ExpireSession. " + excp.Message);
            }
        }

        protected void ExtendExistingSession(int minutes)
        {
            try
            {
                Customer customer = AuthoriseRequest();

                logger.Debug("SIPSorceryAuthenticatedService ExtendExistingSession called for " + customer.CustomerUsername + " and " + minutes + " minutes.");
                if (HttpContext.Current != null)
                {
                    HttpCookie authIdCookie = HttpContext.Current.Request.Cookies[m_authIDKey];
                    authIdCookie.Expires = authIdCookie.Expires.AddMinutes(minutes);
                }
                CRMSessionManager.ExtendSession(ServiceAuthToken.GetAuthId(), minutes);
            }
            catch (Exception excp)
            {
                logger.Error("Exception ExtendExistingSession. " + excp.Message);
                throw;
            }
        }
    }
}