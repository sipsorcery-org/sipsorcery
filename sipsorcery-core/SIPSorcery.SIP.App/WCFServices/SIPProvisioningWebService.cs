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
    public class SIPProvisioningWebService : IProvisioningService {
        public const string AUTH_TOKEN_KEY = "authid";

        private ILog logger = AppState.GetLogger("provisioning");

        private SIPAssetPersistor<SIPAccount> SIPAccountPersistor;
        private SIPAssetPersistor<SIPDialPlan> DialPlanPersistor;
        private SIPAssetPersistor<SIPProvider> SIPProviderPersistor;
        private SIPAssetPersistor<SIPProviderBinding> SIPProviderBindingsPersistor;
        private SIPAssetPersistor<SIPRegistrarBinding> SIPRegistrarBindingsPersistor;
        private SIPAssetPersistor<SIPDialogueAsset> SIPDialoguePersistor;
        private SIPAssetPersistor<SIPCDRAsset> SIPCDRPersistor;
        private SIPAssetPersistor<Customer> CRMCustomerPersistor;
        private CustomerSessionManager CRMSessionManager;
        private SIPDomainManager SIPDomainManager;
        private SIPMonitorLogDelegate LogDelegate_External = (e) => { };

        public SIPProvisioningWebService() { }

        public SIPProvisioningWebService(
            SIPAssetPersistor<SIPAccount> sipAccountPersistor,
            SIPAssetPersistor<SIPDialPlan> sipDialPlanPersistor,
            SIPAssetPersistor<SIPProvider> sipProviderPersistor,
            SIPAssetPersistor<SIPProviderBinding> sipProviderBindingsPersistor,
            SIPAssetPersistor<SIPRegistrarBinding> sipRegistrarBindingsPersistor,
            SIPAssetPersistor<SIPDialogueAsset> sipDialoguePersistor,
            SIPAssetPersistor<SIPCDRAsset> sipCDRPersistor,
            CustomerSessionManager crmSessionManager,
            SIPDomainManager sipDomainManager,
            SIPMonitorLogDelegate log) {

            SIPAccountPersistor = sipAccountPersistor;
            DialPlanPersistor = sipDialPlanPersistor;
            SIPProviderPersistor = sipProviderPersistor;
            SIPProviderBindingsPersistor = sipProviderBindingsPersistor;
            SIPRegistrarBindingsPersistor = sipRegistrarBindingsPersistor;
            SIPDialoguePersistor = sipDialoguePersistor;
            SIPCDRPersistor = sipCDRPersistor;
            CRMCustomerPersistor = crmSessionManager.CustomerPersistor;
            CRMSessionManager = crmSessionManager;
            SIPDomainManager = sipDomainManager;
            LogDelegate_External = log;
        }

        private string GetAuthId() {
            return OperationContext.Current.IncomingMessageHeaders.GetHeader<string>(AUTH_TOKEN_KEY, "");
        }

        private Customer AuthoriseRequest() {
            try {
                string authId = GetAuthId();
                //logger.Debug("Authorising request for sessionid=" + authId + ".");

                if (authId != null) {
                    CustomerSession customerSession = CRMSessionManager.Authenticate(authId);
                    if (customerSession == null) {
                        logger.Warn("SIPProvisioningWebService AuthoriseRequest failed for " + authId + ".");
                        throw new UnauthorizedAccessException();
                    }
                    else {
                        Customer customer = CRMCustomerPersistor.Get(c => c.CustomerUsername == customerSession.CustomerUsername);
                        return customer;
                    }
                }
                else {
                    logger.Warn("SIPProvisioningWebService AuthoriseRequest failed no authid header.");
                    throw new UnauthorizedAccessException();
                }
            }
            catch (UnauthorizedAccessException) {
                throw;
            }
            catch (Exception excp) {
                logger.Error("Exception AuthoriseRequest. " + excp.Message);
                throw new Exception("There was an exception authorising the request.");
            }
        }

        private string GetAuthorisedWhereExpression(Customer customer, string whereExpression) {
            try {
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

                if (!whereExpression.IsNullOrBlank()) {
                    authorisedWhereExpression += " and " + whereExpression;
                }

                return authorisedWhereExpression;
            }
            catch (Exception excp) {
                logger.Error("Exception GetAuthorisedWhereExpression. " + excp.Message);
                throw new Exception("There was an exception constructing the authorisation filter for the request.");
            }
        }

        public bool IsAlive() {
            logger.Debug("IsAlive called from " + OperationContext.Current.Channel.RemoteAddress + ".");
            return true;
        }

        public void CreateCustomer(Customer customer) {
            try {
                // Check whether the username is already taken.
                Customer existingCustomer = CRMCustomerPersistor.Get(c => c.CustomerUsername.ToLower() == customer.CustomerUsername.ToLower());
                if (existingCustomer != null) {
                    throw new ApplicationException("The requested username is already in use please try a different one.");
                }

                CRMCustomerPersistor.Add(customer);
                logger.Debug("New customer record added for " + customer.CustomerUsername + ".");

                // Create a default dialplan.
                SIPDialPlan defaultDialPlan = new SIPDialPlan(customer.CustomerUsername, "default", null, "sys.Log(\"hello world\")\n", SIPDialPlanScriptTypesEnum.Ruby);
                DialPlanPersistor.Add(defaultDialPlan);
                logger.Debug("Default dialplan added for " + customer.CustomerUsername + ".");

                // Get default domain name.
                string defaultDomain = SIPDomainManager.GetDomain("local");

                // Create SIP account.
                if (SIPAccountPersistor.Get(s => s.SIPUsername == customer.CustomerUsername && s.SIPDomain == defaultDomain) == null) {
                    SIPAccount sipAccount = new SIPAccount(customer.CustomerUsername, defaultDomain, customer.CustomerUsername, customer.CustomerPassword, "default");
                    SIPAccountPersistor.Add(sipAccount);
                    logger.Debug("SIP account " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + " added for " + sipAccount.Owner + ".");
                }
                else {
                    int attempts = 0;
                    while (attempts < 10) {
                        string testUsername = customer.CustomerUsername + Crypto.GetRandomString(4);
                        if (SIPAccountPersistor.Get(s => s.SIPUsername == testUsername && s.SIPDomain == defaultDomain) == null) {
                            SIPAccount sipAccount = new SIPAccount(customer.CustomerUsername, defaultDomain, testUsername, customer.CustomerPassword, "default");
                            SIPAccountPersistor.Add(sipAccount);
                            logger.Debug("SIP account " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + " added for " + sipAccount.Owner + ".");
                            break;
                        }
                        else {
                            attempts++;
                        }
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception CreateNewCustomer. " + excp.Message);
                throw;
            }
        }

        public void DeleteCustomer(string customerUsername) {
            try {
                Customer customer = AuthoriseRequest();
                if (customer != null && customer.CustomerUsername == customerUsername) {
                    CRMCustomerPersistor.Delete(customer);
                    logger.Debug("Customer account " + customer.CustomerUsername + " successfully deleted.");
                }
                else {
                    logger.Warn("Unauthorised attempt to delete customer " + customerUsername + ".");
                }
            }
            catch (Exception excp) {
                logger.Error("Exception DeleteCustomer. " + excp.Message);
            }
        }

        public string Login(string username, string password) {
            logger.Debug("SIPProvisioningWebService Login called for " + username + ".");

            if (username == null || username.Trim().Length == 0) {
                return null;
            }
            else {
                string ipAddress = null;
                OperationContext context = OperationContext.Current;
                MessageProperties properties = context.IncomingMessageProperties;
                RemoteEndpointMessageProperty endpoint = properties[RemoteEndpointMessageProperty.Name] as RemoteEndpointMessageProperty;
                if (endpoint != null) {
                    ipAddress = endpoint.Address;
                }

                CustomerSession customerSession = CRMSessionManager.Authenticate(username, password, ipAddress);
                if (customerSession != null) {
                    return customerSession.Id;
                }
                else {
                    return null;
                }
            }
        }

        public void Logout() {
            try {
                Customer customer = AuthoriseRequest();

                logger.Debug("SIPProvisioningWebService Logout called for " + customer.CustomerUsername + ".");
                CRMSessionManager.ExpireToken(GetAuthId());

                // Fire a machine log event to disconnect the silverlight tcp socket.
                LogDelegate_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.Logout, customer.CustomerUsername, null, null));
            }
            catch (UnauthorizedAccessException) {
                // This exception will occur if the SIP Server agent is restarted and the client sends a previously valid token.
                //logger.Debug("An unauthorised exception was thrown in logout.");
            }
            catch (Exception excp) {
                logger.Error("Exception Logout. " + excp.Message);
            }
        }

        public List<SIPDomain> GetSIPDomains(string filterExpression, int offset, int count) {
            Customer customer = AuthoriseRequest();

            if (customer == null) {
                throw new ArgumentNullException("customer", "The customer cannot be empty when building authorised where expression.");
            }
            else {
                string authoriseExpression = "owner =\"" + customer.CustomerUsername + "\" or owner = null";
                //logger.Debug("SIPProvisioningWebService GetSIPDomains called for " + customer.CustomerUsername + " and where: " + authoriseExpression + ".");
                return SIPDomainManager.Get(DynamicExpression.ParseLambda<SIPDomain, bool>(authoriseExpression), offset, count);
            }
        }

        public int GetSIPAccountsCount(string whereExpression) {
            Customer customer = AuthoriseRequest();

            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetSIPAccountsCount called for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return SIPAccountPersistor.Count(DynamicExpression.ParseLambda<SIPAccount, bool>(authoriseExpression));
        }

        public List<SIPAccount> GetSIPAccounts(string whereExpression, int offset, int count) {
            Customer customer = AuthoriseRequest();

            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetSIPAccountscalled for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return SIPAccountPersistor.Get(DynamicExpression.ParseLambda<SIPAccount, bool>(authoriseExpression), "sipusername", offset, count);
        }

        public SIPAccount AddSIPAccount(SIPAccount sipAccount) {
            Customer customer = AuthoriseRequest();
            sipAccount.Owner = customer.CustomerUsername;

            string validationError = SIPAccount.ValidateAndClean(sipAccount);
            if (validationError != null) {
                logger.Warn("Validation error in AddSIPAccount for customer " + customer.CustomerUsername + ". " + validationError);
                throw new ApplicationException(validationError);
            }
            else {
                return SIPAccountPersistor.Add(sipAccount);
            }
        }

        public SIPAccount UpdateSIPAccount(SIPAccount sipAccount) {
            Customer customer = AuthoriseRequest();

            if (customer.AdminId != Customer.TOPLEVEL_ADMIN_ID && sipAccount.Owner != customer.CustomerUsername) {
                logger.Debug("Unauthorised attempt to update SIP account by user=" + customer.CustomerUsername + ", on account owned by=" + sipAccount.Owner + ".");
                throw new ApplicationException("You are not authorised to update the SIP Account.");
            }

            string validationError = SIPAccount.ValidateAndClean(sipAccount);
            if (validationError != null) {
                logger.Warn("Validation error in UpdateSIPAccount for customer " + customer.CustomerUsername + ". " + validationError);
                throw new ApplicationException(validationError);
            }
            else {
                return SIPAccountPersistor.Update(sipAccount);
            }
        }

        public SIPAccount DeleteSIPAccount(SIPAccount sipAccount) {
            Customer customer = AuthoriseRequest();

            if (customer.AdminId != Customer.TOPLEVEL_ADMIN_ID && sipAccount.Owner != customer.CustomerUsername) {
                throw new ApplicationException("You are not authorised to delete the SIP Account.");
            }

            SIPAccountPersistor.Delete(sipAccount);

            // Enables the caller to see which SIP account has been deleted.
            return sipAccount;
        }

        public int GetSIPRegistrarBindingsCount(string whereExpression) {
            Customer customer = AuthoriseRequest();
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetSIPRegistrarBindingsCount for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return SIPRegistrarBindingsPersistor.Count(DynamicExpression.ParseLambda<SIPRegistrarBinding, bool>(authoriseExpression));
        }

        public List<SIPRegistrarBinding> GetSIPRegistrarBindings(string whereExpression, int offset, int count) {
            Customer customer = AuthoriseRequest();
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetSIPRegistrarBindings for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return SIPRegistrarBindingsPersistor.Get(DynamicExpression.ParseLambda<SIPRegistrarBinding, bool>(authoriseExpression), "sipaccountname", offset, count);
        }

        public int GetSIPProvidersCount(string whereExpression) {
            Customer customer = AuthoriseRequest();
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetSIPProvidersCount for " + customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return SIPProviderPersistor.Count(DynamicExpression.ParseLambda<SIPProvider, bool>(authoriseExpression));
        }

        public List<SIPProvider> GetSIPProviders(string whereExpression, int offset, int count) {
            Customer customer = AuthoriseRequest();
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);

            //logger.Debug("SIPProvisioningWebService GetSIPProviders for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");
            return SIPProviderPersistor.Get(DynamicExpression.ParseLambda<SIPProvider, bool>(authoriseExpression), "providername", offset, count);
        }

        public SIPProvider AddSIPProvider(SIPProvider sipProvider) {
            Customer customer = AuthoriseRequest();
            sipProvider.Owner = customer.CustomerUsername;

            string validationError = SIPProvider.ValidateAndClean(sipProvider);
            if (validationError != null) {
                logger.Warn("Validation error in AddSIPProvider for customer " + customer.CustomerUsername + ". " + validationError);
                throw new ApplicationException(validationError);
            }
            else {
                return SIPProviderPersistor.Add(sipProvider);
            }
        }

        public SIPProvider UpdateSIPProvider(SIPProvider sipProvider) {
            Customer customer = AuthoriseRequest();

            if (customer.AdminId != Customer.TOPLEVEL_ADMIN_ID && sipProvider.Owner != customer.CustomerUsername) {
                throw new ApplicationException("You are not authorised to update the SIP Provider.");
            }

            string validationError = SIPProvider.ValidateAndClean(sipProvider);
            if (validationError != null) {
                logger.Warn("Validation error in UpdateSIPProvider for customer " + customer.CustomerUsername + ". " + validationError);
                throw new ApplicationException(validationError);
            }
            else {
                return SIPProviderPersistor.Update(sipProvider);
            }
        }

        public SIPProvider DeleteSIPProvider(SIPProvider sipProvider) {
            Customer customer = AuthoriseRequest();

            if (customer.AdminId != Customer.TOPLEVEL_ADMIN_ID && sipProvider.Owner != customer.CustomerUsername) {
                throw new ApplicationException("You are not authorised to delete the SIP Provider.");
            }

            //logger.Debug("DeleteSIPProvider, owner=" + sipProvider.Owner + ", providername=" + sipProvider.ProviderName + ".");
            SIPProviderPersistor.Delete(sipProvider);

            // Enables the caller to see which SIP Provider has been deleted.
            return sipProvider;
        }

        public int GetSIPProviderBindingsCount(string whereExpression) {
            Customer customer = AuthoriseRequest();
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);

            return SIPProviderBindingsPersistor.Count(DynamicExpression.ParseLambda<SIPProviderBinding, bool>(authoriseExpression));
        }

        public List<SIPProviderBinding> GetSIPProviderBindings(string whereExpression, int offset, int count) {
            Customer customer = AuthoriseRequest();
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);

            return SIPProviderBindingsPersistor.Get(DynamicExpression.ParseLambda<SIPProviderBinding, bool>(authoriseExpression), "providername", offset, count);
        }

        public int GetDialPlansCount(string whereExpression) {
            Customer customer = AuthoriseRequest();
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetDialPlansCount for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return DialPlanPersistor.Count(DynamicExpression.ParseLambda<SIPDialPlan, bool>(authoriseExpression));
        }

        public List<SIPDialPlan> GetDialPlans(string whereExpression, int offset, int count) {
            Customer customer = AuthoriseRequest();
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetDialPlans for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return DialPlanPersistor.Get(DynamicExpression.ParseLambda<SIPDialPlan, bool>(authoriseExpression), "dialplanname", offset, count);
        }

        public SIPDialPlan AddDialPlan(SIPDialPlan dialPlan) {
            Customer customer = AuthoriseRequest();

            dialPlan.Owner = customer.CustomerUsername;

            if (customer.AdminId != Customer.TOPLEVEL_ADMIN_ID) {
                dialPlan.MaxExecutionCount = SIPDialPlan.DEFAULT_MAXIMUM_EXECUTION_COUNT;
            }

            return DialPlanPersistor.Add(dialPlan);
        }

        public SIPDialPlan UpdateDialPlan(SIPDialPlan dialPlan) {
            Customer customer = AuthoriseRequest();

            if (customer.AdminId != Customer.TOPLEVEL_ADMIN_ID && dialPlan.Owner != customer.CustomerUsername) {
                throw new ApplicationException("You are not authorised to update the Dial Plan.");
            }

            return DialPlanPersistor.Update(dialPlan);
        }

        public SIPDialPlan DeleteDialPlan(SIPDialPlan dialPlan) {
            Customer customer = AuthoriseRequest();

            if (customer.AdminId != Customer.TOPLEVEL_ADMIN_ID && dialPlan.Owner != customer.CustomerUsername) {
                throw new ApplicationException("You are not authorised to delete the Dial Plan.");
            }

            DialPlanPersistor.Delete(dialPlan);

            // Enables the caller to see which dialplan has been deleted.
            return dialPlan;
        }

        public int GetCallsCount(string whereExpression) {
            Customer customer = AuthoriseRequest();

            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetCallsCount for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return SIPDialoguePersistor.Count(DynamicExpression.ParseLambda<SIPDialogueAsset, bool>(authoriseExpression));
        }

        public List<SIPDialogueAsset> GetCalls(string whereExpression, int offset, int count) {
            Customer customer = AuthoriseRequest();

            string authorisedExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetCalls for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return SIPDialoguePersistor.Get(DynamicExpression.ParseLambda<SIPDialogueAsset, bool>(authorisedExpression), null, offset, count);
        }

        public int GetCDRsCount(string whereExpression) {
            Customer customer = AuthoriseRequest();

            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetCDRsCount for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return SIPCDRPersistor.Count(DynamicExpression.ParseLambda<SIPCDRAsset, bool>(authoriseExpression));
        }

        public List<SIPCDRAsset> GetCDRs(string whereExpression, int offset, int count) {
            Customer customer = AuthoriseRequest();

            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetCDRs for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            return SIPCDRPersistor.Get(DynamicExpression.ParseLambda<SIPCDRAsset, bool>(authoriseExpression), "createdutc", offset, count);
        }
    }
}

