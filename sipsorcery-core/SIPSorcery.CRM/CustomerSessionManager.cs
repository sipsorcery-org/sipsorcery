// ============================================================================
// FileName: CustomerSessionManager.cs
//
// Description:
// Manages user sessions for authenticated users.
//
// Author(s):
// Aaron Clauson
//
// History:
// 20 May 2009	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2009 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Linq;
using System.Security.Principal;
using System.Text;
using SIPSorcery.Sys;
using SIPSorcery.Persistence;
using log4net;

namespace SIPSorcery.CRM
{
    public delegate CustomerSession AuthenticateCustomerDelegate(string username, string password, string ipAddress);
    public delegate CustomerSession AuthenticateTokenDelegate(string token);
    public delegate void ExpireTokenDelegate(string token);

    public class CustomerSessionManager
    {
        public const string CUSTOMERS_XML_FILENAME = "customers.xml";
        public const string CUSTOMER_SESSIONS_XML_FILENAME = "customersessions.xml";

        public const int SESSION_ID_STRING_LENGTH = 96;  // 384 bits of entropy.

        private static ILog logger = AppState.logger;

        private SIPAssetPersistor<Customer> m_customerPersistor;
        private SIPAssetPersistor<CustomerSession> m_customerSessionPersistor;

        public SIPAssetPersistor<Customer> CustomerPersistor
        {
            get { return m_customerPersistor; }
        }

        public CustomerSessionManager(StorageTypes storageType, string connectionString)
        {
            m_customerPersistor = SIPAssetPersistorFactory<Customer>.CreateSIPAssetPersistor(storageType, connectionString, CUSTOMERS_XML_FILENAME);
            m_customerSessionPersistor = SIPAssetPersistorFactory<CustomerSession>.CreateSIPAssetPersistor(storageType, connectionString, CUSTOMER_SESSIONS_XML_FILENAME);
        }

        public CustomerSessionManager(SIPAssetPersistor<Customer> customerPersistor, SIPAssetPersistor<CustomerSession> customerSessionPersistor)
        {
            m_customerPersistor = customerPersistor;
            m_customerSessionPersistor = customerSessionPersistor;
        }

        public CustomerSessionManager(SIPSorceryConfiguration sipSorceryConfig)
        {
            StorageTypes storageType = sipSorceryConfig.PersistenceStorageType;
            string connectionString = sipSorceryConfig.PersistenceConnStr;
            m_customerPersistor = SIPAssetPersistorFactory<Customer>.CreateSIPAssetPersistor(storageType, connectionString, CUSTOMERS_XML_FILENAME);
            m_customerSessionPersistor = SIPAssetPersistorFactory<CustomerSession>.CreateSIPAssetPersistor(storageType, connectionString, CUSTOMER_SESSIONS_XML_FILENAME);
        }

        public CustomerSession Authenticate(string username, string password, string ipAddress)
        {
            try
            {
                if (username.IsNullOrBlank() || password.IsNullOrBlank())
                {
                    logger.Debug("Login failed, either username or password was not specified.");
                    return null;
                }
                else
                {
                    // Don't do the password check via the database as different ones have different string case matching.
                    Customer customer = m_customerPersistor.Get(c => c.CustomerUsername == username);

                    if (customer != null && PasswordHash.Hash(password, customer.Salt) == customer.CustomerPassword)
                    {
                        if (!customer.EmailAddressConfirmed)
                        {
                            throw new ApplicationException("Your email address has not yet been confirmed.");
                        }
                        else if (customer.Suspended)
                        {
                            throw new ApplicationException("Your account is suspended.");
                        }
                        else
                        {
                            logger.Debug("Login successful for " + username + ".");

                            string sessionId = Crypto.GetRandomByteString(SESSION_ID_STRING_LENGTH / 2);
                            CustomerSession customerSession = new CustomerSession(Guid.NewGuid(), sessionId, customer.CustomerUsername, ipAddress);
                            m_customerSessionPersistor.Add(customerSession);
                            return customerSession;
                        }
                    }
                    else
                    {
                        logger.Debug("Login failed for " + username + ".");
                        return null;
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception Authenticate CustomerSessionManager. " + excp.Message);
                throw;
            }
        }

        public CustomerSession Authenticate(string sessionId)
        {
            try
            {
                CustomerSession customerSession = m_customerSessionPersistor.Get(s => s.SessionID == sessionId && !s.Expired);
                //CustomerSession customerSession = m_customerSessionPersistor.Get(s => s.Id == sessionId);

                if (customerSession != null)
                {
                    int sessionLengthMinutes = (int)DateTimeOffset.UtcNow.Subtract(customerSession.Inserted).TotalMinutes;
                    //logger.Debug("CustomerSession Inserted=" + customerSession.Inserted.ToString("o") + ", session length=" + sessionLengthMinutes + "mins.");
                    if (sessionLengthMinutes > customerSession.TimeLimitMinutes || sessionLengthMinutes > CustomerSession.MAX_SESSION_LIFETIME_MINUTES)
                    {
                        customerSession.Expired = true;
                        m_customerSessionPersistor.Update(customerSession);
                        return null;
                    }
                    else
                    {
                        //logger.Debug("Authentication token valid for " + sessionId + ".");
                        return customerSession;
                    }
                }
                else
                {
                    logger.Warn("Authentication token invalid for " + sessionId + ".");
                    return null;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception Authenticate CustomerSessionManager. " + excp.Message);
                throw;
            }
        }

        public CustomerSession CreateSwitchboardSession(string owner)
        {
            Customer customer = m_customerPersistor.Get(c => c.CustomerUsername == owner);

            if (customer != null)
            {
                if (!customer.EmailAddressConfirmed)
                {
                    throw new ApplicationException("Your email address has not yet been confirmed.");
                }
                else if (customer.Suspended)
                {
                    throw new ApplicationException("Your account is suspended.");
                }
                else
                {
                    logger.Debug("CreateSwitchboardSession successful for " + owner + ".");

                    string sessionId = Crypto.GetRandomByteString(SESSION_ID_STRING_LENGTH / 2);
                    CustomerSession customerSession = new CustomerSession(Guid.NewGuid(), sessionId, customer.CustomerUsername, null);
                    m_customerSessionPersistor.Add(customerSession);
                    return customerSession;
                }
            }
            else
            {
                logger.Debug("CreateSwitchboardSession failed for " + owner + ".");
                return null;
            }
        }

        public void ExpireToken(string sessionId)
        {
            try
            {
                CustomerSession customerSession = m_customerSessionPersistor.Get(s => s.SessionID == sessionId);
                if (customerSession != null)
                {
                    customerSession.Expired = true;
                    m_customerSessionPersistor.Update(customerSession);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ExpireToken CustomerSessionManager. " + excp.Message);
                throw;
            }
        }

        public void ExtendSession(string sessionId, int minutes)
        {
            try
            {
                CustomerSession customerSession = m_customerSessionPersistor.Get(s => s.SessionID == sessionId);
                if (customerSession != null)
                {
                    if (customerSession.TimeLimitMinutes >= CustomerSession.MAX_SESSION_LIFETIME_MINUTES)
                    {
                        throw new ApplicationException("The session lifetime cannot be extended beyind " + CustomerSession.MAX_SESSION_LIFETIME_MINUTES + " minutes.");
                    }
                    else
                    {
                        if (customerSession.TimeLimitMinutes + minutes > CustomerSession.MAX_SESSION_LIFETIME_MINUTES)
                        {
                            customerSession.TimeLimitMinutes = CustomerSession.MAX_SESSION_LIFETIME_MINUTES;
                        }
                        else
                        {
                            customerSession.TimeLimitMinutes += minutes;
                        }

                        m_customerSessionPersistor.Update(customerSession);
                    }
                }
                else
                {
                    throw new ApplicationException("The session ID that was requested to extend does not exist.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ExtendSession. " + excp.Message);
                throw;
            }
        }
    }
}
