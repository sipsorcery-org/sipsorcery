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
    }
}