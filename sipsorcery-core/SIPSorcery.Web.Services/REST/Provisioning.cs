//-----------------------------------------------------------------------------
// Filename: Provisioning.cs
//
// Description: Provides a REST/JSON service implementation for a service to manipulate the resource
// entities exposed by sipsorcery.
// 
// History:
// 25 Oct 2011	Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2011 Aaron Clauson (aaron@sipsorcery.com), SIPSorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIPSorcery Pty. Ltd. 
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
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.ServiceModel.Web;
using System.Web;
using SIPSorcery.Entities;
using SIPSorcery.Sys;
using SIPSorcery.Sys.Auth;
using log4net;

namespace SIPSorcery.Web.Services
{
    public class Provisioning : IProvisioning
    {
        private const int DEFAULT_COUNT = 25;   // If a count isn't specified or it's invalid this value will be used.

        private static ILog logger = AppState.logger;

        private SIPSorceryService m_service = new SIPSorceryService();

        private Customer AuthoriseRequest()
        {
            try
            {
                string apiKey = ServiceAuthToken.GetAPIKey();

                if (!apiKey.IsNullOrBlank())
                {
                    Customer customer = m_service.GetCustomerForAPIKey(apiKey);
                    if (customer == null)
                    {
                        throw new ApplicationException("The " + ServiceAuthToken.API_KEY + " header value was not recognised as belonging to a valid account.");
                    }
                    else if (customer.Suspended)
                    {
                        throw new ApplicationException("Your account is suspended.");
                    }
                    else
                    {
                        return customer;
                    }
                }
                else
                {
                    throw new ApplicationException("No " + ServiceAuthToken.API_KEY + " header was found in the request.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception Provisioning.AuthoriseRequest. " + excp.Message);
                throw;
            }
        }

        public bool IsAlive()
        {
            return true;
        }

        public JSONResult<string> AddCustomer(CustomerJSON customer)
        {
            try
            {
                var reqCustomer = AuthoriseRequest();

                if (reqCustomer.AdminID != Customer.TOPLEVEL_ADMIN_ID)
                {
                    return new JSONResult<string>() { Success = false, Error = "Sorry you are not authorised to add new customers." };
                }
                else
                {
                    m_service.InsertCustomer(customer.ToCustomer());
                    var cust = m_service.GetCustomer(customer.Username);
                    return new JSONResult<string>() { Success = true, Result = cust.ID };
                }
            }
            catch (Exception excp)
            {
                return new JSONResult<string>() { Success = false, Error = excp.Message };
            }
        }

        public JSONResult<string> SetCustomerServiceLevel(string username, string serviceLevel, string renewalDate)
        {
            try
            {
                var reqCustomer = AuthoriseRequest();

                if (reqCustomer.AdminID != Customer.TOPLEVEL_ADMIN_ID)
                {
                    return new JSONResult<string>() { Success = false, Error = "Sorry you are not authorised to add set customer service levels." };
                }
                else
                {
                    CustomerServiceLevels newServiceLevel = (CustomerServiceLevels)Enum.Parse(typeof(CustomerServiceLevels), serviceLevel);
                    DateTimeOffset? newRenewalDate = (!renewalDate.IsNullOrBlank()) ? DateTimeOffset.Parse(renewalDate) : (DateTimeOffset?)null;
                    m_service.UpdateCustomerServiceLevel(username, newServiceLevel, newRenewalDate);
                    return new JSONResult<string>() { Success = true };
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SetCustomerServiceLevel. " + excp);
                return new JSONResult<string>() { Success = false, Error = excp.Message };
            }
        }

        public JSONResult<string> SetReadOnly(string username)
        {
            try
            {
                var reqCustomer = AuthoriseRequest();

                if (reqCustomer.AdminID != Customer.TOPLEVEL_ADMIN_ID)
                {
                    return new JSONResult<string>() { Success = false, Error = "Sorry you are not authorised to add set a customer's read only status." };
                }
                else
                {
                    m_service.SetAllProvidersAndDialPlansReadonly(username);
                    return new JSONResult<string>() { Success = true };
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SetReadOnly. " + excp);
                return new JSONResult<string>() { Success = false, Error = excp.Message };
            }
        }

        public List<SIPDomain> GetSIPDomains(string where, int offset, int count)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Retrieves a count of the user's matching SIP accounts.
        /// </summary>
        /// <param name="where">An optional dynamic LINQ where clause to apply when doing the count.</param>
        /// <returns>A count wrapped up to be JSON serialisable.</returns>
        public JSONResult<int> GetSIPAccountsCount(string where)
        {
            try
            {
                var customer = AuthoriseRequest();

                var result = m_service.GetSIPAccountsCount(customer.Name, where);

                return new JSONResult<int>() { Success = true, Result = result };
            }
            catch (Exception excp)
            {
                return new JSONResult<int>() { Success = false, Error = excp.Message };
            }
        }

        public JSONResult<List<SIPAccountJSON>> GetSIPAccounts(string where, int offset, int count)
        {
            try
            {
                var customer = AuthoriseRequest();

                if (count <= 0)
                {
                    count = DEFAULT_COUNT;
                }

                var result = from sipAccount in m_service.GetSIPAccounts(customer.Name, @where, offset, count)
                             select new SIPAccountJSON()
                             {
                                 AvatarURL = sipAccount.AvatarURL,
                                 DontMangleEnabled = sipAccount.DontMangleEnabled,
                                 ID = sipAccount.ID,
                                 InDialPlanName = sipAccount.InDialPlanName,
                                 IPAddressACL = sipAccount.IPAddressACL,
                                 IsIncomingOnly = sipAccount.IsIncomingOnly,
                                 IsSwitchboardEnabled = sipAccount.IsSwitchboardEnabled,
                                 IsUserDisabled = sipAccount.IsUserDisabled,
                                 NetworkID = sipAccount.NetworkID,
                                 OutDialPlanName = sipAccount.OutDialPlanName,
                                 SendNATKeepAlives = sipAccount.SendNATKeepAlives,
                                 SIPDomain = sipAccount.SIPDomain,
                                 SIPPassword = sipAccount.SIPPassword,
                                 SIPUsername = sipAccount.SIPUsername
                             };

                return new JSONResult<List<SIPAccountJSON>>() { Success = true, Result = result.ToList() };
            }
            catch (Exception excp)
            {
                return new JSONResult<List<SIPAccountJSON>>() { Success = false, Error = excp.Message };
            }
        }

        public JSONResult<string> AddSIPAccount(SIPAccountJSON sipAccount)
        {
            try
            {
                var customer = AuthoriseRequest();

                SIPAccount entitySIPAccount = sipAccount.ToSIPAccount();
                m_service.InsertSIPAccount(customer.Name, entitySIPAccount);

                return new JSONResult<string>() { Success = true, Result = entitySIPAccount.ID };
            }
            catch (Exception excp)
            {
                return new JSONResult<string>() { Success = false, Error = excp.Message };
            }
        }

        public JSONResult<string> UpdateSIPAccount(SIPAccountJSON sipAccount)
        {
            try
            {
                var customer = AuthoriseRequest();

                SIPAccount entitySIPAccount = sipAccount.ToSIPAccount();
                m_service.UpdateSIPAccount(customer.Name, entitySIPAccount);

                return new JSONResult<string>() { Success = true, Result = entitySIPAccount.ID };
            }
            catch (Exception excp)
            {
                return new JSONResult<string>() { Success = false, Error = excp.Message };
            }
        }

        /// <summary>
        /// Attempts to delete a SIP account that matches the specified ID.
        /// </summary>
        /// <returns>True if the account was deleted otherwise an error message.</returns>
        public JSONResult<bool> DeleteSIPAccount(string id)
        {
            try
            {
                var customer = AuthoriseRequest();

                m_service.DeleteSIPAccount(customer.Name, id);

                return new JSONResult<bool>() { Success = true, Result = true };
            }
            catch (Exception excp)
            {
                return new JSONResult<bool>() { Success = false, Error = excp.Message, Result = false };
            }
        }

        public int GetSIPAccountBindingsCount(string where)
        {
            throw new NotImplementedException();
        }

        public List<SIPRegistrarBinding> GetSIPAccountBindings(string where, int offset, int count)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Retrieves a count of the user's matching SIP providers.
        /// </summary>
        /// <param name="where">An optional dynamic LINQ where clause to apply when doing the count.</param>
        /// <returns>A count wrapped up to be JSON serialisable.</returns>
        public JSONResult<int> GetSIPProvidersCount(string where)
        {
            try
            {
                var customer = AuthoriseRequest();

                var result = m_service.GetSIPProvidersCount(customer.Name, where);

                return new JSONResult<int>() { Success = true, Result = result };
            }
            catch (Exception excp)
            {
                return new JSONResult<int>() { Success = false, Error = excp.Message };
            }
        }

        public JSONResult<List<SIPProviderJSON>> GetSIPProviders(string where, int offset, int count)
        {
            try
            {
                var customer = AuthoriseRequest();

                if (count <= 0)
                {
                    count = DEFAULT_COUNT;
                }

                var result = from sipProvider in m_service.GetSIPProviders(customer.Name, @where, offset, count)
                             select new SIPProviderJSON()
                             {
                                 ID = sipProvider.ID,
                                 ProviderName = sipProvider.ProviderName,
                                 ProviderUsername = sipProvider.ProviderUsername,
                                 ProviderPassword = sipProvider.ProviderPassword,
                                 ProviderServer = sipProvider.ProviderServer,
                                 ProviderAuthUsername = sipProvider.ProviderAuthUsername,
                                 ProviderOutboundProxy = sipProvider.ProviderOutboundProxy,
                                 ProviderType = sipProvider.ProviderType,
                                 ProviderFrom = sipProvider.ProviderFrom,
                                 CustomHeaders = sipProvider.CustomHeaders,
                                 RegisterContact = sipProvider.RegisterContact,
                                 RegisterExpiry = sipProvider.RegisterExpiry != null ? sipProvider.RegisterExpiry.Value : 0,
                                 RegisterServer = sipProvider.RegisterServer,
                                 RegisterRealm = sipProvider.RegisterRealm,
                                 GVCallbackNumber = sipProvider.GVCallbackNumber,
                                 GVCallbackPattern = sipProvider.GVCallbackPattern,
                                 GVCallbackType = sipProvider.GVCallbackType
                             };

                return new JSONResult<List<SIPProviderJSON>>() { Success = true, Result = result.ToList() };
            }
            catch (Exception excp)
            {
                return new JSONResult<List<SIPProviderJSON>>() { Success = false, Error = excp.Message };
            }
        }

        public JSONResult<string> AddSIPProvider(SIPProviderJSON sipProvider)
        {
            try
            {
                var customer = AuthoriseRequest();

                SIPProvider entitySIPProvider = sipProvider.ToSIPProvider();
                m_service.InsertSIPProvider(customer.Name, entitySIPProvider);

                return new JSONResult<string>() { Success = true, Result = entitySIPProvider.ID };
            }
            catch (Exception excp)
            {
                return new JSONResult<string>() { Success = false, Error = excp.Message };
            }
        }

        public JSONResult<string> UpdateSIPProvider(SIPProviderJSON sipProvider)
        {
            try
            {
                var customer = AuthoriseRequest();

                SIPProvider entitySIPProvider = sipProvider.ToSIPProvider();
                m_service.UpdateSIPProvider(customer.Name, entitySIPProvider);

                return new JSONResult<string>() { Success = true, Result = entitySIPProvider.ID };
            }
            catch (Exception excp)
            {
                return new JSONResult<string>() { Success = false, Error = excp.Message };
            }
        }

        /// <summary>
        /// Attempts to delete a SIP provider that matches the specified ID.
        /// </summary>
        /// <returns>True if the account was deleted otherwise an error message.</returns>
        public JSONResult<bool> DeleteSIPProvider(string id)
        {
            try
            {
                var customer = AuthoriseRequest();

                m_service.DeleteSIPProvider(customer.Name, id);

                return new JSONResult<bool>() { Success = true, Result = true };
            }
            catch (Exception excp)
            {
                return new JSONResult<bool>() { Success = false, Error = excp.Message, Result = false };
            }
        }

        public int GetSIPProviderBindingsCount(string where)
        {
            throw new NotImplementedException();
        }

        public List<SIPProviderBinding> GetSIPProviderBindings(string where, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public int GetDialPlansCount(string where)
        {
            throw new NotImplementedException();
        }

        public List<SIPDialPlan> GetDialPlans(string where, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public int GetCallsCount(string where)
        {
            throw new NotImplementedException();
        }

        public List<SIPDialogue> GetCalls(string where, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public int GetCDRsCount(string where)
        {
            throw new NotImplementedException();
        }

        public List<CDR> GetCDRs(string where, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }
}