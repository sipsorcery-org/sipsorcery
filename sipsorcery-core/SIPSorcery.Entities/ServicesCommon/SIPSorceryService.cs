using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
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

        #endregion

        #region SIP Accounts.

        public IQueryable<SIPAccount> GetSIPAccounts(string authUser)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSIPAccounts.");
            }

            return new SIPSorceryEntities().SIPAccounts.Where(x => x.Owner == authUser);
        }

        public void InsertSIPAccount(string authUser, SIPAccount sipAccount)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for InsertSIPAccount.");
            }

            string validationError = SIPAccount.Validate(sipAccount);
            if (validationError != null)
            {
                throw new ApplicationException(validationError);
            }

            sipAccount.Owner = authUser;
            sipAccount.Inserted = DateTimeOffset.UtcNow.ToString("o");
            sipAccount.IsAdminDisabled = false;
            sipAccount.AdminDisabledReason = null;

            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                sipSorceryEntities.SIPAccounts.AddObject(sipAccount);
                sipSorceryEntities.SaveChanges();
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
                SIPAccount existingAccount = (from sa in sipSorceryEntities.SIPAccounts where sa.ID == sipAccount.ID select sa).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The SIP account to update could not be found.");
                }
                else if (existingAccount.Owner != authUser)
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
                SIPAccount existingAccount = (from sa in sipSorceryEntities.SIPAccounts where sa.ID == sipAccount.ID select sa).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The SIP account to delete could not be found.");
                }
                else if (existingAccount.Owner != authUser)
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

            return new SIPSorceryEntities().SIPRegistrarBindings.Where(x => x.Owner == authUser);
        }

        #endregion

        #region SIP Providers.

        public IQueryable<SIPProvider> GetSIPProviders(string authUser)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSIPProviders.");
            }

            return new SIPSorceryEntities().SIPProviders.Where(x => x.Owner == authUser);
        }

        public void InsertSIPProvider(string authUser, SIPProvider sipProvider)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for InsertSIPProvider.");
            }

            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                string serviceLevel = (from cust in sipSorceryEntities.Customers where cust.Name == authUser select cust.ServiceLevel).FirstOrDefault();

                if (!serviceLevel.IsNullOrBlank() && serviceLevel.ToLower() == CustomerServiceLevels.Free.ToString().ToLower())
                {
                    // Check the number of SIP providers is within limits.
                    if ((from provider in sipSorceryEntities.SIPProviders where provider.Owner == authUser select provider).Count() >= PROVIDER_COUNT_FREE_SERVICE)
                    {
                        throw new ApplicationException("The SIP provider cannot be added as your existing SIP provider count has reached the allowed limit for your service level.");
                    }
                }

                string validationError = SIPProvider.Validate(sipProvider);
                if (validationError != null)
                {
                    throw new ApplicationException(validationError);
                }

                sipProvider.Owner = authUser;
                sipProvider.Inserted = DateTimeOffset.UtcNow.ToString("o");

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
                existingAccount = (from sp in sipSorceryEntities.SIPProviders where sp.ID == sipProvider.ID select sp).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The SIP provider to update could not be found.");
                }
                else if (existingAccount.Owner != authUser)
                {
                    throw new ApplicationException("Not authorised to update the SIP Provider.");
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
                SIPProvider existingAccount = (from sp in sipSorceryEntities.SIPProviders where sp.ID == sipProvider.ID select sp).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The SIP Provider to delete could not be found.");
                }
                else if (existingAccount.Owner != authUser)
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

            return new SIPSorceryEntities().SIPProviderBindings.Where(x => x.Owner == authUser);
        }

        #endregion
    }
}
