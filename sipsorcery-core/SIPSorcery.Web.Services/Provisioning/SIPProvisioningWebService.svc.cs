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
using System.ServiceModel.Activation;
using System.ServiceModel.Description;
using System.ServiceModel.Channels;
using System.Text;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Timers;
using System.Threading;
using System.Web;
using System.Xml;
using SIPSorcery.CRM;
using SIPSorcery.Persistence;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Web.Services
{
    [AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Allowed)]
    public class SIPProvisioningWebService : SIPSorceryAuthorisationService, IProvisioningServiceREST, IProvisioningService
    {
        private const string NEW_ACCOUNT_EMAIL_FROM_ADDRESS = "admin@sipsorcery.com";
        private const string NEW_ACCOUNT_EMAIL_SUBJECT = "SIP Sorcery Account Confirmation";

        private static string m_customerConfirmLink = AppState.GetConfigSetting("CustomerConfirmLink");
        private static string m_providerRegistrationsDisabled = AppState.GetConfigSetting("ProviderRegistrationsDisabled");

        private const string NEW_ACCOUNT_EMAIL_BODY =
            "Hi {0},\r\n\r\n" +
            "This is your automated SIP Sorcery new account confirmation email.\r\n\r\n" +
            "To confirm your account please visit the link below. If you did not request this email please ignore it.\r\n\r\n" +
            "{1}?id={2}\r\n\r\n" +
            "Regards,\r\n\r\n" +
            "SIP Sorcery";

        private ILog logger = AppState.GetLogger("provisioning");

        private SIPSorcery.Entities.SIPSorceryService m_service = new SIPSorcery.Entities.SIPSorceryService();

        private SIPAssetPersistor<SIPAccount> SIPAccountPersistor;
        private SIPAssetPersistor<SIPDialPlan> DialPlanPersistor;
        private SIPAssetPersistor<SIPProvider> SIPProviderPersistor;
        private SIPAssetPersistor<SIPProviderBinding> SIPProviderBindingsPersistor;
        private SIPAssetPersistor<SIPRegistrarBinding> SIPRegistrarBindingsPersistor;
        private SIPAssetPersistor<SIPDialogueAsset> SIPDialoguePersistor;
        private SIPAssetPersistor<SIPCDRAsset> SIPCDRPersistor;
        private SIPDomainManager SIPDomainManager;
        private SIPMonitorLogDelegate LogDelegate_External = (e) => { };

        private int m_newCustomersAllowedLimit;
        private bool m_inviteCodeRequired;
        private bool m_providerRegDisabled;

        public SIPProvisioningWebService()
        { }

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
            SIPMonitorLogDelegate log,
            int newCustomersAllowedLimit,
            bool inviteCodeRequired) :
            base(crmSessionManager)
        {
            SIPAccountPersistor = sipAccountPersistor;
            DialPlanPersistor = sipDialPlanPersistor;
            SIPProviderPersistor = sipProviderPersistor;
            SIPProviderBindingsPersistor = sipProviderBindingsPersistor;
            SIPRegistrarBindingsPersistor = sipRegistrarBindingsPersistor;
            SIPDialoguePersistor = sipDialoguePersistor;
            SIPCDRPersistor = sipCDRPersistor;
            SIPDomainManager = sipDomainManager;
            LogDelegate_External = log;
            m_newCustomersAllowedLimit = newCustomersAllowedLimit;
            m_inviteCodeRequired = inviteCodeRequired;

            if (!String.IsNullOrEmpty(m_providerRegistrationsDisabled))
            {
                Boolean.TryParse(m_providerRegistrationsDisabled, out m_providerRegDisabled);
            }

            //SIPSorcery.Entities.Services.SIPEntitiesDomainService domainSvc = new Entities.Services.SIPEntitiesDomainService();
        }

        private string GetAuthorisedWhereExpression(Customer customer, string whereExpression)
        {
            try
            {
                if (customer == null)
                {
                    throw new ArgumentNullException("customer", "The customer cannot be empty when building authorised where expression.");
                }

                if (customer.AdminId == Customer.TOPLEVEL_ADMIN_ID)
                {
                    // This user is the top level administrator and has permission to view all system assets.
                    return whereExpression;
                }
                else
                {
                    string authorisedWhereExpression = "owner=\"" + customer.CustomerUsername + "\"";

                    if (!customer.AdminId.IsNullOrBlank())
                    {
                        authorisedWhereExpression =
                            "(owner=\"" + customer.CustomerUsername + "\" or adminmemberid=\"" + customer.AdminId + "\")";
                    }

                    if (!whereExpression.IsNullOrBlank())
                    {
                        authorisedWhereExpression += " and " + whereExpression;
                    }

                    return authorisedWhereExpression;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetAuthorisedWhereExpression. " + excp.Message);
                throw new Exception("There was an exception constructing the authorisation filter for the request.");
            }
        }

        public bool IsAlive()
        {
            return base.IsServiceAlive();
        }

        public void TestException()
        {
            throw new ApplicationException("Test exception message " + DateTime.UtcNow.ToString("o") + ".");
        }

        public string Login(string username, string password)
        {
            return base.Authenticate(username, password);
        }

        public void ExtendSession(int minutes)
        {
            base.ExtendExistingSession(minutes);
        }

        public void Logout()
        {
            base.ExpireSession();
        }

        public bool AreNewAccountsEnabled()
        {
            logger.Debug("AreNewAccountsEnabled called from " + OperationContext.Current.Channel.RemoteAddress + ".");
            return m_newCustomersAllowedLimit == 0 || CRMCustomerPersistor.Count(c => !c.Suspended) < m_newCustomersAllowedLimit;
        }

        public void CreateCustomer(Customer customer)
        {
            try
            {
                if (m_inviteCodeRequired && customer.InviteCode == null)
                {
                    throw new ApplicationException("Sorry new account creations currently require an invite code, please see http://sipsorcery.wordpress.com/new-accounts/.");
                }
                else if (m_newCustomersAllowedLimit != 0 && CRMCustomerPersistor.Count(null) >= m_newCustomersAllowedLimit)
                {
                    // Check whether the number of customers is within the allowed limit.
                    throw new ApplicationException("Sorry new account creations are currently disabled, please see http://sipsorcery.wordpress.com/new-accounts/.");
                }
                else
                {
                    // Check whether the username is already taken.
                    customer.CustomerUsername = customer.CustomerUsername.ToLower();
                    Customer existingCustomer = CRMCustomerPersistor.Get(c => c.CustomerUsername == customer.CustomerUsername);
                    if (existingCustomer != null)
                    {
                        throw new ApplicationException("The requested username is already in use please try a different one.");
                    }

                    // Check whether the email address is already taken.
                    customer.EmailAddress = customer.EmailAddress.ToLower();
                    existingCustomer = CRMCustomerPersistor.Get(c => c.EmailAddress == customer.EmailAddress);
                    if (existingCustomer != null)
                    {
                        throw new ApplicationException("The email address is already associated with an account.");
                    }

                    string validationError = Customer.ValidateAndClean(customer);
                    if (validationError != null)
                    {
                        throw new ApplicationException(validationError);
                    }

                    customer.MaxExecutionCount = Customer.DEFAULT_MAXIMUM_EXECUTION_COUNT;
                    customer.APIKey = Crypto.GetRandomByteString(Customer.API_KEY_LENGTH / 2);

                    CRMCustomerPersistor.Add(customer);
                    logger.Debug("New customer record added for " + customer.CustomerUsername + ".");

                    // Create a default dialplan.
                    SIPDialPlan defaultDialPlan = new SIPDialPlan(customer.CustomerUsername, "default", null, "sys.Log(\"hello world\")\n", SIPDialPlanScriptTypesEnum.Ruby);
                    DialPlanPersistor.Add(defaultDialPlan);
                    logger.Debug("Default dialplan added for " + customer.CustomerUsername + ".");

                    // Get default domain name.
                    string defaultDomain = SIPDomainManager.GetDomain("local", true);

                    // Create SIP account.
                    if (SIPAccountPersistor.Get(s => s.SIPUsername == customer.CustomerUsername && s.SIPDomain == defaultDomain) == null)
                    {
                        SIPAccount sipAccount = new SIPAccount(customer.CustomerUsername, defaultDomain, customer.CustomerUsername, customer.CustomerPassword, "default");
                        SIPAccountPersistor.Add(sipAccount);
                        logger.Debug("SIP account " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + " added for " + sipAccount.Owner + ".");
                    }
                    else
                    {
                        int attempts = 0;
                        while (attempts < 10)
                        {
                            string testUsername = customer.CustomerUsername + Crypto.GetRandomString(4);
                            if (SIPAccountPersistor.Get(s => s.SIPUsername == testUsername && s.SIPDomain == defaultDomain) == null)
                            {
                                SIPAccount sipAccount = new SIPAccount(customer.CustomerUsername, defaultDomain, testUsername, customer.CustomerPassword, "default");
                                SIPAccountPersistor.Add(sipAccount);
                                logger.Debug("SIP account " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + " added for " + sipAccount.Owner + ".");
                                break;
                            }
                            else
                            {
                                attempts++;
                            }
                        }
                    }

                    if (!m_customerConfirmLink.IsNullOrBlank())
                    {
                        logger.Debug("Sending new account confirmation email to " + customer.EmailAddress + ".");
                        SIPSorcerySMTP.SendEmail(customer.EmailAddress, NEW_ACCOUNT_EMAIL_FROM_ADDRESS, NEW_ACCOUNT_EMAIL_SUBJECT, String.Format(NEW_ACCOUNT_EMAIL_BODY, customer.FirstName, m_customerConfirmLink, customer.Id));
                    }
                    else
                    {
                        logger.Debug("Customer confirmation email was not sent as no confirmation link has been set.");
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception CreateNewCustomer. " + excp.Message);
                throw;
            }
        }

        public void DeleteCustomer(string customerUsername)
        {
            try
            {
                Customer customer = AuthoriseRequest();
                if (customer != null && customer.CustomerUsername == customerUsername)
                {
                    CRMCustomerPersistor.Delete(customer);
                    logger.Debug("Customer account " + customer.CustomerUsername + " successfully deleted.");
                }
                else
                {
                    logger.Warn("Unauthorised attempt to delete customer " + customerUsername + ".");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception DeleteCustomer. " + excp.Message);
            }
        }

        public Customer GetCustomer(string username)
        {
            Customer customer = AuthoriseRequest();

            if (customer.CustomerUsername == username)
            {
                return customer;
            }
            else
            {
                throw new ApplicationException("You are not authorised to retrieve customer for username " + username + ".");
            }
        }

        public int GetTimeZoneOffsetMinutes()
        {
            try
            {
                Customer customer = AuthoriseRequest();

                if (!customer.TimeZone.IsNullOrBlank())
                {
                    foreach (TimeZoneInfo timezone in TimeZoneInfo.GetSystemTimeZones())
                    {
                        if (timezone.DisplayName == customer.TimeZone)
                        {
                            //return (int)timezone.BaseUtcOffset.TotalMinutes;
                            return (int)timezone.GetUtcOffset(DateTimeOffset.UtcNow).TotalMinutes;
                        }
                    }
                }

                return 0;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetTimeZoneOffsetMinutes. " + excp.Message);
                return 0;
            }
        }

        public void UpdateCustomer(Customer updatedCustomer)
        {
            Customer customer = AuthoriseRequest();

            if (customer.CustomerUsername == updatedCustomer.CustomerUsername)
            {
                logger.Debug("Updating customer details for " + customer.CustomerUsername);
                customer.FirstName = updatedCustomer.FirstName;
                customer.LastName = updatedCustomer.LastName;
                customer.EmailAddress = updatedCustomer.EmailAddress;
                customer.SecurityQuestion = updatedCustomer.SecurityQuestion;
                customer.SecurityAnswer = updatedCustomer.SecurityAnswer;
                customer.City = updatedCustomer.City;
                customer.Country = updatedCustomer.Country;
                customer.WebSite = updatedCustomer.WebSite;
                customer.TimeZone = updatedCustomer.TimeZone;

                string validationError = Customer.ValidateAndClean(customer);
                if (validationError != null)
                {
                    throw new ApplicationException(validationError);
                }

                CRMCustomerPersistor.Update(customer);
            }
            else
            {
                throw new ApplicationException("You are not authorised to update customer for username " + updatedCustomer.CustomerUsername + ".");
            }
        }

        public void UpdateCustomerPassword(string username, string oldPassword, string newPassword)
        {
            Customer customer = AuthoriseRequest();

            if (customer.CustomerUsername == username)
            {
                if (PasswordHash.Hash(oldPassword, customer.Salt) != customer.CustomerPassword)
                {
                    throw new ApplicationException("The existing password did not match when attempting a password update.");
                }
                else
                {
                    logger.Debug("Updating customer password for " + customer.CustomerUsername);
                    //customer.CustomerPassword = newPassword;
                    
                    // Hash the password.
                    string salt = PasswordHash.GenerateSalt();
                    customer.CustomerPassword = PasswordHash.Hash(newPassword, salt);
                    customer.Salt = salt;

                    CRMCustomerPersistor.Update(customer);
                }
            }
            else
            {
                throw new ApplicationException("You are not authorised to update customer password for username " + username + ".");
            }
        }

        public List<SIPDomain> GetSIPDomains(string filterExpression, int offset, int count)
        {
            Customer customer = AuthoriseRequest();

            if (customer == null)
            {
                throw new ArgumentNullException("customer", "The customer cannot be empty when building authorised where expression.");
            }
            else
            {
                string authoriseExpression = "owner =\"" + customer.CustomerUsername + "\" or owner = null";
                //logger.Debug("SIPProvisioningWebService GetSIPDomains called for " + customer.CustomerUsername + " and where: " + authoriseExpression + ".");
                return SIPDomainManager.Get(DynamicExpression.ParseLambda<SIPDomain, bool>(authoriseExpression), offset, count);
            }
        }

        public int GetSIPAccountsCount(string whereExpression)
        {
            Customer customer = AuthoriseRequest();

            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetSIPAccountsCount called for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            if (authoriseExpression.IsNullOrBlank())
            {
                return SIPAccountPersistor.Count(null);
            }
            else
            {
                return SIPAccountPersistor.Count(DynamicExpression.ParseLambda<SIPAccount, bool>(authoriseExpression));
            }
        }

        public List<SIPAccount> GetSIPAccounts(string whereExpression, int offset, int count)
        {
            Customer customer = AuthoriseRequest();

            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            logger.Debug("SIPProvisioningWebService GetSIPAccounts called for " + customer.CustomerUsername + " and where: " + authoriseExpression + ", offset=" + offset + ", count=" + count + ".");

            if (authoriseExpression.IsNullOrBlank())
            {
                return SIPAccountPersistor.Get(null, "sipusername", offset, count);
            }
            else
            {
                return SIPAccountPersistor.Get(DynamicExpression.ParseLambda<SIPAccount, bool>(authoriseExpression), "sipusername", offset, count);
            }
        }

        public string AddSIPAccount(string username, string password, string domain, string avatarURL)
        {
            Customer customer = AuthoriseRequest();

            SIPSorcery.Entities.SIPAccount sipAccount = new Entities.SIPAccount()
            {
                ID = Guid.NewGuid().ToString(),
                SIPUsername = username,
                SIPPassword = password,
                AvatarURL = avatarURL,
            };

            if (!domain.IsNullOrBlank())
            {
                sipAccount.SIPDomain = domain.Trim();
            }

            return m_service.InsertSIPAccount(customer.CustomerUsername, sipAccount);
        }

        //public string AddSIPAccount(SIPSorcery.Entities.SIPAccount sipAccount)
        public SIPAccount AddSIPAccount(SIPAccount sipAccount)
        {
            Customer customer = AuthoriseRequest();
            sipAccount.Owner = customer.CustomerUsername;

            //string validationError = SIPSorcery.Entities.SIPAccount.Validate(sipAccount);
            string validationError = SIPAccount.ValidateAndClean(sipAccount);
            if (validationError != null)
            {
                logger.Warn("Validation error in AddSIPAccount for customer " + customer.CustomerUsername + ". " + validationError);
                throw new ApplicationException(validationError);
            }
            else
            {
                return SIPAccountPersistor.Add(sipAccount);
                //return m_service.InsertSIPAccount(customer.CustomerUsername, sipAccount);
            }
        }

        public SIPAccount UpdateSIPAccount(SIPAccount sipAccount)
        {
            Customer customer = AuthoriseRequest();

            if (customer.AdminId != Customer.TOPLEVEL_ADMIN_ID && sipAccount.Owner != customer.CustomerUsername)
            {
                logger.Debug("Unauthorised attempt to update SIP account by user=" + customer.CustomerUsername + ", on account owned by=" + sipAccount.Owner + ".");
                throw new ApplicationException("You are not authorised to update the SIP Account.");
            }

            string validationError = SIPAccount.ValidateAndClean(sipAccount);
            if (validationError != null)
            {
                logger.Warn("Validation error in UpdateSIPAccount for customer " + customer.CustomerUsername + ". " + validationError);
                throw new ApplicationException(validationError);
            }
            else
            {
                return SIPAccountPersistor.Update(sipAccount);
            }
        }

        public SIPAccount DeleteSIPAccount(SIPAccount sipAccount)
        {
            Customer customer = AuthoriseRequest();

            if (customer.AdminId != Customer.TOPLEVEL_ADMIN_ID && sipAccount.Owner != customer.CustomerUsername)
            {
                throw new ApplicationException("You are not authorised to delete the SIP Account.");
            }

            SIPAccountPersistor.Delete(sipAccount);

            // Enables the caller to see which SIP account has been deleted.
            return sipAccount;
        }

        public int GetSIPRegistrarBindingsCount(string whereExpression)
        {
            Customer customer = AuthoriseRequest();
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetSIPRegistrarBindingsCount for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            if (authoriseExpression.IsNullOrBlank())
            {
                return SIPRegistrarBindingsPersistor.Count(null);
            }
            else
            {
                return SIPRegistrarBindingsPersistor.Count(DynamicExpression.ParseLambda<SIPRegistrarBinding, bool>(authoriseExpression));
            }
        }

        public List<SIPRegistrarBinding> GetSIPRegistrarBindings(string whereExpression, int offset, int count)
        {
            Customer customer = AuthoriseRequest();
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetSIPRegistrarBindings for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            if (authoriseExpression.IsNullOrBlank())
            {
                return SIPRegistrarBindingsPersistor.Get(null, "sipaccountname", offset, count);
            }
            else
            {
                return SIPRegistrarBindingsPersistor.Get(DynamicExpression.ParseLambda<SIPRegistrarBinding, bool>(authoriseExpression), "sipaccountname", offset, count);
            }
        }

        public int GetSIPProvidersCount(string whereExpression)
        {
            Customer customer = AuthoriseRequest();
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetSIPProvidersCount for " + customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            if (authoriseExpression.IsNullOrBlank())
            {
                return SIPProviderPersistor.Count(null);
            }
            else
            {
                return SIPProviderPersistor.Count(DynamicExpression.ParseLambda<SIPProvider, bool>(authoriseExpression));
            }
        }

        public List<SIPProvider> GetSIPProviders(string whereExpression, int offset, int count)
        {
            Customer customer = AuthoriseRequest();
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);

            if (authoriseExpression.IsNullOrBlank())
            {
                return SIPProviderPersistor.Get(null, "providername", offset, count);
            }
            else
            {
                //logger.Debug("SIPProvisioningWebService GetSIPProviders for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");
                return SIPProviderPersistor.Get(DynamicExpression.ParseLambda<SIPProvider, bool>(authoriseExpression), "providername", offset, count);
            }
        }

        public SIPProvider AddSIPProvider(SIPProvider sipProvider)
        {
            Customer customer = AuthoriseRequest();

            if (!customer.ServiceLevel.IsNullOrBlank() && customer.ServiceLevel.ToLower() == "free")
            {
                // Check the number of SIP Provider is within limits.
                if (GetSIPProvidersCount(null) >= 1)
                {
                    throw new ApplicationException("The SIP Provider cannot be added as your existing SIP Provider count has reached the allowed limit for your service level.");
                }
            }

            sipProvider.Owner = customer.CustomerUsername;

            string validationError = SIPProvider.ValidateAndClean(sipProvider);
            if (validationError != null)
            {
                logger.Warn("Validation error in AddSIPProvider for customer " + customer.CustomerUsername + ". " + validationError);
                throw new ApplicationException(validationError);
            }
            else
            {
                return SIPProviderPersistor.Add(sipProvider);
            }
        }

        public SIPProvider UpdateSIPProvider(SIPProvider sipProvider)
        {
            Customer customer = AuthoriseRequest();

            if (customer.AdminId != Customer.TOPLEVEL_ADMIN_ID && sipProvider.Owner != customer.CustomerUsername)
            {
                throw new ApplicationException("You are not authorised to update the SIP Provider.");
            }

            string validationError = SIPProvider.ValidateAndClean(sipProvider);
            if (validationError != null)
            {
                logger.Warn("Validation error in UpdateSIPProvider for customer " + customer.CustomerUsername + ". " + validationError);
                throw new ApplicationException(validationError);
            }
            else
            {
                if (m_providerRegDisabled && sipProvider.RegisterEnabled)
                {
                    logger.Warn("A SIP provider for customer " + customer.CustomerUsername + " had registrations enabled on a disabled registrations service.");
                    throw new ApplicationException("SIP provider registrations are disabled on this system.");
                }
                else
                {
                    return SIPProviderPersistor.Update(sipProvider);
                }
            }
        }

        public SIPProvider DeleteSIPProvider(SIPProvider sipProvider)
        {
            Customer customer = AuthoriseRequest();

            if (customer.AdminId != Customer.TOPLEVEL_ADMIN_ID && sipProvider.Owner != customer.CustomerUsername)
            {
                throw new ApplicationException("You are not authorised to delete the SIP Provider.");
            }

            //logger.Debug("DeleteSIPProvider, owner=" + sipProvider.Owner + ", providername=" + sipProvider.ProviderName + ".");
            SIPProviderPersistor.Delete(sipProvider);

            // Enables the caller to see which SIP Provider has been deleted.
            return sipProvider;
        }

        public int GetSIPProviderBindingsCount(string whereExpression)
        {
            Customer customer = AuthoriseRequest();
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);

            if (authoriseExpression.IsNullOrBlank())
            {
                return SIPProviderBindingsPersistor.Count(null);
            }
            else
            {
                return SIPProviderBindingsPersistor.Count(DynamicExpression.ParseLambda<SIPProviderBinding, bool>(authoriseExpression));
            }
        }

        public List<SIPProviderBinding> GetSIPProviderBindings(string whereExpression, int offset, int count)
        {
            Customer customer = AuthoriseRequest();
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);

            if (authoriseExpression.IsNullOrBlank())
            {
                return SIPProviderBindingsPersistor.Get(null, "providername asc", offset, count);
            }
            else
            {
                return SIPProviderBindingsPersistor.Get(DynamicExpression.ParseLambda<SIPProviderBinding, bool>(authoriseExpression), "providername asc", offset, count);
            }
        }

        public int GetDialPlansCount(string whereExpression)
        {
            Customer customer = AuthoriseRequest();
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            //logger.Debug("SIPProvisioningWebService GetDialPlansCount for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            if (authoriseExpression.IsNullOrBlank())
            {
                return DialPlanPersistor.Count(null);
            }
            else
            {
                return DialPlanPersistor.Count(DynamicExpression.ParseLambda<SIPDialPlan, bool>(authoriseExpression));
            }
        }

        public List<SIPDialPlan> GetDialPlans(string whereExpression, int offset, int count)
        {
            Customer customer = AuthoriseRequest();
            string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
            logger.Debug("SIPProvisioningWebService GetDialPlans for " + customer.CustomerUsername + " and where: " + authoriseExpression + ".");

            if (authoriseExpression.IsNullOrBlank())
            {
                return DialPlanPersistor.Get(null, "dialplanname asc", offset, count);
            }
            else
            {
                return DialPlanPersistor.Get(DynamicExpression.ParseLambda<SIPDialPlan, bool>(authoriseExpression), "dialplanname asc", offset, count);
            }
        }

        public SIPDialPlan AddDialPlan(SIPDialPlan dialPlan)
        {
            Customer customer = AuthoriseRequest();

            if (!customer.ServiceLevel.IsNullOrBlank() && customer.ServiceLevel.ToLower() == "free")
            {
                // Check the number of SIP Provider is within limits.
                if (GetDialPlansCount(null) >= 1)
                {
                    throw new ApplicationException("The dial plan cannot be added as your existing dial plan count has reached the allowed limit for your service level.");
                }
            }

            dialPlan.Owner = customer.CustomerUsername;

            if (customer.AdminId != Customer.TOPLEVEL_ADMIN_ID)
            {
                dialPlan.MaxExecutionCount = SIPDialPlan.DEFAULT_MAXIMUM_EXECUTION_COUNT;
            }

            return DialPlanPersistor.Add(dialPlan);
        }

        public SIPDialPlan UpdateDialPlan(SIPDialPlan dialPlan)
        {
            Customer customer = AuthoriseRequest();

            if (customer.AdminId != Customer.TOPLEVEL_ADMIN_ID && dialPlan.Owner != customer.CustomerUsername)
            {
                throw new ApplicationException("You are not authorised to update the Dial Plan.");
            }

            return DialPlanPersistor.Update(dialPlan);
        }

        public SIPDialPlan DeleteDialPlan(SIPDialPlan dialPlan)
        {
            Customer customer = AuthoriseRequest();

            if (customer.AdminId != Customer.TOPLEVEL_ADMIN_ID && dialPlan.Owner != customer.CustomerUsername)
            {
                throw new ApplicationException("You are not authorised to delete the Dial Plan.");
            }

            DialPlanPersistor.Delete(dialPlan);

            // Enables the caller to see which dialplan has been deleted.
            return dialPlan;
        }

        public int GetCallsCount(string whereExpression)
        {
            try
            {
                Customer customer = AuthoriseRequest();

                string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
                //logger.Debug("SIPProvisioningWebService GetCallsCount for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

                if (authoriseExpression.IsNullOrBlank())
                {
                    return SIPDialoguePersistor.Count(null);
                }
                else
                {
                    return SIPDialoguePersistor.Count(DynamicExpression.ParseLambda<SIPDialogueAsset, bool>(authoriseExpression));
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetCallsCount. " + excp.Message);
                throw;
            }
        }

        public List<SIPDialogueAsset> GetCalls(string whereExpression, int offset, int count)
        {
            try
            {
                Customer customer = AuthoriseRequest();

                string authorisedExpression = GetAuthorisedWhereExpression(customer, whereExpression);
                //logger.Debug("SIPProvisioningWebService GetCalls for " + customerSession.Customer.CustomerUsername + " and where: " + authoriseExpression + ".");

                if (authorisedExpression.IsNullOrBlank())
                {
                    return SIPDialoguePersistor.Get(null, null, offset, count);
                }
                else
                {
                    return SIPDialoguePersistor.Get(DynamicExpression.ParseLambda<SIPDialogueAsset, bool>(authorisedExpression), null, offset, count);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetCalls. " + excp.Message);
                throw;
            }
        }

        public int GetCDRsCount(string whereExpression)
        {
            try
            {
                Customer customer = AuthoriseRequest();

                string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
                logger.Debug("SIPProvisioningWebService GetCDRsCount for " + customer.CustomerUsername + " and where: " + authoriseExpression + ".");

                if (authoriseExpression.IsNullOrBlank())
                {
                    return SIPCDRPersistor.Count(null);
                }
                else
                {
                    return SIPCDRPersistor.Count(DynamicExpression.ParseLambda<SIPCDRAsset, bool>(authoriseExpression));
                }

                //return m_service.GetCDRCount(customer.CustomerUsername, authoriseExpression);
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetCDRsCount. " + excp.Message);

                if (excp.InnerException != null)
                {
                    logger.Error("InnerException GetCDRs. " + excp.InnerException.Message);
                }

                throw;
            }
        }

        public List<SIPCDRAsset> GetCDRs(string whereExpression, int offset, int count)
        {
            try
            {
                Customer customer = AuthoriseRequest();

                string authoriseExpression = GetAuthorisedWhereExpression(customer, whereExpression);
                logger.Debug("SIPProvisioningWebService GetCDRs for " + customer.CustomerUsername + " and where: " + authoriseExpression + ".");

                if (authoriseExpression.IsNullOrBlank())
                {
                    return SIPCDRPersistor.Get(null, "created desc", offset, count);
                }
                else
                {
                    return SIPCDRPersistor.Get(DynamicExpression.ParseLambda<SIPCDRAsset, bool>(authoriseExpression), "created desc", offset, count);
                }

                //var results=  m_service.GetCDRs(customer.CustomerUsername, authoriseExpression, offset, count);

                //return results;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetCDRs. " + excp.Message);

                if (excp.InnerException != null)
                {
                    logger.Error("InnerException GetCDRs. " + excp.InnerException.Message);
                }

                throw;
            }
        }
    }
}

