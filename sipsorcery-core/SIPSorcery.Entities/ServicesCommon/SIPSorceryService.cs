using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.Linq;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Linq.Dynamic;
using System.Text;
using SIPSorcery.SIP;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Entities
{
    public class SIPSorceryService
    {
        private const string NEW_ACCOUNT_EMAIL_FROM_ADDRESS = "admin@sipsorcery.com";
        private const string NEW_ACCOUNT_EMAIL_SUBJECT = "SIP Sorcery Account Confirmation";
        private const int PROVIDER_COUNT_FREE_SERVICE = 1;  // The number of SIP Providers allowed on free accounts.
        private const int DIALPLAN_COUNT_FREE_SERVICE = 1;  // The number of dial plans allowed on free accounts.

        private static string m_disabledProviderServerPattern = AppState.GetConfigSetting("DisabledProviderServersPattern");
        private static string m_customerConfirmLink = AppState.GetConfigSetting("CustomerConfirmLink");
        private static string m_domainForProviderContact = AppState.GetConfigSetting("DomainForProviderContact") ?? "sipsorcery.com";

        private const string NEW_ACCOUNT_EMAIL_BODY =
            "Hi {0},\r\n\r\n" +
            "This is your automated SIP Sorcery new account confirmation email.\r\n\r\n" +
            "To confirm your account please visit the link below. If you did not request this email please ignore it.\r\n\r\n" +
            "{1}?id={2}\r\n\r\n" +
            "Regards,\r\n\r\n" +
            "SIP Sorcery";

        private static ILog logger = AppState.logger;

        static SIPSorceryService()
        {
            // Prevent users from creaing loopback or other crazy providers.
            if (!m_disabledProviderServerPattern.IsNullOrBlank())
            {
                SIPProvider.ProhibitedServerPatterns = m_disabledProviderServerPattern;
            }
        }

        public SIPSorceryService()
        { }

        #region Customers

        public void InsertCustomer(Customer customer)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                if (sipSorceryEntities.Customers.Any(x => x.Name == customer.Name.ToLower()))
                {
                    throw new ApplicationException("The username is already taken. Please choose a different one.");
                }
                else if (sipSorceryEntities.Customers.Any(x => x.EmailAddress.ToLower() == customer.EmailAddress.ToLower()))
                {
                    throw new ApplicationException("The email address is already associated with an account.");
                }
                else
                {
                    customer.ID = Guid.NewGuid().ToString();
                    customer.Inserted = DateTime.UtcNow.ToString("o");
                    customer.Name = customer.Name.Trim().ToLower();
                    customer.MaxExecutionCount = Customer.FREE_MAXIMUM_EXECUTION_COUNT;
                    customer.APIKey = Crypto.GetRandomByteString(Customer.API_KEY_LENGTH / 2);
                    customer.ServiceLevel = CustomerServiceLevels.Free.ToString();

                    if ((customer.EntityState != EntityState.Detached))
                    {
                        sipSorceryEntities.ObjectStateManager.ChangeObjectState(customer, EntityState.Added);
                    }
                    else
                    {
                        sipSorceryEntities.Customers.AddObject(customer);
                    }

                    sipSorceryEntities.SaveChanges();

                    logger.Debug("New customer record added for " + customer.Name + ".");

                    // Create a default dialplan.
                    SIPDialPlan defaultDialPlan = new SIPDialPlan()
                    {
                        ID = Guid.NewGuid().ToString(),
                        Owner = customer.Name,
                        DialPlanName = "default",
                        DialPlanScript = "sys.Log(\"Log message from default dialplan.\")\nsys.Dial(\"music@iptel.org\")\n",
                        ScriptTypeDescription = SIPDialPlanScriptTypesEnum.Ruby.ToString(),
                        Inserted = DateTimeOffset.UtcNow.ToString("o"),
                        LastUpdate = DateTimeOffset.UtcNow.ToString("o"),
                        MaxExecutionCount = SIPDialPlan.DEFAULT_MAXIMUM_EXECUTION_COUNT
                    };
                    sipSorceryEntities.SIPDialPlans.AddObject(defaultDialPlan);
                    sipSorceryEntities.SaveChanges();
                    logger.Debug("Default dialplan added for " + customer.Name + ".");

                    // Get default domain name.
                    string defaultDomain = sipSorceryEntities.SIPDomains.Where(x => x.AliasList.Contains("local")).Select(y => y.Domain).First();

                    // Create SIP account.
                    if (!sipSorceryEntities.SIPAccounts.Any(s => s.SIPUsername == customer.Name && s.SIPDomain == defaultDomain))
                    {
                        SIPAccount sipAccount = SIPAccount.Create(customer.Name, defaultDomain, customer.Name, customer.CustomerPassword, "default");
                        sipSorceryEntities.SIPAccounts.AddObject(sipAccount);
                        sipSorceryEntities.SaveChanges();
                        logger.Debug("SIP account " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + " added for " + sipAccount.Owner + ".");
                    }
                    else
                    {
                        int attempts = 0;
                        while (attempts < 10)
                        {
                            string testUsername = customer.Name + Crypto.GetRandomString(4);
                            if (!sipSorceryEntities.SIPAccounts.Any(s => s.SIPUsername == testUsername && s.SIPDomain == defaultDomain))
                            {
                                SIPAccount sipAccount = SIPAccount.Create(customer.Name, defaultDomain, testUsername, customer.CustomerPassword, "default");
                                sipSorceryEntities.SIPAccounts.AddObject(sipAccount);
                                sipSorceryEntities.SaveChanges();
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
                        SIPSorcerySMTP.SendEmail(customer.EmailAddress, NEW_ACCOUNT_EMAIL_FROM_ADDRESS, NEW_ACCOUNT_EMAIL_SUBJECT, String.Format(NEW_ACCOUNT_EMAIL_BODY, customer.Firstname, m_customerConfirmLink, customer.ID));
                    }
                    else
                    {
                        logger.Debug("Customer confirmation email was not sent as no confirmation link has been set.");
                    }
                }
            }
        }

        public string ConfirmEmailAddress(string id, string requestIPAddress)
        {
            try
            {
                logger.Debug("CustomerEmailConfirmation request from " + requestIPAddress + ".");

                if (!id.IsNullOrBlank())
                {
                    using (SIPSorceryEntities sipSorceryEntities = new SIPSorceryEntities())
                    {
                        Customer customer = (from cust in sipSorceryEntities.Customers where cust.ID == id select cust).Single();

                        if (customer != null)
                        {
                            if (!customer.EmailAddressConfirmed)
                            {
                                customer.CreatedFromIPAddress = requestIPAddress;
                                customer.EmailAddressConfirmed = true;
                                sipSorceryEntities.SaveChanges();

                                return null;
                            }
                            else
                            {
                                return "Your account has already been confirmed.";
                            }
                        }
                        else
                        {
                            return "No matching customer record could be found. Please check that you entered the confirmation URL correctly.";
                        }
                    }
                }
                else
                {
                    return "Your account could not be confirmed. Please check that you entered the confirmation URL correctly.";
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ConfirmEmailAddress. " + excp.Message);
                return "There was an error confirming your account. Please check that you entered the confirmation URL correctly.";
            }
        }

        public Customer GetCustomer(string username)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                return (from cust in sipSorceryEntities.Customers where cust.Name == username select cust).SingleOrDefault();
            }
        }

        public Customer GetCustomerForAPIKey(string apiKey)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                return (from cust in sipSorceryEntities.Customers where cust.APIKey == apiKey select cust).SingleOrDefault();
            }
        }

        #endregion

        #region SIP Accounts.

        public IQueryable<SIPAccount> GetSIPAccounts(string authUser)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSIPAccounts.");
            }

            return new SIPSorceryEntities().SIPAccounts.Where(x => x.Owner == authUser.ToLower());
        }

        public string InsertSIPAccount(string authUser, SIPAccount sipAccount)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for InsertSIPAccount.");
            }

            sipAccount.ID = Guid.NewGuid().ToString();
            sipAccount.Owner = authUser.ToLower();
            sipAccount.Inserted = DateTimeOffset.UtcNow.ToString("o");
            sipAccount.IsAdminDisabled = false;
            sipAccount.AdminDisabledReason = null;

            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                if (sipAccount.SIPDomain.IsNullOrBlank())
                {
                    // Get default domain name.
                    string defaultDomain = sipSorceryEntities.SIPDomains.Where(x => x.AliasList.Contains("local")).Select(y => y.Domain).First();
                    sipAccount.SIPDomain = defaultDomain;
                }

                string validationError = SIPAccount.Validate(sipAccount);
                if (validationError != null)
                {
                    throw new ApplicationException(validationError);
                }

                // Check for a duplicate.
                if (sipSorceryEntities.SIPAccounts.Where(x => x.SIPUsername.ToLower() == sipAccount.SIPUsername.ToLower() && 
                                                                x.SIPDomain.ToLower() == sipAccount.SIPDomain.ToLower()).Count() > 0)
                {
                    throw new ApplicationException("Sorry the requested username and domain combination is already in use.");
                }

                sipSorceryEntities.SIPAccounts.AddObject(sipAccount);
                sipSorceryEntities.SaveChanges();

                return sipAccount.ID;
            }
        }

        public void UpdateSIPAccount(string authUser, SIPAccount sipAccount)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for UpdateSIPAccount.");
            }

            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                SIPAccount existingAccount = (from sa in sipSorceryEntities.SIPAccounts where sa.ID == sipAccount.ID && sa.Owner == authUser.ToLower() select sa).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The SIP account to update could not be found.");
                }
                else if (existingAccount.Owner != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to update the SIP Account.");
                }

                existingAccount.DontMangleEnabled = sipAccount.DontMangleEnabled;
                existingAccount.InDialPlanName = sipAccount.InDialPlanName;
                existingAccount.IPAddressACL = sipAccount.IPAddressACL;
                existingAccount.IsIncomingOnly = sipAccount.IsIncomingOnly;
                existingAccount.IsSwitchboardEnabled = sipAccount.IsSwitchboardEnabled;
                existingAccount.IsUserDisabled = sipAccount.IsUserDisabled;
                existingAccount.NetworkID = sipAccount.NetworkID;
                existingAccount.OutDialPlanName = sipAccount.OutDialPlanName;
                existingAccount.SendNATKeepAlives = sipAccount.SendNATKeepAlives;
                existingAccount.SIPPassword = sipAccount.SIPPassword;

                string validationError = SIPAccount.Validate(existingAccount);
                if (validationError != null)
                {
                    throw new ApplicationException(validationError);
                }

                sipSorceryEntities.SaveChanges();
            }
        }

        public void DeleteSIPAccount(string authUser, SIPAccount sipAccount)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                SIPAccount existingAccount = (from sa in sipSorceryEntities.SIPAccounts where sa.ID == sipAccount.ID && sa.Owner == authUser.ToLower() select sa).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The SIP account to delete could not be found.");
                }
                else if (existingAccount.Owner != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to delete the SIP Account.");
                }

                sipSorceryEntities.SIPAccounts.DeleteObject(existingAccount);
                sipSorceryEntities.SaveChanges();
            }
        }

        public IQueryable<SIPRegistrarBinding> GetSIPRegistrarBindings(string authUser)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSIPRegistrarBindings.");
            }

            return new SIPSorceryEntities().SIPRegistrarBindings.Where(x => x.Owner == authUser.ToLower());
        }

        #endregion

        #region SIP Providers.

        public IQueryable<SIPProvider> GetSIPProviders(string authUser)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSIPProviders.");
            }

            return new SIPSorceryEntities().SIPProviders.Where(x => x.Owner == authUser.ToLower());
        }

        public void InsertSIPProvider(string authUser, SIPProvider sipProvider)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for InsertSIPProvider.");
            }

            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                string serviceLevel = (from cust in sipSorceryEntities.Customers where cust.Name == authUser.ToLower() select cust.ServiceLevel).FirstOrDefault();

                if (!serviceLevel.IsNullOrBlank() && serviceLevel.ToLower() == CustomerServiceLevels.Free.ToString().ToLower())
                {
                    // Check the number of SIP providers is within limits.
                    if ((from provider in sipSorceryEntities.SIPProviders
                         where provider.Owner.ToLower() == authUser.ToLower() && !provider.IsReadOnly
                         select provider).Count() >= PROVIDER_COUNT_FREE_SERVICE)
                    {
                        throw new ApplicationException("The SIP Provider cannot be added. You are limited to " + PROVIDER_COUNT_FREE_SERVICE + " SIP Provider on a Free account. Please upgrade to a Premium service if you wish to create additional SIP Providers.");
                    }
                }

                if (sipProvider.RegisterEnabled)
                {
                    if (sipProvider.RegisterContact.IsNullOrBlank())
                    {
                        sipProvider.RegisterContact = "sip:" + authUser.ToLower() + "@" + m_domainForProviderContact;
                    }
                    else
                    {
                        try
                        {
                            sipProvider.RegisterContact = SIPURI.ParseSIPURIRelaxed(sipProvider.RegisterContact.Trim()).ToString();
                        }
                        catch (Exception sipURIExcp)
                        {
                            throw new ApplicationException(sipURIExcp.Message);
                        }
                    }

                    if (sipProvider.RegisterExpiry == null || sipProvider.RegisterExpiry < SIPProvider.REGISTER_MINIMUM_EXPIRY || sipProvider.RegisterExpiry > SIPProvider.REGISTER_MAXIMUM_EXPIRY)
                    {
                        sipProvider.RegisterExpiry = SIPProvider.REGISTER_DEFAULT_EXPIRY;
                    }
                }

                sipProvider.ID = Guid.NewGuid().ToString();
                sipProvider.Owner = authUser.ToLower();
                sipProvider.Inserted = DateTimeOffset.UtcNow.ToString("o");
                sipProvider.LastUpdate = DateTimeOffset.UtcNow.ToString("o");
                sipProvider.RegisterAdminEnabled = true;

                string validationError = SIPProvider.Validate(sipProvider);
                if (validationError != null)
                {
                    throw new ApplicationException(validationError);
                }

                sipSorceryEntities.SIPProviders.AddObject(sipProvider);
                sipSorceryEntities.SaveChanges();
            }

            SIPProviderBindingSynchroniser.SIPProviderAdded(sipProvider);
        }

        public void UpdateSIPProvider(string authUser, SIPProvider sipProvider)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for InsertSIPProvider.");
            }

            SIPProvider existingAccount = null;

            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                existingAccount = (from sp in sipSorceryEntities.SIPProviders where sp.ID == sipProvider.ID && sp.Owner == authUser.ToLower() select sp).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The SIP provider to update could not be found.");
                }
                else if (existingAccount.Owner.ToLower() != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to update the SIP Provider.");
                }
                else if (existingAccount.IsReadOnly)
                {
                    throw new ApplicationException("This SIP Provider is read-only. Please upgrade to a Premium service to enable it.");
                }

                existingAccount.CustomHeaders = sipProvider.CustomHeaders;
                existingAccount.GVCallbackNumber = sipProvider.GVCallbackNumber;
                existingAccount.GVCallbackPattern = sipProvider.GVCallbackPattern;
                existingAccount.GVCallbackType = sipProvider.GVCallbackType;
                existingAccount.LastUpdate = DateTimeOffset.UtcNow.ToString("o");
                existingAccount.ProviderAuthUsername = sipProvider.ProviderAuthUsername;
                existingAccount.ProviderFrom = sipProvider.ProviderFrom;
                existingAccount.ProviderName = sipProvider.ProviderName;
                existingAccount.ProviderOutboundProxy = sipProvider.ProviderOutboundProxy;
                existingAccount.ProviderPassword = sipProvider.ProviderPassword;
                existingAccount.ProviderServer = sipProvider.ProviderServer;
                existingAccount.ProviderUsername = sipProvider.ProviderUsername;
                existingAccount.RegisterContact = sipProvider.RegisterContact;
                existingAccount.RegisterEnabled = sipProvider.RegisterEnabled;
                existingAccount.RegisterExpiry = sipProvider.RegisterExpiry;
                existingAccount.RegisterRealm = sipProvider.RegisterRealm;
                existingAccount.RegisterServer = sipProvider.RegisterServer;
                existingAccount.RegisterDisabledReason = sipProvider.RegisterDisabledReason;

                string validationError = SIPProvider.Validate(existingAccount);
                if (validationError != null)
                {
                    throw new ApplicationException(validationError);
                }

                sipSorceryEntities.SaveChanges();
            }

            SIPProviderBindingSynchroniser.SIPProviderUpdated(existingAccount);
        }

        public void DeleteSIPProvider(string authUser, SIPProvider sipProvider)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                SIPProvider existingAccount = (from sp in sipSorceryEntities.SIPProviders where sp.ID == sipProvider.ID && sp.Owner == authUser.ToLower() select sp).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The SIP Provider to delete could not be found.");
                }
                else if (existingAccount.Owner.ToLower() != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to delete the SIP Provider.");
                }

                sipSorceryEntities.SIPProviders.DeleteObject(existingAccount);
                sipSorceryEntities.SaveChanges();
            }
        }

        public IQueryable<SIPProviderBinding> GetSIPProviderBindings(string authUser)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSIPProviderBindings.");
            }

            return new SIPSorceryEntities().SIPProviderBindings.Where(x => x.Owner.ToLower() == authUser.ToLower());
        }

        #endregion

        #region SIP Dial Plan

        public IQueryable<SIPDialPlan> GetSIPSIPDialPlans(string authUser)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSIPSIPDialPlans.");
            }

            return new SIPSorceryEntities().SIPDialPlans.Where(x => x.Owner == authUser.ToLower());
        }

        public void InsertSIPDialPlan(string authUser, SIPDialPlan sipDialPlan)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for InsertSIPDialPlan.");
            }

            //string validationError = SIPDialPlan.Validate(sipDialPlan);
            //if (validationError != null)
            //{
            //    throw new ApplicationException(validationError);
            //}

            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                string serviceLevel = (from cust in sipSorceryEntities.Customers where cust.Name == authUser.ToLower() select cust.ServiceLevel).FirstOrDefault();

                if (!serviceLevel.IsNullOrBlank() && serviceLevel.ToLower() == CustomerServiceLevels.Free.ToString().ToLower())
                {
                    // Check the number of Dial Plans is within limits.
                    if ((from dialPlan in sipSorceryEntities.SIPDialPlans
                         where dialPlan.Owner == authUser.ToLower() && !dialPlan.IsReadOnly
                         select dialPlan).Count() >= DIALPLAN_COUNT_FREE_SERVICE)
                    {
                        throw new ApplicationException("The Dial Plan cannot be added. You are limited to " + DIALPLAN_COUNT_FREE_SERVICE + " dial plan on a Free account. Please upgrade to a Premium service if you wish to create additional dial plans.");
                    }
                }

                sipDialPlan.ID = Guid.NewGuid().ToString();
                sipDialPlan.Owner = authUser.ToLower();
                sipDialPlan.Inserted = DateTimeOffset.UtcNow.ToString("o");
                sipDialPlan.LastUpdate = DateTimeOffset.UtcNow.ToString("o");
                sipDialPlan.MaxExecutionCount = SIPDialPlan.DEFAULT_MAXIMUM_EXECUTION_COUNT;

                if (sipDialPlan.ScriptType == SIPDialPlanScriptTypesEnum.TelisWizard)
                {
                    // Set the default script.
                    sipDialPlan.DialPlanScript = "require 'teliswizard'";

                    // Create a new SIP dialplan options record.
                    SIPDialplanOption options = sipSorceryEntities.SIPDialplanOptions.CreateObject();
                    options.ID = Guid.NewGuid().ToString();
                    options.Owner = sipDialPlan.Owner;
                    options.DialPlanID = sipDialPlan.ID;
                    sipSorceryEntities.SIPDialplanOptions.AddObject(options);
                }
                if (sipDialPlan.ScriptType == SIPDialPlanScriptTypesEnum.SimpleWizard)
                {
                    // Set the default script.
                    sipDialPlan.DialPlanScript = "require 'simplewizard'";
                }

                sipSorceryEntities.SIPDialPlans.AddObject(sipDialPlan);
                sipSorceryEntities.SaveChanges();
            }
        }

        public void UpdateSIPDialPlan(string authUser, SIPDialPlan sipDialPlan)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for UpdateSIPDialPlan.");
            }

            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                SIPDialPlan existingAccount = (from dp in sipSorceryEntities.SIPDialPlans where dp.ID == sipDialPlan.ID && dp.Owner == authUser.ToLower() select dp).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The SIP Dial Plan to update could not be found.");
                }
                else if (existingAccount.Owner != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to update the SIP Dial Plan.");
                }
                else if (existingAccount.IsReadOnly)
                {
                    throw new ApplicationException("This Dial Plan is read-only. Please upgrade to a Premium service to enable it.");
                }

                logger.Debug("Updating SIP dialplan " + existingAccount.DialPlanName + " for " + existingAccount.Owner + ".");

                existingAccount.DialPlanScript = sipDialPlan.DialPlanScript;
                existingAccount.LastUpdate = DateTimeOffset.UtcNow.ToString("o");
                existingAccount.TraceEmailAddress = sipDialPlan.TraceEmailAddress;
                existingAccount.ScriptTypeDescription = sipDialPlan.ScriptTypeDescription;
                existingAccount.AcceptNonInvite = sipDialPlan.AcceptNonInvite;

                if (existingAccount.DialPlanName != sipDialPlan.DialPlanName)
                {
                    // Need to update the SIP accounts using the dial plan.
                    string dialPlanName = existingAccount.DialPlanName;
                    var sipAccounts = (from sa in sipSorceryEntities.SIPAccounts where 
                                           (sa.OutDialPlanName == dialPlanName || sa.InDialPlanName == dialPlanName)
                                            && sa.Owner == authUser.ToLower() select sa).ToList();

                    foreach (SIPAccount sipAccount in sipAccounts)
                    {
                        if (sipAccount.InDialPlanName == dialPlanName)
                        {
                            logger.Debug("SIP dialplan name updated; updating in dialplan on SIP account" + sipAccount.SIPUsername + " for " + existingAccount.Owner + " to " + sipDialPlan.DialPlanName + ".");
                            sipAccount.InDialPlanName = sipDialPlan.DialPlanName;
                        }

                        if (sipAccount.OutDialPlanName == dialPlanName)
                        {
                            logger.Debug("SIP dialplan name updated; updating out dialplan on SIP account" + sipAccount.SIPUsername + " for " + existingAccount.Owner + " to " + sipDialPlan.DialPlanName + ".");
                            sipAccount.OutDialPlanName = sipDialPlan.DialPlanName;
                        }
                    }

                    existingAccount.DialPlanName = sipDialPlan.DialPlanName;
                }
                //string validationError = SIPDialPlan.Validate(existingAccount);
                //if (validationError != null)
                //{
                //    throw new ApplicationException(validationError);
                //}

                sipSorceryEntities.SaveChanges();
            }
        }

        public void DeleteSIPDialPlan(string authUser, SIPDialPlan sipDialPlan)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                SIPDialPlan existingAccount = (from dp in sipSorceryEntities.SIPDialPlans where dp.ID == sipDialPlan.ID && dp.Owner == authUser.ToLower() select dp).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The SIP Dial Plan to delete could not be found.");
                }
                else if (existingAccount.Owner != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to delete the SIP Dial Plan.");
                }

                sipSorceryEntities.SIPDialPlans.DeleteObject(existingAccount);
                sipSorceryEntities.SaveChanges();
            }
        }

        #endregion

        #region Simple Wizard Dial Plan Rules.

        public IQueryable<SimpleWizardRule> GetSimpleWizardRules(string authUser)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSimpleDialPlanWizardRules.");
            }

            return new SIPSorceryEntities().SimpleWizardRules.Where(x => x.Owner == authUser.ToLower());
        }

        public void InsertSimpleWizardRule(string authUser, SimpleWizardRule rule)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for InsertSimplWizardeDialPlanRule.");
            }

            rule.Owner = authUser.ToLower();

            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                if (rule.EntityState != EntityState.Detached)
                {
                    sipSorceryEntities.ObjectStateManager.ChangeObjectState(rule, EntityState.Added);
                }
                else
                {
                    sipSorceryEntities.SimpleWizardRules.AddObject(rule);
                }

                sipSorceryEntities.SaveChanges();
            }
        }

        public void UpdateSimpleWizardRule(string authUser, SimpleWizardRule rule)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for InsertSimplWizardeDialPlanRule.");
            }

            SimpleWizardRule existingRule = null;

            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                existingRule = (from ru in sipSorceryEntities.SimpleWizardRules where ru.ID == rule.ID && ru.Owner == authUser.ToLower() select ru).FirstOrDefault();

                if (existingRule == null)
                {
                    throw new ApplicationException("The Simple Wizard rule to update could not be found.");
                }
                else if (existingRule.Owner != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to update the Simple Wizard rule.");
                }

                existingRule.Description = rule.Description;
                existingRule.Command = rule.Command;
                existingRule.CommandParameter1 = rule.CommandParameter1;
                existingRule.CommandParameter2 = rule.CommandParameter2;
                existingRule.CommandParameter3 = rule.CommandParameter3;
                existingRule.Direction = rule.Direction;
                existingRule.Pattern = rule.Pattern;
                existingRule.Priority = rule.Priority;
                existingRule.TimeIntervalID = rule.TimeIntervalID;

                sipSorceryEntities.SaveChanges();
            }
        }

        public void DeleteSimpleWizardRule(string authUser, SimpleWizardRule rule)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                SimpleWizardRule existingRule = (from ru in sipSorceryEntities.SimpleWizardRules where ru.ID == rule.ID && ru.Owner == authUser.ToLower() select ru).FirstOrDefault();

                if (existingRule == null)
                {
                    throw new ApplicationException("The Simple Wizard Rule to delete could not be found.");
                }
                else if (existingRule.Owner != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to delete the Simple Wizard Rule.");
                }

                sipSorceryEntities.SimpleWizardRules.DeleteObject(existingRule);
                sipSorceryEntities.SaveChanges();
            }
        }

        #endregion

        #region CDR's.

        public int GetCDRCount(string authUser, string where)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetCDRCount.");
            }

            using (var entities = new SIPSorceryEntities())
            {
                var query = (from cdr in entities.CDRs where cdr.Owner == authUser.ToLower() select cdr);

                if (where != null)
                {
                    query = query.Where(DynamicExpression.ParseLambda<CDR, bool>(where));
                }

                return query.Count();
            }
        }

        public List<CDR> GetCDRs(string authUser, string where, int offset, int count)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetCDRs.");
            }

            using (var entities = new SIPSorceryEntities())
            {
                var query = (from cdr in entities.CDRs where cdr.Owner == authUser.ToLower() select cdr);

                if (where != null)
                {
                    query = query.Where(DynamicExpression.ParseLambda<CDR, bool>(where));
                }

                return query.AsEnumerable().OrderByDescending(x => x.Created).Skip(offset).Take(count).ToList();
            }
        }

        #endregion
    }
}
