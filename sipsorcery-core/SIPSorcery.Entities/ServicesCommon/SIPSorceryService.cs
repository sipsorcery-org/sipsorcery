using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
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
        private const int DEFAULT_COMMAND_TIMEOUT = 180;     // The MySQL command timeout in seconds.

        private static string m_disabledProviderServerPattern = AppState.GetConfigSetting("DisabledProviderServersPattern");                    // Provider server fields that need to be completely disallowed.
        private static string m_disabledRegisterProviderServerPattern = AppState.GetConfigSetting("DisabledRegisterProviderServersPattern");    // Provider server fields that need to be prevented from registering.
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

        internal CustomerAccountDataLayer _customerAccountDataLayer = new CustomerAccountDataLayer();
        internal RateDataLayer _rateDataLayer = new RateDataLayer();

        static SIPSorceryService()
        {
            // Prevent users from creating loopback or other crazy providers.
            if (!m_disabledProviderServerPattern.IsNullOrBlank())
            {
                SIPProvider.ProhibitedServerPatterns = m_disabledProviderServerPattern;
            }
        }

        public SIPSorceryService()
        { }

        public List<string> GetTimeZones()
        {
            List<string> timezones = new List<string>();
            foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
            {
                timezones.Add(zone.DisplayName);
            }
            return timezones;
        }

        #region Customers

        public void InsertCustomer(Customer customer)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                if (sipSorceryEntities.Customers.Any(x => x.Name.ToLower() == customer.Name.ToLower()))
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
                    customer.ServiceLevel = (customer.ServiceLevel == null) ? CustomerServiceLevels.Free.ToString() : customer.ServiceLevel;
                    customer.EmailAddressConfirmed = true;
                    customer.CreatedFromIPAddress = System.Web.HttpContext.Current.Request.UserHostAddress;

                    string plainTextassword = customer.CustomerPassword;

                    // Hash the password.
                    string salt = PasswordHash.GenerateSalt();
                    customer.CustomerPassword = PasswordHash.Hash(customer.CustomerPassword, salt);
                    customer.Salt = salt;

                    if (customer.ServiceRenewalDate != null)
                    {
                        DateTime renewalDate = DateTime.MinValue;
                        if (DateTime.TryParse(customer.ServiceRenewalDate, out renewalDate))
                        {
                            customer.ServiceRenewalDate = DateTime.SpecifyKind(renewalDate, DateTimeKind.Utc).ToUniversalTime().ToString("o");
                        }
                        else
                        {
                            throw new ApplicationException("The service renewal date could not be parsed as a valid date.");
                        }
                    }

                    //if ((customer.EntityState != EntityState.Detached))
                    //{
                    //    sipSorceryEntities.ObjectStateManager.ChangeObjectState(customer, EntityState.Added);
                    //}
                    //else
                    //{
                        sipSorceryEntities.Customers.Add(customer);
                    //}

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
                    sipSorceryEntities.SIPDialPlans.Add(defaultDialPlan);
                    sipSorceryEntities.SaveChanges();

                    logger.Debug("Default dialplan added for " + customer.Name + ".");

                    // Get default domain name.
                    string defaultDomain = sipSorceryEntities.SIPDomains.Where(x => x.AliasList.Contains("local")).Select(y => y.Domain).First();

                    // Create SIP account.
                    if (!sipSorceryEntities.SIPAccounts.Any(s => s.SIPUsername == customer.Name && s.SIPDomain == defaultDomain))
                    {
                        SIPAccount sipAccount = SIPAccount.Create(customer.Name, defaultDomain, customer.Name, plainTextassword, "default");
                        sipSorceryEntities.SIPAccounts.Add(sipAccount);
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
                                SIPAccount sipAccount = SIPAccount.Create(customer.Name, defaultDomain, testUsername, plainTextassword, "default");
                                sipSorceryEntities.SIPAccounts.Add(sipAccount);
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

        /// <summary>
        /// Only available from the REST API service to admins.
        /// </summary>
        public void UpdateCustomerServiceLevel(string username, CustomerServiceLevels serviceLevel, DateTimeOffset? renewalDate)
        {
            logger.Debug("Updating customer " + username + " to service level " + serviceLevel + " and renewal date " + renewalDate + ".");

            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                var customer = (from cust in sipSorceryEntities.Customers where cust.Name.ToLower() == username.ToLower() select cust).SingleOrDefault();

                customer.ServiceLevel = serviceLevel.ToString();
                customer.ServiceRenewalDate = (renewalDate != null) ? renewalDate.Value.ToUniversalTime().ToString("o") : null;

                sipSorceryEntities.SaveChanges();
            }
        }

        public void SetAllProvidersAndDialPlansReadonly(string username)
        {
            try
            {
                logger.Debug("Setting all providers and dial plans to readonly for " + username + ".");

                using (var sipSorceryEntities = new SIPSorceryEntities())
                {
                    var providers = (from prov in sipSorceryEntities.SIPProviders where prov.Owner.ToLower() == username.ToLower() select prov).ToList();

                    foreach (var provider in providers)
                    {
                        logger.Debug(" Setting provider " + provider.ProviderName + " to readonly.");
                        provider.RegisterEnabled = false;
                        provider.IsReadOnly = true;
                    }

                    sipSorceryEntities.SaveChanges();

                    var dialplans = (from dp in sipSorceryEntities.SIPDialPlans where dp.Owner.ToLower() == username.ToLower() select dp).ToList();

                    foreach (var dialplan in dialplans)
                    {
                        logger.Debug(" Setting dial plan " + dialplan.DialPlanName + " to readonly.");
                        dialplan.IsReadOnly = true;
                    }

                    sipSorceryEntities.SaveChanges();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SetAllProvidersAndDialPlansReadonly. " + excp.Message);
                throw;
            }
        }

        public void UpdateCustomer(string authUser, Customer customer)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for UpdateCustomer.");
            }

            using (var db = new SIPSorceryEntities())
            {
                var existingCustomer = (from cust in db.Customers where cust.Name.ToLower() == authUser.ToLower() select cust).Single();

                if (existingCustomer == null)
                {
                    throw new ApplicationException("The customer record to update could not be found.");
                }
                else
                {
                    existingCustomer.Firstname = customer.Firstname;
                    existingCustomer.Lastname = customer.Lastname;
                    existingCustomer.EmailAddress = customer.EmailAddress;
                    existingCustomer.SecurityQuestion = customer.SecurityQuestion;
                    existingCustomer.SecurityAnswer = customer.SecurityAnswer;
                    existingCustomer.City = customer.City;
                    existingCustomer.Country = customer.Country;
                    existingCustomer.WebSite = customer.WebSite;
                    existingCustomer.Timezone = customer.Timezone;

                    db.SaveChanges();
                }
            }
        }

        public void CustomerResetAPIKey(string authUser, string customerUsername)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for CustomerResetAPIKey.");
            }
            else if (authUser.ToLower() != customerUsername.ToLower())
            {
                throw new ArgumentException("You are not authorised to reset the API key for " + customerUsername + ".");
            }

            using (var db = new SIPSorceryEntities())
            {
                var existingCustomer = (from cust in db.Customers where cust.Name.ToLower() == customerUsername.ToLower() select cust).Single();

                if (existingCustomer == null)
                {
                    throw new ApplicationException("The customer record to reset the API key for could not be found.");
                }
                else
                {
                    existingCustomer.APIKey = Crypto.GetRandomByteString(Customer.API_KEY_LENGTH / 2);

                    db.SaveChanges();
                }
            }
        }

        #endregion

        #region SIP Accounts.

        public int GetSIPAccountsCount(string authUser, string where)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSIPAccountsCount.");
            }

            using (var entities = new SIPSorceryEntities())
            {
                var query = (from sipAccount in entities.SIPAccounts where sipAccount.Owner.ToLower() == authUser.ToLower() select sipAccount);

                if (where != null)
                {
                    query = query.Where(DynamicExpression.ParseLambda<SIPAccount, bool>(where));
                }

                return query.Count();
            }
        }

        public IQueryable<SIPAccount> GetSIPAccounts(string authUser)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSIPAccounts.");
            }

            return new SIPSorceryEntities().SIPAccounts.Where(x => x.Owner.ToLower() == authUser.ToLower());
        }

        public List<SIPAccount> GetSIPAccounts(string authUser, string where, int offset, int count)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSIPAccounts.");
            }

            using (var entities = new SIPSorceryEntities())
            {
                var query = (from sipAccount in entities.SIPAccounts where sipAccount.Owner.ToLower() == authUser.ToLower() select sipAccount);

                if (where != null)
                {
                    query = query.Where(DynamicExpression.ParseLambda<SIPAccount, bool>(where));
                }

                return query.AsEnumerable().OrderBy(x => x.SIPUsername).Skip(offset).Take(count).ToList();
            }
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

                sipSorceryEntities.SIPAccounts.Add(sipAccount);
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
                SIPAccount existingAccount = (from sa in sipSorceryEntities.SIPAccounts where sa.ID == sipAccount.ID && sa.Owner.ToLower() == authUser.ToLower() select sa).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The SIP account to update could not be found.");
                }
                else if (existingAccount.Owner != authUser.ToLower())
                {
                    logger.Warn("User " + authUser + " was not authorised to update SIP account " + existingAccount.SIPUsername + " belonging to " + existingAccount.Owner + ".");
                    throw new ApplicationException("Not authorised to update the SIP Account.");
                }

                // Check for a duplicate in case the SIP username has been changed.
                if (sipSorceryEntities.SIPAccounts.Where(x => x.SIPUsername.ToLower() == sipAccount.SIPUsername.ToLower() &&
                                                              x.SIPDomain.ToLower() == sipAccount.SIPDomain.ToLower() &&
                                                              x.ID != existingAccount.ID).Count() > 0)
                {
                    throw new ApplicationException("Sorry the requested username and domain combination is already in use.");
                }

                existingAccount.SIPUsername = sipAccount.SIPUsername;
                existingAccount.SIPDomain = sipAccount.SIPDomain;
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
                existingAccount.AccountCode = sipAccount.AccountCode;
                existingAccount.Description = sipAccount.Description;

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
                SIPAccount existingAccount = (from sa in sipSorceryEntities.SIPAccounts where sa.ID == sipAccount.ID && sa.Owner.ToLower() == authUser.ToLower() select sa).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The SIP account to delete could not be found.");
                }
                else if (existingAccount.Owner.ToLower() != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to delete the SIP Account.");
                }

                sipSorceryEntities.SIPAccounts.Remove(existingAccount);
                sipSorceryEntities.SaveChanges();
            }
        }

        public void DeleteSIPAccount(string authUser, string sipAccountID)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                SIPAccount existingAccount = (from sa in sipSorceryEntities.SIPAccounts where sa.ID == sipAccountID && sa.Owner.ToLower() == authUser.ToLower() select sa).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The SIP account to delete could not be found.");
                }
                else if (existingAccount.Owner.ToLower() != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to delete the SIP Account.");
                }

                sipSorceryEntities.SIPAccounts.Remove(existingAccount);
                sipSorceryEntities.SaveChanges();
            }
        }

        public int GetSIPRegistrarBindingsCount(string authUser, string where)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSIPRegistrarBindingsCount.");
            }

            logger.Debug("GetSIPRegistrarBindingsCount for " + authUser + " and where=" + where + ".");

            using (var entities = new SIPSorceryEntities())
            {
                var query = (from bind in entities.SIPRegistrarBindings where bind.Owner.ToLower() == authUser.ToLower() select bind);

                if (where != null)
                {
                    query = query.Where(DynamicExpression.ParseLambda<SIPRegistrarBinding, bool>(where));
                }

                return query.Count();
            }
        }

        public IQueryable<SIPRegistrarBinding> GetSIPRegistrarBindings(string authUser)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for SIPRegistrarBindings.");
            }

            return new SIPSorceryEntities().SIPRegistrarBindings.Where(x => x.Owner.ToLower() == authUser.ToLower());
        }

        public List<SIPRegistrarBinding> GetSIPRegistrarBindings(string authUser, string where, int offset, int count)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSIPRegistrarBindings.");
            }

            using (var entities = new SIPSorceryEntities())
            {
                var query = (from bind in entities.SIPRegistrarBindings where bind.Owner.ToLower() == authUser.ToLower() select bind);

                if (where != null)
                {
                    query = query.Where(DynamicExpression.ParseLambda<SIPRegistrarBinding, bool>(where));
                }

                return query.AsEnumerable().OrderBy(x => x.SIPAccountName).Skip(offset).Take(count).ToList();
            }
        }

        #endregion

        #region SIP Providers.

        public int GetSIPProvidersCount(string authUser, string where)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSIPProvidersCount.");
            }

            using (var entities = new SIPSorceryEntities())
            {
                var query = (from sipProvider in entities.SIPProviders where sipProvider.Owner.ToLower() == authUser.ToLower() select sipProvider);

                if (where != null)
                {
                    query = query.Where(DynamicExpression.ParseLambda<SIPProvider, bool>(where));
                }

                return query.Count();
            }
        }

        public IQueryable<SIPProvider> GetSIPProviders(string authUser)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSIPProviders.");
            }

            return new SIPSorceryEntities().SIPProviders.Where(x => x.Owner.ToLower() == authUser.ToLower());
        }

        public List<SIPProvider> GetSIPProviders(string authUser, string where, int offset, int count)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSIPProviders.");
            }

            using (var entities = new SIPSorceryEntities())
            {
                var query = (from sipProvider in entities.SIPProviders where sipProvider.Owner.ToLower() == authUser.ToLower() select sipProvider);

                if (where != null)
                {
                    query = query.Where(DynamicExpression.ParseLambda<SIPProvider, bool>(where));
                }

                return query.AsEnumerable().OrderBy(x => x.ProviderName).Skip(offset).Take(count).ToList();
            }
        }

        public void InsertSIPProvider(string authUser, SIPProvider sipProvider)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for InsertSIPProvider.");
            }

            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                string serviceLevel = (from cust in sipSorceryEntities.Customers where cust.Name.ToLower() == authUser.ToLower() select cust.ServiceLevel).FirstOrDefault();

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
                    FixProviderRegisterDetails(sipProvider, authUser);
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

                sipSorceryEntities.SIPProviders.Add(sipProvider);
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
                existingAccount = (from sp in sipSorceryEntities.SIPProviders where sp.ID == sipProvider.ID && sp.Owner.ToLower() == authUser.ToLower() select sp).FirstOrDefault();

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
                existingAccount.SendMWISubscribe = sipProvider.SendMWISubscribe;

                if (existingAccount.RegisterEnabled)
                {
                    FixProviderRegisterDetails(existingAccount, authUser);
                }

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
                SIPProvider existingAccount = (from sp in sipSorceryEntities.SIPProviders where sp.ID == sipProvider.ID && sp.Owner.ToLower() == authUser.ToLower() select sp).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The SIP Provider to delete could not be found.");
                }
                else if (existingAccount.Owner.ToLower() != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to delete the SIP Provider.");
                }

                sipSorceryEntities.SIPProviders.Remove(existingAccount);
                sipSorceryEntities.SaveChanges();
            }
        }

        public void DeleteSIPProvider(string authUser, string sipProviderID)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                SIPProvider existingAccount = (from sp in sipSorceryEntities.SIPProviders where sp.ID == sipProviderID && sp.Owner.ToLower() == authUser.ToLower() select sp).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The SIP Provider to delete could not be found.");
                }
                else if (existingAccount.Owner.ToLower() != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to delete the SIP Provider.");
                }

                sipSorceryEntities.SIPProviders.Remove(existingAccount);
                sipSorceryEntities.SaveChanges();
            }
        }

        public int GetSIPProviderBindingsCount(string authUser, string where)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSIPProviderBindingsCount.");
            }

            logger.Debug("GetSIPProviderBindingsCount for " + authUser + " and where=" + where + ".");

            using (var entities = new SIPSorceryEntities())
            {
                var query = (from bind in entities.SIPProviderBindings where bind.Owner.ToLower() == authUser.ToLower() select bind);

                if (where != null)
                {
                    query = query.Where(DynamicExpression.ParseLambda<SIPProviderBinding, bool>(where));
                }

                return query.Count();
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

        public List<SIPProviderBinding> GetSIPProviderBindings(string authUser, string where, int offset, int count)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSIPProviderBindings.");
            }

            using (var entities = new SIPSorceryEntities())
            {
                var query = (from bind in entities.SIPProviderBindings where bind.Owner.ToLower() == authUser.ToLower() select bind);

                if (where != null)
                {
                    query = query.Where(DynamicExpression.ParseLambda<SIPProviderBinding, bool>(where));
                }

                return query.AsEnumerable().OrderBy(x => x.ProviderName).Skip(offset).Take(count).ToList();
            }
        }

        /// <summary>
        /// Fixes up the register contact and expiry fields on provider records that have the register box checked.
        /// </summary>
        private void FixProviderRegisterDetails(SIPProvider sipProvider, string owner)
        {
            if (!m_disabledRegisterProviderServerPattern.IsNullOrBlank() && Regex.Match(sipProvider.ProviderServer, m_disabledRegisterProviderServerPattern).Success)
            {
                throw new ApplicationException("Registrations are not supported with this provider. Please uncheck the Register box.");
            }

            if (sipProvider.RegisterContact.IsNullOrBlank())
            {
                sipProvider.RegisterContact = "sip:" + sipProvider.ProviderName + "." + owner.ToLower() + "@" + m_domainForProviderContact;
            }

            try
            {
                var registerURI = SIPURI.ParseSIPURIRelaxed(sipProvider.RegisterContact.Trim());
                registerURI.User = SIPEscape.SIPURIUserEscape(registerURI.User);
                sipProvider.RegisterContact = registerURI.ToString();
            }
            catch (Exception sipURIExcp)
            {
                throw new ApplicationException(sipURIExcp.Message);
            }

            if (sipProvider.RegisterExpiry == null || sipProvider.RegisterExpiry < SIPProvider.REGISTER_MINIMUM_EXPIRY || sipProvider.RegisterExpiry > SIPProvider.REGISTER_MAXIMUM_EXPIRY)
            {
                sipProvider.RegisterExpiry = SIPProvider.REGISTER_DEFAULT_EXPIRY;
            }
        }

        #endregion

        #region SIP Dial Plan

        public int GetSIPDialPlansCount(string authUser, string where)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSIPDialPlansCount.");
            }

            using (var entities = new SIPSorceryEntities())
            {
                var query = (from dialPlan in entities.SIPDialPlans where dialPlan.Owner.ToLower() == authUser.ToLower() select dialPlan);

                if (where != null)
                {
                    query = query.Where(DynamicExpression.ParseLambda<SIPDialPlan, bool>(where));
                }

                return query.Count();
            }
        }

        public IQueryable<SIPDialPlan> GetSIPSIPDialPlans(string authUser)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSIPSIPDialPlans.");
            }

            return new SIPSorceryEntities().SIPDialPlans.Where(x => x.Owner.ToLower() == authUser.ToLower());
        }

        public List<SIPDialPlan> GetSIPSIPDialPlans(string authUser, string where, int offset, int count)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetSIPSIPDialPlans.");
            }

            using (var entities = new SIPSorceryEntities())
            {
                var query = (from dialPlan in entities.SIPDialPlans where dialPlan.Owner.ToLower() == authUser.ToLower() select dialPlan);

                if (where != null)
                {
                    query = query.Where(DynamicExpression.ParseLambda<SIPDialPlan, bool>(where));
                }

                return query.AsEnumerable().OrderBy(x => x.DialPlanName).Skip(offset).Take(count).ToList();
            }
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
                string serviceLevel = (from cust in sipSorceryEntities.Customers where cust.Name.ToLower() == authUser.ToLower() select cust.ServiceLevel).FirstOrDefault();

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
                    SIPDialplanOption options = sipSorceryEntities.SIPDialplanOptions.Create();
                    options.ID = Guid.NewGuid().ToString();
                    options.Owner = sipDialPlan.Owner;
                    options.DialPlanID = sipDialPlan.ID;
                    sipSorceryEntities.SIPDialplanOptions.Add(options);
                }
                if (sipDialPlan.ScriptType == SIPDialPlanScriptTypesEnum.SimpleWizard)
                {
                    // Set the default script.
                    sipDialPlan.DialPlanScript = "require 'simplewizard'";
                }

                sipSorceryEntities.SIPDialPlans.Add(sipDialPlan);
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
                SIPDialPlan existingAccount = (from dp in sipSorceryEntities.SIPDialPlans where dp.ID == sipDialPlan.ID && dp.Owner.ToLower() == authUser.ToLower() select dp).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The SIP Dial Plan to update could not be found.");
                }
                else if (existingAccount.Owner.ToLower() != authUser.ToLower())
                {
                    logger.Warn("User " + authUser + " was not authorised to update dial plan " + existingAccount.DialPlanName + " belonging to " + existingAccount.Owner + ".");
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
                //existingAccount.ScriptTypeDescription = sipDialPlan.ScriptTypeDescription;
                existingAccount.AcceptNonInvite = sipDialPlan.AcceptNonInvite;

                if (existingAccount.DialPlanName != sipDialPlan.DialPlanName)
                {
                    // Need to update the SIP accounts using the dial plan.
                    string dialPlanName = existingAccount.DialPlanName;

                    UpdateSIPAccountsDialPlanName(sipSorceryEntities, authUser, existingAccount.DialPlanName, sipDialPlan.DialPlanName);

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
                SIPDialPlan existingAccount = (from dp in sipSorceryEntities.SIPDialPlans where dp.ID == sipDialPlan.ID && dp.Owner.ToLower() == authUser.ToLower() select dp).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The SIP Dial Plan to delete could not be found.");
                }
                else if (existingAccount.Owner.ToLower() != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to delete the SIP Dial Plan.");
                }

                sipSorceryEntities.SIPDialPlans.Remove(existingAccount);
                sipSorceryEntities.SaveChanges();
            }
        }

        public void DeleteSIPDialPlan(string authUser, string sipDialPlanID)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                SIPDialPlan existingAccount = (from dp in sipSorceryEntities.SIPDialPlans where dp.ID == sipDialPlanID && dp.Owner.ToLower() == authUser.ToLower() select dp).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The SIP Dial Plan to delete could not be found.");
                }
                else if (existingAccount.Owner.ToLower() != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to delete the SIP Dial Plan.");
                }

                sipSorceryEntities.SIPDialPlans.Remove(existingAccount);
                sipSorceryEntities.SaveChanges();
            }
        }

        public void CopySIPDialPlan(string authUser, string sipDialPlanID)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for CopySIPDialPlan.");
            }

            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                SIPDialPlan existingAccount = (from dp in sipSorceryEntities.SIPDialPlans where dp.ID == sipDialPlanID && dp.Owner.ToLower() == authUser.ToLower() select dp).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The SIP Dial Plan to copy could not be found.");
                }
                else if (existingAccount.Owner.ToLower() != authUser.ToLower())
                {
                    logger.Warn("User " + authUser + " was not authorised to copy dial plan " + existingAccount.DialPlanName + " belonging to " + existingAccount.Owner + ".");
                    throw new ApplicationException("Not authorised to copy the SIP Dial Plan.");
                }
                else if (existingAccount.IsReadOnly)
                {
                    throw new ApplicationException("This Dial Plan is read-only. Please upgrade to a Premium service to enable it.");
                }

                logger.Debug("Copying SIP dialplan " + existingAccount.DialPlanName + " for " + existingAccount.Owner + ".");

                string newDialPlanName = existingAccount.DialPlanName + " Copy";

                if (sipSorceryEntities.SIPDialPlans.Any(x => x.DialPlanName.ToLower() == newDialPlanName.ToLower() && x.Owner.ToLower() == authUser.ToLower()))
                {
                    int attempts = 2;
                    string newNameAttempt = newDialPlanName + " " + attempts.ToString();

                    while (sipSorceryEntities.SIPDialPlans.Any(x => x.DialPlanName.ToLower() == newNameAttempt.ToLower() && x.Owner.ToLower() == authUser.ToLower()) && attempts < 10)
                    {
                        attempts++;
                        newNameAttempt = newDialPlanName + " " + attempts.ToString();
                    }

                    if (attempts < 10)
                    {
                        newDialPlanName = newNameAttempt;
                    }
                    else
                    {
                        throw new ApplicationException("A new dial plan name could not be created for a dial plan copy operation.");
                    }
                }

                SIPDialPlan copy = new SIPDialPlan();
                copy.ID = Guid.NewGuid().ToString();
                copy.Owner = authUser.ToLower();
                copy.AdminMemberId = existingAccount.AdminMemberId;
                copy.Inserted = DateTimeOffset.UtcNow.ToString("o");
                copy.LastUpdate = DateTimeOffset.UtcNow.ToString("o");
                copy.MaxExecutionCount = SIPDialPlan.DEFAULT_MAXIMUM_EXECUTION_COUNT;
                copy.DialPlanName = newDialPlanName;
                copy.DialPlanScript = existingAccount.DialPlanScript;
                copy.ScriptTypeDescription = existingAccount.ScriptTypeDescription;
                copy.AuthorisedApps = existingAccount.AuthorisedApps;
                copy.AcceptNonInvite = existingAccount.AcceptNonInvite;

                sipSorceryEntities.SIPDialPlans.Add(copy);
                //sipSorceryEntities.SaveChanges();

                logger.Debug("A new dial plan copy was created for " + existingAccount.DialPlanName + ", new dial plan name " + copy.DialPlanName + ".");

                if (existingAccount.ScriptType == SIPDialPlanScriptTypesEnum.SimpleWizard)
                {
                    var simpleWizardRules = sipSorceryEntities.SimpleWizardRules.Where(x => x.Owner.ToLower() == authUser.ToLower() && x.DialPlanID == existingAccount.ID);

                    if (simpleWizardRules != null && simpleWizardRules.Count() > 0)
                    {
                        foreach (var rule in simpleWizardRules)
                        {
                            SimpleWizardRule copiedRule = new SimpleWizardRule();
                            copiedRule.ID = Guid.NewGuid().ToString();
                            copiedRule.DialPlanID = copy.ID;
                            copiedRule.Owner = authUser.ToLower();
                            copiedRule.ToMatchType = rule.ToMatchType;
                            copiedRule.ToMatchParameter = rule.ToMatchParameter;
                            copiedRule.Description = rule.Description;
                            copiedRule.Command = rule.Command;
                            copiedRule.CommandParameter1 = rule.CommandParameter1;
                            copiedRule.CommandParameter2 = rule.CommandParameter2;
                            copiedRule.CommandParameter3 = rule.CommandParameter3;
                            copiedRule.CommandParameter4 = rule.CommandParameter4;
                            copiedRule.Direction = rule.Direction;
                            copiedRule.PatternType = rule.PatternType;
                            copiedRule.Pattern = rule.Pattern;
                            copiedRule.Priority = rule.Priority;
                            copiedRule.IsDisabled = rule.IsDisabled;
                            copiedRule.TimePattern = rule.TimePattern;
                            copiedRule.ToSIPAccount = rule.ToSIPAccount;
                            copiedRule.ToProvider = rule.ToProvider;

                            sipSorceryEntities.SimpleWizardRules.Add(copiedRule);

                            logger.Debug("Copied simple wizard rule priority " + rule.Priority + " to dial plan " + copy.DialPlanName + ".");
                        }
                    }
                }

                sipSorceryEntities.SaveChanges();
            }
        }

        public void ChangeSIPDialPlanName(string authUser, string sipDialPlanID, string name)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for ChangeSIPDialPlanName.");
            }
            else if (name.IsNullOrBlank())
            {
                throw new ArgumentNullException("The new name cannot be empty in ChangeSIPDialPlanName.");
            }

            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                SIPDialPlan existingAccount = (from dp in sipSorceryEntities.SIPDialPlans where dp.ID == sipDialPlanID && dp.Owner.ToLower() == authUser.ToLower() select dp).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The SIP Dial Plan to change the name for could not be found.");
                }
                else if (existingAccount.Owner.ToLower() != authUser.ToLower())
                {
                    logger.Warn("User " + authUser + " was not authorised to change dial plan " + existingAccount.DialPlanName + " belonging to " + existingAccount.Owner + ".");
                    throw new ApplicationException("Not authorised to change the SIP Dial Plan name.");
                }
                else if (existingAccount.IsReadOnly)
                {
                    throw new ApplicationException("This Dial Plan is read-only. Please upgrade to a Premium service to enable it.");
                }

                logger.Debug("Changing the SIP dialplan " + existingAccount.DialPlanName + " for " + existingAccount.Owner + " to " + name + ".");

                if (sipSorceryEntities.SIPDialPlans.Any(x => x.DialPlanName.ToLower() == name.ToLower() && x.Owner.ToLower() == authUser.ToLower()))
                {
                    throw new ApplicationException("There is already a dialplan with the same name. Please choose something different.");
                }

                // Need to update any SIP accounts that are using the old dialplan name.
                UpdateSIPAccountsDialPlanName(sipSorceryEntities, authUser, existingAccount.DialPlanName, name);

                existingAccount.DialPlanName = name;

                sipSorceryEntities.SaveChanges();
            }
        }

        private void UpdateSIPAccountsDialPlanName(SIPSorceryEntities dbContext, string owner, string oldDialPlanName, string newDialPlanName)
        {
            var sipAccounts = (from sa in dbContext.SIPAccounts
                               where
                                   (sa.OutDialPlanName == oldDialPlanName || sa.InDialPlanName == oldDialPlanName)
                                   && sa.Owner.ToLower() == owner.ToLower()
                               select sa).ToList();

            foreach (SIPAccount sipAccount in sipAccounts)
            {
                if (sipAccount.InDialPlanName == oldDialPlanName)
                {
                    logger.Debug("SIP dialplan name updated; updating in dialplan on SIP account" + sipAccount.SIPUsername + " for " + owner + " to " + newDialPlanName + ".");
                    sipAccount.InDialPlanName = newDialPlanName;
                }

                if (sipAccount.OutDialPlanName == oldDialPlanName)
                {
                    logger.Debug("SIP dialplan name updated; updating out dialplan on SIP account" + sipAccount.SIPUsername + " for " + owner + " to " + newDialPlanName + ".");
                    sipAccount.OutDialPlanName = newDialPlanName;
                }
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

            return new SIPSorceryEntities().SimpleWizardRules.Where(x => x.Owner.ToLower() == authUser.ToLower());
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
                // ToDo Check.
                //if (rule.EntityState != EntityState.Detached)
                //{
                //    sipSorceryEntities.ObjectStateManager.ChangeObjectState(rule, EntityState.Added);
                //}
                //else
                //{
                    sipSorceryEntities.SimpleWizardRules.Add(rule);
                //}

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
                existingRule = (from ru in sipSorceryEntities.SimpleWizardRules where ru.ID == rule.ID && ru.Owner.ToLower() == authUser.ToLower() select ru).FirstOrDefault();

                if (existingRule == null)
                {
                    throw new ApplicationException("The Simple Wizard rule to update could not be found.");
                }
                else if (existingRule.Owner.ToLower() != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to update the Simple Wizard rule.");
                }

                //existingRule.ToProvider = rule.ToProvider;
                //existingRule.ToSIPAccount = rule.ToSIPAccount;
                existingRule.ToMatchType = rule.ToMatchType;
                existingRule.ToMatchParameter = rule.ToMatchParameter;
                existingRule.Description = rule.Description;
                existingRule.Command = rule.Command;
                existingRule.CommandParameter1 = rule.CommandParameter1;
                existingRule.CommandParameter2 = rule.CommandParameter2;
                existingRule.CommandParameter3 = rule.CommandParameter3;
                existingRule.CommandParameter4 = rule.CommandParameter4;
                existingRule.Direction = rule.Direction;
                existingRule.PatternType = rule.PatternType;
                existingRule.Pattern = rule.Pattern;
                existingRule.Priority = rule.Priority;
                existingRule.IsDisabled = rule.IsDisabled;
                existingRule.TimePattern = rule.TimePattern;

                sipSorceryEntities.SaveChanges();
            }
        }

        public void DeleteSimpleWizardRule(string authUser, SimpleWizardRule rule)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                SimpleWizardRule existingRule = (from ru in sipSorceryEntities.SimpleWizardRules where ru.ID == rule.ID && ru.Owner.ToLower() == authUser.ToLower() select ru).FirstOrDefault();

                if (existingRule == null)
                {
                    throw new ApplicationException("The Simple Wizard Rule to delete could not be found.");
                }
                else if (existingRule.Owner.ToLower() != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to delete the Simple Wizard Rule.");
                }

                sipSorceryEntities.SimpleWizardRules.Remove(existingRule);
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
                entities.Database.CommandTimeout = DEFAULT_COMMAND_TIMEOUT;

                var query = (from cdr in entities.CDRs where cdr.Owner.ToLower() == authUser.ToLower() select cdr);

                if (where != null)
                {
                    // The CDR entity uses Direction where the JSON uses CallDirection.
                    where = Regex.Replace(where, "calldirection", "Direction", RegexOptions.IgnoreCase);

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
                entities.Database.CommandTimeout = DEFAULT_COMMAND_TIMEOUT;

                var query = (from cdr in entities.CDRs.Include("rtccs") where cdr.Owner.ToLower() == authUser.ToLower() select cdr);

                if (where != null)
                {
                    // The CDR entity uses Direction where the JSON uses CallDirection.
                    where = Regex.Replace(where, "calldirection", "Direction", RegexOptions.IgnoreCase);

                    query = query.Where(DynamicExpression.ParseLambda<CDR, bool>(where));
                }

                return query.AsEnumerable().OrderByDescending(x => x.Created).Skip(offset).Take(count).ToList();
            }
        }

        #endregion

        #region Web Callbacks

        public IQueryable<WebCallback> GetWebCallbacks(string authUser)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetWebCallbacks.");
            }

            return new SIPSorceryEntities().WebCallbacks.Where(x => x.Owner.ToLower() == authUser.ToLower());
        }

        public void InsertWebCallback(string authUser, WebCallback webcallback)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for InsertWebCallback.");
            }

            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                webcallback.ID = Guid.NewGuid().ToString();
                webcallback.Owner = authUser.ToLower();
                webcallback.Inserted = DateTimeOffset.UtcNow.ToString("o");

                sipSorceryEntities.WebCallbacks.Add(webcallback);
                sipSorceryEntities.SaveChanges();
            }
        }

        public void UpdateWebCallback(string authUser, WebCallback webcallback)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for UpdateWebCallback.");
            }

            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                var existingAccount = (from wc in sipSorceryEntities.WebCallbacks where wc.ID == webcallback.ID && wc.Owner.ToLower() == authUser.ToLower() select wc).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The web callback to update could not be found.");
                }
                else if (existingAccount.Owner.ToLower() != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to update the web callback.");
                }

                logger.Debug("Updating web callback " + existingAccount.Description + " for " + existingAccount.Owner + ".");

                existingAccount.DialString1 = webcallback.DialString1;
                existingAccount.DialString2 = webcallback.DialString2;
                existingAccount.Description = webcallback.Description;

                sipSorceryEntities.SaveChanges();
            }
        }

        public void DeleteWebCallback(string authUser, WebCallback webCallback)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                var existingAccount = (from wc in sipSorceryEntities.WebCallbacks where wc.ID == webCallback.ID && wc.Owner.ToLower() == authUser.ToLower() select wc).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The web callback to delete could not be found.");
                }
                else if (existingAccount.Owner.ToLower() != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to delete the web callback.");
                }

                sipSorceryEntities.WebCallbacks.Remove(existingAccount);
                sipSorceryEntities.SaveChanges();
            }
        }

        #endregion

        #region Customer Accounts

        public int GetCustomerAccountsCount(string authUser, string where)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetCustomerAccountsCount.");
            }

            using (var entities = new SIPSorceryEntities())
            {
                var query = (from acc in entities.CustomerAccounts where acc.Owner.ToLower() == authUser.ToLower() select acc);

                if (where != null)
                {
                    query = query.Where(DynamicExpression.ParseLambda<CustomerAccount, bool>(where));
                }

                return query.Count();
            }
        }

        public IQueryable<CustomerAccount> GetCustomerAccounts(string authUser)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetCustomerAccounts.");
            }

            return new SIPSorceryEntities().CustomerAccounts.Where(x => x.Owner.ToLower() == authUser.ToLower());
        }

        public List<CustomerAccount> GetCustomerAccounts(string authUser, string where, int offset, int count)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetCustomerAccounts.");
            }

            using (var entities = new SIPSorceryEntities())
            {
                var query = (from acc in entities.CustomerAccounts where acc.Owner.ToLower() == authUser.ToLower() select acc);

                if (where != null)
                {
                    query = query.Where(DynamicExpression.ParseLambda<CustomerAccount, bool>(where));
                }

                return query.AsEnumerable().OrderBy(x => x.AccountCode).Skip(offset).Take(count).ToList();
            }
        }

        public void InsertCustomerAccount(string authUser, CustomerAccount customerAccount)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for InsertCustomerAccount.");
            }

            customerAccount.Owner = authUser;
            _customerAccountDataLayer.Add(customerAccount);
        }

        public void UpdateCustomerAccount(string authUser, CustomerAccount customerAccount)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for UpdateCustomerAccount.");
            }

            customerAccount.Owner = authUser;
            _customerAccountDataLayer.Update(customerAccount);
        }

        public void DeleteCustomerAccount(string authUser, CustomerAccount customerAccount)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                var existingAccount = (from ca in sipSorceryEntities.CustomerAccounts where ca.ID == customerAccount.ID && ca.Owner.ToLower() == authUser.ToLower() select ca).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The customer account to delete could not be found.");
                }
                else if (existingAccount.Owner.ToLower() != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to delete the customer account.");
                }

                sipSorceryEntities.CustomerAccounts.Remove(existingAccount);
                sipSorceryEntities.SaveChanges();
            }
        }

        public void DeleteCustomerAccount(string authUser, string customerAccountID)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                var existingAccount = (from ca in sipSorceryEntities.CustomerAccounts where ca.ID == customerAccountID && ca.Owner.ToLower() == authUser.ToLower() select ca).FirstOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The customer account to delete could not be found.");
                }
                else if (existingAccount.Owner.ToLower() != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to delete the customer account.");
                }

                sipSorceryEntities.CustomerAccounts.Remove(existingAccount);
                sipSorceryEntities.SaveChanges();
            }
        }

        #endregion

        #region Rates

        public int GetRatesCount(string authUser, string where)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetRatesCount.");
            }

            using (var entities = new SIPSorceryEntities())
            {
                var query = (from rate in entities.Rates where rate.Owner.ToLower() == authUser.ToLower() select rate);

                if (where != null)
                {
                    query = query.Where(DynamicExpression.ParseLambda<Rate, bool>(where));
                }

                return query.Count();
            }
        }

        public IQueryable<Rate> GetRates(string authUser)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetRates.");
            }

            return new SIPSorceryEntities().Rates.Where(x => x.Owner.ToLower() == authUser.ToLower());
        }

        public List<Rate> GetRates(string authUser, string where, int offset, int count)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for GetRates.");
            }

            using (var entities = new SIPSorceryEntities())
            {
                var query = (from rate in entities.Rates where rate.Owner.ToLower() == authUser.ToLower() select rate);

                if (where != null)
                {
                    query = query.Where(DynamicExpression.ParseLambda<Rate, bool>(where));
                }

                return query.AsEnumerable().OrderBy(x => x.Description).Skip(offset).Take(count).ToList();
            }
        }

        public void InsertRate(string authUser, Rate rate)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for InsertRate.");
            }

            rate.Owner = authUser;
            _rateDataLayer.Add(rate);
        }

        public void UpdateRate(string authUser, Rate rate)
        {
            if (authUser.IsNullOrBlank())
            {
                throw new ArgumentException("An authenticated user is required for UpdateRate.");
            }

            rate.Owner = authUser;
            _rateDataLayer.Update(rate);
        }

        public void DeleteRate(string authUser, Rate rate)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                var existingRate = (from ra in sipSorceryEntities.Rates where ra.ID == rate.ID && ra.Owner.ToLower() == authUser.ToLower() select ra).FirstOrDefault();

                if (existingRate == null)
                {
                    throw new ApplicationException("The rate to delete could not be found.");
                }
                else if (existingRate.Owner.ToLower() != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to delete the rate.");
                }

                sipSorceryEntities.Rates.Remove(existingRate);
                sipSorceryEntities.SaveChanges();
            }
        }

        public void DeleteRate(string authUser, string rateID)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                var existingRate = (from ra in sipSorceryEntities.Rates where ra.ID == rateID && ra.Owner.ToLower() == authUser.ToLower() select ra).FirstOrDefault();

                if (existingRate == null)
                {
                    throw new ApplicationException("The rate to delete could not be found.");
                }
                else if (existingRate.Owner.ToLower() != authUser.ToLower())
                {
                    throw new ApplicationException("Not authorised to delete the rate.");
                }

                sipSorceryEntities.Rates.Remove(existingRate);
                sipSorceryEntities.SaveChanges();
            }
        }

        #endregion

        #region Key-Value Store.

        public void DBWrite(string authUsername, string key, string value)
        {
            using (var entities = new SIPSorceryEntities())
            {
                var existingEntry = entities.DialPlanData.Where(x => x.DataOwner == authUsername && x.DataKey == key).SingleOrDefault();

                if (existingEntry != null)
                {
                    existingEntry.DataValue = value;
                }
                else
                {
                    var newEntry = new DialPlanDataEntry() { DataOwner = authUsername, DataKey = key, DataValue = value };
                    entities.DialPlanData.Add(newEntry);
                }

                entities.SaveChanges();
            }
        }

        public void DBDelete(string authUsername, string key)
        {
            using (var entities = new SIPSorceryEntities())
            {
                var existingEntry = entities.DialPlanData.Where(x => x.DataOwner == authUsername && x.DataKey == key).SingleOrDefault();

                if (existingEntry != null)
                {
                    entities.DialPlanData.Remove(existingEntry);
                    entities.SaveChanges();
                }
            }
        }

        public string DBRead(string authUsername, string key)
        {
            using (var entities = new SIPSorceryEntities())
            {
                var existingEntry = entities.DialPlanData.Where(x => x.DataOwner == authUsername && x.DataKey == key).SingleOrDefault();

                if (existingEntry != null)
                {
                    return existingEntry.DataValue;
                }
                else
                {
                    return null;
                }
            }
        }

        public List<string> DBGetKeys(string authUsername)
        {
            using (var entities = new SIPSorceryEntities())
            {
                return (from entry in entities.DialPlanData where entry.DataOwner == authUsername select entry.DataKey).ToList();
            }
        }

        #endregion
    }
}
