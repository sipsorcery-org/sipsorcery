//-----------------------------------------------------------------------------
// Filename: SIPProvisioningWebService.cs
//
// Description: Web services that expose provisioning services for SIP assets such
// as SIPAccounts, SIPProivders etc. This web service deals with storing objects that need
// to be presisted as oppossed to the manager web service which deals with transient objects
// such as SIP acocunt bindings or registrations.
// 
// History:
// 25 Sep 2008	Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Linq.Dynamic;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Description;
using System.ServiceModel.Channels;
using System.Text;
using System.Data;
using System.IO;
using System.Text.RegularExpressions;
using System.Timers;
using System.Threading;
using System.Xml;
using SIPSorcery.CRM;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App
{
    [ServiceContract(Namespace = "http://www.sipsorcery.com")]
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
    public class SIPProvisioningWebService 
    {
        public const string AUTH_TOKEN_KEY = "authid";

        private ILog logger = log4net.LogManager.GetLogger("provisioning");

        public AuthenticateCustomerDelegate AuthenticateWebService_External;
        public AuthenticateTokenDelegate AuthenticateToken_External;
        public ExpireTokenDelegate ExpireToken_External;

        public SIPAssetPersistor<SIPAccount> SIPAccountPersistor;
        public SIPAssetPersistor<SIPDialPlan> DialPlanPersistor;
        public SIPAssetPersistor<SIPProvider> SIPProviderPersistor;
        public SIPAssetPersistor<SIPProviderBinding> SIPProviderBindingsPersistor;
        public SIPAssetPersistor<SIPDomain> SIPDomainPersistor;
        public SIPAssetPersistor<SIPRegistrarBinding> SIPRegistrarBindingsPersistor;
        public SIPAssetPersistor<SIPDialogueAsset> SIPDialoguePersistor;
        public SIPAssetPersistor<SIPCDRAsset> SIPCDRPersistor;
        public SIPAssetPersistor<Customer> CRMCustomerPersistor;

        public SIPProvisioningWebService()
        {}

        private CustomerSession AuthoriseRequest()
        {
            try
            {
                string authId = OperationContext.Current.IncomingMessageHeaders.GetHeader<string>(AUTH_TOKEN_KEY, "");
                //logger.Debug("Authorising request for sessionid=" + authId + ".");

                if (authId != null)
                {
                    CustomerSession customerSession = AuthenticateToken_External(authId);
                    if (customerSession == null)
                    {
                        logger.Warn("SIPProvisioningWebService AuthoriseRequest failed for " + authId + ".");
                        throw new UnauthorizedAccessException();
                    }
                    else
                    {
                        return customerSession;
                    }
                }
                else
                {
                    logger.Warn("SIPProvisioningWebService AuthoriseRequest failed no authid header.");
                    throw new UnauthorizedAccessException();
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

        private string GetAuthorisedWhereExpression(Customer customer, string whereExpression)
        {
            try
            {
                if (customer == null) {
                    throw new ArgumentNullException("customer", "The customer cannot be empty when building authorised where expression.");
                }

                string authorisedWhereExpression = "owner=\"" + customer.CustomerUsername + "\"";
                if (customer.AdminId == Customer.TOPLEVEL_ADMIN_ID) {
                    // This user is the top level administrator and has permission to view all system assets.
                    authorisedWhereExpression = "true";
                }
                else if (!customer.AdminId.IsNullOrBlank()) {
                    authorisedWhereExpression = 
                        "(owner=\"" + customer.CustomerUsername + "\" or adminmemberid=\"" + customer.AdminId + "\")";
                }
                
                //if (whereExpression != null)
                //{
                //    authorisedWhereExpression += " and " + whereExpression;
                //}

                return authorisedWhereExpression;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetAuthorisedWhereExpression. " + excp.Message);
                throw new Exception("There was an exception constructing the authorisation filter for the request.");
            }
        }

        [OperationContract]
        public bool IsAlive()
        {
            return true;
        }

        [OperationContract]
        public string Login(string username, string password)
        {
            logger.Debug("SIPProvisioningWebService Login called for " + username + ".");

            if (username == null || username.Trim().Length == 0)
            {
                return null;
            }
            else
            {
                CustomerSession customerSession = AuthenticateWebService_External(username, password);
                if (customerSession != null)
                {
                    return customerSession.SessionId.ToString();
                }
                else
                {
                    return null;
                }
            }
        }

        [OperationContract]
        public void Logout()
        {
            try
            {
                CustomerSession customerSession = AuthoriseRequest();
                Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);
                logger.Debug("SIPProvisioningWebService Logout called for " + customer.CustomerUsername + ".");
                ExpireToken_External(customerSession.SessionId.ToString());
            }
            catch (UnauthorizedAccessException)
            {
                // This exception will occur if the SIP Server agent is restarted and the client sends a previosly valid token.
                logger.Debug("An unauthorised exception was thrown in logout.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception Logout. " + excp.Message);
            }
        }

        [OperationContract]
        public List<SIPDomain> GetSIPDomains(string filterExpression, int offset, int count)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);

            if (customer == null) {
                throw new ArgumentNullException("customer", "The customer cannot be empty when building authorised where expression.");
            }
            else {
                string authoriseExpression = "owner =\"" + customer.CustomerUsername + "\"";
                logger.Debug("SIPProvisioningWebService GetSIPDomains called for " + customer.CustomerUsername + " and where: " + authoriseExpression + ".");

                return SIPDomainPersistor.Get(DynamicExpression.ParseLambda<SIPDomain, bool>(authoriseExpression), offset, count);
            }
        }

        [OperationContract]
        public int GetSIPAccountsCount(string whereExpression)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetSIPAccountsCount called for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return SIPAccountPersistor.Count(DynamicExpression.ParseLambda<SIPAccount, bool>(authoriseExpression));
        }

        [OperationContract]
        public List<SIPAccount> GetSIPAccounts(string whereExpression, int offset, int count)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetSIPAccountscalled for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return SIPAccountPersistor.Get(DynamicExpression.ParseLambda<SIPAccount, bool>(authoriseExpression), offset, count);
        }
        
        [OperationContract]
        public SIPAccount AddSIPAccount(SIPAccount sipAccount)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);
            sipAccount.Owner = customer.CustomerUsername;

            return SIPAccountPersistor.Add(sipAccount);
        }

        [OperationContract]
        public SIPAccount UpdateSIPAccount(SIPAccount sipAccount)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);

            if (sipAccount.Owner != customer.CustomerUsername)
            {
                logger.Debug("Unauthorised attempt to update SIP account by user=" + customer.CustomerUsername + ", on account owned by=" + sipAccount.Owner + ".");
                throw new UnauthorizedAccessException("You are not authorised to update the SIP Account.");
            }

            return SIPAccountPersistor.Update(sipAccount);
        }

        [OperationContract]
        public SIPAccount DeleteSIPAccount(SIPAccount sipAccount)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);

            if (sipAccount.Owner != customer.CustomerUsername)
            {
                throw new UnauthorizedAccessException("You are not authorised to delete the SIP Account.");
            }

            SIPAccountPersistor.Delete(sipAccount);

            // Enables the caller to see which SIP account has been deleted.
            return sipAccount;
        }

        [OperationContract]
        public int GetSIPRegistrarBindingsCount(string whereExpression)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetSIPRegistrarBindingsCount for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return SIPRegistrarBindingsPersistor.Count(DynamicExpression.ParseLambda<SIPRegistrarBinding, bool>(authoriseExpression));
        }

        [OperationContract]
        public List<SIPRegistrarBinding> GetSIPRegistrarBindings(string whereExpression, int offset, int count)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetSIPRegistrarBindings for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return SIPRegistrarBindingsPersistor.Get(DynamicExpression.ParseLambda<SIPRegistrarBinding, bool>(authoriseExpression), offset, count);
        }

        [OperationContract]
        public int GetSIPProvidersCount(string whereExpression)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetSIPProvidersCount for " + customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return SIPProviderPersistor.Count(DynamicExpression.ParseLambda<SIPProvider, bool>(authoriseExpression));
        }

        [OperationContract]
        public List<SIPProvider> GetSIPProviders(string whereExpression, int offset, int count)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            
            //logger.Debug("SIPProvisioningWebService GetSIPProviders for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");
            return SIPProviderPersistor.Get(DynamicExpression.ParseLambda<SIPProvider, bool>(authoriseExpression), offset, count);
        }

        [OperationContract]
        public SIPProvider AddSIPProvider(SIPProvider sipProvider)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);
            sipProvider.Owner = customer.CustomerUsername;

            //logger.Debug("AddSIPProvider, owner=" + sipProvider.Owner + ", providername=" + sipProvider.ProviderName + ".");
            return SIPProviderPersistor.Add(sipProvider);
        }

        [OperationContract]
        public SIPProvider UpdateSIPProvider(SIPProvider sipProvider)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);

            if (sipProvider.Owner != customer.CustomerUsername)
            {
                throw new UnauthorizedAccessException("You are not authorised to update the SIP Provider.");
            }

            //logger.Debug("UpdateSIPProvider, owner=" + sipProvider.Owner + ", providername=" + sipProvider.ProviderName + ".");
    
            SIPProvider updatedProvider =  SIPProviderPersistor.Update(sipProvider);

            return updatedProvider;
        }

        [OperationContract]
        public SIPProvider DeleteSIPProvider(SIPProvider sipProvider)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);

            if (sipProvider.Owner != customer.CustomerUsername)
            {
                throw new UnauthorizedAccessException("You are not authorised to delete the SIP Provider.");
            }

            //logger.Debug("DeleteSIPProvider, owner=" + sipProvider.Owner + ", providername=" + sipProvider.ProviderName + ".");
            SIPProviderPersistor.Delete(sipProvider);

            // Enables the caller to see which SIP Provider has been deleted.
            return sipProvider;
        }

        [OperationContract]
        public int GetSIPProviderBindingsCount(string whereExpression) {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
 
            return SIPProviderBindingsPersistor.Count(DynamicExpression.ParseLambda<SIPProviderBinding, bool>(authoriseExpression));
        }

        [OperationContract]
        public List<SIPProviderBinding> GetSIPProviderBindings(string whereExpression, int offset, int count) {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);

            return SIPProviderBindingsPersistor.Get(DynamicExpression.ParseLambda<SIPProviderBinding, bool>(authoriseExpression), offset, count);
        }

        [OperationContract]
        public int GetDialPlansCount(string whereExpression)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetDialPlansCount for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return DialPlanPersistor.Count(DynamicExpression.ParseLambda<SIPDialPlan, bool>(authoriseExpression));
        }

        [OperationContract]
        public List<SIPDialPlan> GetDialPlans(string whereExpression, int offset, int count)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetDialPlans for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return DialPlanPersistor.Get(DynamicExpression.ParseLambda<SIPDialPlan, bool>(authoriseExpression), offset, count);
        }

        [OperationContract]
        public SIPDialPlan AddDialPlan(SIPDialPlan dialPlan)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);

            dialPlan.Owner = customer.CustomerUsername;

            return DialPlanPersistor.Add(dialPlan);
        }

        [OperationContract]
        public SIPDialPlan UpdateDialPlan(SIPDialPlan dialPlan)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);

            if (dialPlan.Owner != customer.CustomerUsername)
            {
                throw new UnauthorizedAccessException("You are not authorised to update the Dial Plan.");
            }

            return DialPlanPersistor.Update(dialPlan);
        }

        [OperationContract]
        public SIPDialPlan DeleteDialPlan(SIPDialPlan dialPlan)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);

            if (dialPlan.Owner != customer.CustomerUsername)
            {
                throw new UnauthorizedAccessException("You are not authorised to delete the Dial Plan.");
            }

            DialPlanPersistor.Delete(dialPlan);

            // Enables the caller to see which dialplan has been deleted.
            return dialPlan;
        }

        [OperationContract]
        public int GetCallsCount(string whereExpression)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);

            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetCallsCount for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return SIPDialoguePersistor.Count(DynamicExpression.ParseLambda<SIPDialogueAsset, bool>(authoriseExpression));
        }

        [OperationContract]
        public List<SIPDialogueAsset> GetCalls(string whereExpression, int offset, int count)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);

            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetCalls for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return SIPDialoguePersistor.Get(DynamicExpression.ParseLambda<SIPDialogueAsset, bool>(authoriseExpression), offset, count);
        }

        [OperationContract]
        public int GetCDRsCount(string whereExpression)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);

            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetCDRsCount for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return SIPCDRPersistor.Count(DynamicExpression.ParseLambda<SIPCDRAsset, bool>(authoriseExpression));
        }

        [OperationContract]
        public List<SIPCDRAsset> GetCDRs(string whereExpression, int offset, int count)
        {
            CustomerSession customerSession = AuthoriseRequest();
            Customer customer = CRMCustomerPersistor.Get(customerSession.CustomerId);

            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetCDRs for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return SIPCDRPersistor.Get(DynamicExpression.ParseLambda<SIPCDRAsset, bool>(authoriseExpression), offset, count);
        }
    }
}

