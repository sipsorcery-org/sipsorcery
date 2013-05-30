//-----------------------------------------------------------------------------
// Filename: Accountinging.cs
//
// Description: Provides a REST/JSON service implementation for accounting services and 
// customer account management.
// 
// History:
// 25 Sep 2012	Aaron Clauson	    Created.
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
using System.Text.RegularExpressions;
using System.Web;
using SIPSorcery.Entities;
using SIPSorcery.Sys;
using SIPSorcery.Sys.Auth;
using log4net;

namespace SIPSorcery.Web.Services
{
    public class Accounting : IAccounting
    {
        private const int DEFAULT_COUNT = 25;   // If a count isn't specified or it's invalid this value will be used.

        private static ILog logger = AppState.logger;

        private SIPSorceryService m_service = new SIPSorceryService();

        internal CustomerAccountDataLayer _customerAccountDataLayer = new CustomerAccountDataLayer();
        internal RateDataLayer _rateDataLayer = new RateDataLayer();

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
                    else if (!(customer.ServiceLevel == CustomerServiceLevels.Professional.ToString() || customer.ServiceLevel == CustomerServiceLevels.Gold.ToString()))
                    {
                         throw new ApplicationException("The requested method is only available for Professional accounts.");
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

        #region Customer Accounts.

        public bool VerifyCustomerAccount(string accountNumber, int pin)
        {
            var customer = AuthoriseRequest();

            logger.Debug("VerifyCustomerAccount owner=" + customer.Name + ", accountNumber=" + accountNumber + ".");

            accountNumber = Regex.Replace(accountNumber, @"^\D", String.Empty);

            var customerAccount = _customerAccountDataLayer.Get(customer.Name, accountNumber);

            if (customerAccount != null && customerAccount.PIN == pin)
            {
                return true;
            }
            else
            {
                if (customerAccount == null)
                {
                    logger.Warn("The customer account could not be found for owner " + customer.Name + " and account number " + accountNumber + ".");
                }
                else if (customerAccount.PIN != pin)
                {
                    logger.Warn("The customer account PIN did not match for owner " + customer.Name + " and account number " + accountNumber + ".");
                }

                return false;
            }
        }

        public bool DoesAccountNumberExist(string accountNumber)
        {
            var customer = AuthoriseRequest();

            logger.Debug("DoesAccountNumberExist owner=" + customer.Name + ", accountNumber=" + accountNumber + ".");

            accountNumber = Regex.Replace(accountNumber, @"^\D", String.Empty);

            var customerAccount = _customerAccountDataLayer.Get(customer.Name, accountNumber);

            if (customerAccount != null)
            {
                logger.Debug("Customer account number exists for owner " + customer.Name + " and account number " + accountNumber + ".");
                return true;
            }
            else
            {
                logger.Debug("Customer account number does not exist for owner " + customer.Name + " and account number " + accountNumber + ".");
                return false;
            }
        }

        public JSONResult<int> GetCustomerAccountsCount(string where)
        {
            try
            {
                var customer = AuthoriseRequest();

                var count = m_service.GetCustomerAccountsCount(customer.Name, where);

                return new JSONResult<int>() { Success = true, Result = count };
            }
            catch (Exception excp)
            {
                return new JSONResult<int>() { Success = false, Error = excp.Message };
            }
        }

        public JSONResult<List<CustomerAccountJSON>> GetCustomerAccounts(string where, int offset, int count)
        {
            try
            {
                var customer = AuthoriseRequest();

                if (count <= 0)
                {
                    count = DEFAULT_COUNT;
                }

                var result = from account in m_service.GetCustomerAccounts(customer.Name, @where, offset, count)
                             select new CustomerAccountJSON()
                             {
                                 ID = account.ID,
                                 Inserted = account.Inserted,
                                 AccountCode = account.AccountCode,
                                 AccountName = account.AccountName,
                                 AccountNumber = account.AccountNumber,
                                 Credit = account.Credit,
                                 PIN = account.PIN
                             };

                return new JSONResult<List<CustomerAccountJSON>>() { Success = true, Result = result.ToList() };
            }
            catch (Exception excp)
            {
                return new JSONResult<List<CustomerAccountJSON>>() { Success = false, Error = excp.Message };
            }
        }

        public JSONResult<bool> DeleteCustomerAccount(string id)
        {
            try
            {
                var customer = AuthoriseRequest();

                m_service.DeleteCustomerAccount(customer.Name, id);

                return new JSONResult<bool>() { Success = true, Result = true };
            }
            catch (Exception excp)
            {
                return new JSONResult<bool>() { Success = false, Error = excp.Message, Result = false };
            }
        }

        public JSONResult<string> AddCustomerAccount(CustomerAccountJSON customerAccount)
        {
            try
            {
                var customer = AuthoriseRequest();

                CustomerAccount entityCustomerAccount = customerAccount.ToCustomerAccount();
                m_service.InsertCustomerAccount(customer.Name, entityCustomerAccount);

                return new JSONResult<string>() { Success = true, Result = entityCustomerAccount.ID };
            }
            catch (Exception excp)
            {
                return new JSONResult<string>() { Success = false, Error = excp.Message };
            }
        }

        public JSONResult<string> UpdateCustomerAccount(CustomerAccountJSON customerAccount)
        {
            try
            {
                var customer = AuthoriseRequest();

                CustomerAccount entityCustomerAccount = customerAccount.ToCustomerAccount();
                m_service.UpdateCustomerAccount(customer.Name, entityCustomerAccount);

                return new JSONResult<string>() { Success = true, Result = entityCustomerAccount.ID };
            }
            catch (Exception excp)
            {
                return new JSONResult<string>() { Success = false, Error = excp.Message };
            }
        }

        #endregion

        #region Rates.

        public JSONResult<int> GetRatesCount(string where)
        {
            try
            {
                var customer = AuthoriseRequest();

                var count = m_service.GetRatesCount(customer.Name, where);

                return new JSONResult<int>() { Success = true, Result = count };
            }
            catch (Exception excp)
            {
                return new JSONResult<int>() { Success = false, Error = excp.Message };
            }
        }

        public JSONResult<List<RateJSON>> GetRates(string where, int offset, int count)
        {
            try
            {
                var customer = AuthoriseRequest();

                if (count <= 0)
                {
                    count = DEFAULT_COUNT;
                }

                var result = from dialPlan in m_service.GetRates(customer.Name, @where, offset, count)
                             select new RateJSON()
                             {
                                 ID = dialPlan.ID,
                                 Inserted = dialPlan.Inserted,
                                 Description = dialPlan.Description,
                                 Prefix = dialPlan.Prefix,
                                 Rate = dialPlan.Rate1,
                                 RateCode = dialPlan.RateCode,
                                 SetupCost = dialPlan.SetupCost,
                                 IncrementSeconds = dialPlan.IncrementSeconds
                             };

                return new JSONResult<List<RateJSON>>() { Success = true, Result = result.ToList() };
            }
            catch (Exception excp)
            {
                return new JSONResult<List<RateJSON>>() { Success = false, Error = excp.Message };
            }
        }

        public JSONResult<bool> DeleteRate(string id)
        {
            try
            {
                var customer = AuthoriseRequest();

                m_service.DeleteRate(customer.Name, id);

                return new JSONResult<bool>() { Success = true, Result = true };
            }
            catch (Exception excp)
            {
                return new JSONResult<bool>() { Success = false, Error = excp.Message, Result = false };
            }
        }

        public JSONResult<string> AddRate(RateJSON rate)
        {
            try
            {
                var customer = AuthoriseRequest();

                Rate entityRate = rate.ToRate();
                m_service.InsertRate(customer.Name, entityRate);

                return new JSONResult<string>() { Success = true, Result = entityRate.ID };
            }
            catch (Exception excp)
            {
                return new JSONResult<string>() { Success = false, Error = excp.Message };
            }
        }

        public JSONResult<string> UpdateRate(RateJSON rate)
        {
            try
            {
                var customer = AuthoriseRequest();

                Rate entityRate = rate.ToRate();
                m_service.UpdateRate(customer.Name, entityRate);

                return new JSONResult<string>() { Success = true, Result = entityRate.ID };
            }
            catch (Exception excp)
            {
                return new JSONResult<string>() { Success = false, Error = excp.Message };
            }
        }

        #endregion
    }
}