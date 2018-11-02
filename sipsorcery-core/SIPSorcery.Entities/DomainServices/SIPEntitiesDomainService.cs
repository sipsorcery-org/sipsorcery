using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Entity.Core;
using System.Data.Entity.Core.Objects;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.DomainServices.EntityFramework;
using System.ServiceModel.DomainServices.Hosting;
using System.ServiceModel.DomainServices.Server;
using System.ServiceModel.DomainServices.Server.ApplicationServices;
using System.Web;
using System.Web.Security;
using System.Web.Configuration;
using SIPSorcery.Entities;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Entities.Services
{
    public class User : UserBase
    {
        // NOTE: Profile properties can be added here 
        // To enable profiles, edit the appropriate section of web.config file.

        // public string MyProfileProperty { get; set; }
    }

    [EnableClientAccess(RequiresSecureEndpoint = true)]
    public class SIPEntitiesDomainService : LinqToEntitiesDomainService<SIPSorceryEntities>, IAuthentication<User>
    {
        private static ILog logger = AppState.logger;

        SIPSorceryService m_service = new SIPSorceryService();

        public class AuthenticationDomainService : AuthenticationBase<User>
        {  }

        private AuthenticationDomainService m_authService = new AuthenticationDomainService();

        public bool IsAlive()
        {
            return true;
        }

        [Invoke]
        public List<string> GetTimeZones()
        {
            return m_service.GetTimeZones();
        }

        /// <param name="customData">If populated this field is used to indicate the login requested is to impersonate this username. It's only available
        /// to admin users.</param>
        public User Login(string username, string password, bool isPersistent, string customData)
        {
            if (!customData.IsNullOrBlank())
            {
                // An impersonation login has been requested. The requesting user MUST be an administrator.
                using (var sipSorceryEntities = new SIPSorceryEntities())
                {
                    var admin = sipSorceryEntities.Customers.SingleOrDefault(x => x.Name.ToLower() == username.ToLower() && x.AdminID == Customer.TOPLEVEL_ADMIN_ID);

                    if (admin != null && PasswordHash.Hash(password, admin.Salt) == admin.CustomerPassword)
                    {
                        var impersonateCustomer = sipSorceryEntities.Customers.Where(x => x.Name.ToLower() == customData.ToLower() || x.EmailAddress.ToLower() == customData.ToLower()).Single();
                        //return m_authService.Impersonate(impersonateCustomer.Name);

                        FormsAuthenticationTicket ticket = new FormsAuthenticationTicket(
                                                  2,
                                                  impersonateCustomer.Name,
                                                  DateTime.Now,
                                                  DateTime.Now.AddMinutes(FormsAuthentication.Timeout.TotalMinutes),
                                                  true,
                                                  string.Empty,
                                                  FormsAuthentication.FormsCookiePath);

                        string encryptedTicket = FormsAuthentication.Encrypt(ticket);
                        HttpCookie cookie = new HttpCookie(FormsAuthentication.FormsCookieName, encryptedTicket);
                        cookie.Expires = DateTime.Now.AddMinutes(FormsAuthentication.Timeout.TotalMinutes);
                        HttpContext.Current.Response.Cookies.Add(cookie);

                        return new User() { Name = impersonateCustomer.Name };
                    }
                    else
                    {
                        throw new ApplicationException("You are not authorised.");
                    }
                }
            }
            else
            {
                return m_authService.Login(username, password, isPersistent, customData);
            }
        }

        public void InsertCustomer(Customer customer)
        {
            m_service.InsertCustomer(customer);
        }

        [RequiresAuthentication]
        public User GetUser()
        {
            throw new NotImplementedException();
        }

        [RequiresAuthentication]
        public User Logout()
        {
            return m_authService.Logout();
        }

        [RequiresAuthentication]
        public void UpdateUser(User user)
        {
            throw new NotImplementedException();
        }

        [RequiresAuthentication]
        public IQueryable<CDR> GetCDRs()
        {
            return this.ObjectContext.CDRs.Where(x => x.Owner == this.ServiceContext.User.Identity.Name);
        }

        [RequiresAuthentication]
        public Customer GetCustomer()
        {
            var customer = this.ObjectContext.Customers.Where(x => x.Name == this.ServiceContext.User.Identity.Name).FirstOrDefault();
            customer.TimeZoneOffsetMinutes = GetCustomerTimeZoneOffsetMinutes(customer);
            return customer;
        }

        private int GetCustomerTimeZoneOffsetMinutes(Customer customer)
        {
            if (customer != null && !customer.Timezone.IsNullOrBlank())
            {
                return TimeZoneHelper.GetTimeZonesUTCOffsetMinutes(customer.Timezone);
            }

            return 0;
        }

        [RequiresAuthentication]
        public int GetTimeZoneOffsetMinutes()
        {
            try
            {
                Customer customer = GetCustomer();
                return GetCustomerTimeZoneOffsetMinutes(customer);
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetTimeZoneOffsetMinutes. " + excp.Message);
                return 0;
            }
        }

        [RequiresAuthentication]
        public void UpdateCustomer(Customer currentCustomer)
        {
            if (currentCustomer.Name != this.ServiceContext.User.Identity.Name)
            {
                throw new ApplicationException("You are not authorised to update this record.");
            }
            else
            {
                //this.ObjectContext.Customers.AttachAsModified(currentCustomer, this.ChangeSet.GetOriginal(currentCustomer));
                m_service.UpdateCustomer(this.ServiceContext.User.Identity.Name, currentCustomer);
            }
        }

        [Invoke]
        [RequiresAuthentication]
        public void ChangePassword(string oldPassword, string newPassword)
        {
            Customer customer = this.ObjectContext.Customers.Where(x => x.Name == this.ServiceContext.User.Identity.Name).First();

            if (PasswordHash.Hash(oldPassword, customer.Salt) != customer.CustomerPassword)
            {
                throw new ApplicationException("The old password was not correct. Password was not updated.");
            }
            else
            {
                //customer.CustomerPassword = newPassword;

                string salt = PasswordHash.GenerateSalt();
                customer.CustomerPassword = PasswordHash.Hash(newPassword, salt);
                customer.Salt = salt;

                this.ObjectContext.SaveChanges();
            }
        }

        [RequiresAuthentication]
        public void DeleteCustomer(Customer customer)
        {
            if (customer.Name != this.ServiceContext.User.Identity.Name)
            {
                throw new ApplicationException("You are not authorised to delete this record.");
            }
            else
            {
                logger.Debug("User requested delete of " + customer.Name + ".");

                if ((customer.EntityState == EntityState.Detached))
                {
                    this.ObjectContext.Customers.Attach(customer);
                }
                this.ObjectContext.Customers.DeleteObject(customer);
            }
        }

        [RequiresAuthentication]
        [Query(IsDefault = true)]
        public IQueryable<SIPAccount> GetSIPAccounts()
        {
            //return this.ObjectContext.SIPAccounts.Where(x => x.Owner == this.ServiceContext.User.Identity.Name);
            return m_service.GetSIPAccounts(this.ServiceContext.User.Identity.Name);
        }

        [RequiresAuthentication]
        public void InsertSIPAccount(SIPAccount sipAccount)
        {
            //string validationError = SIPAccount.Validate(sipAccount);
            //if (validationError != null)
            //{
            //    throw new ApplicationException(validationError);
            //}

            //sipAccount.Owner = this.ServiceContext.User.Identity.Name;
            //sipAccount.Inserted = DateTime.UtcNow.ToString("o");

            //if ((sipAccount.EntityState != EntityState.Detached))
            //{
            //    this.ObjectContext.ObjectStateManager.ChangeObjectState(sipAccount, EntityState.Added);
            //}
            //else
            //{
            //    this.ObjectContext.SIPAccounts.AddObject(sipAccount);
            //}

            m_service.InsertSIPAccount(this.ServiceContext.User.Identity.Name, sipAccount);
        }

        [RequiresAuthentication]
        public void UpdateSIPAccount(SIPAccount sipAccount)
        {
            //if (sipAccount.Owner != this.ServiceContext.User.Identity.Name)
            //{
            //    throw new ApplicationException("You are not authorised to update this record.");
            //}
            //else
            //{
            //    string validationError = SIPAccount.Validate(sipAccount);
            //    if (validationError != null)
            //    {
            //        throw new ApplicationException(validationError);
            //    }

            //    this.ObjectContext.SIPAccounts.AttachAsModified(sipAccount, this.ChangeSet.GetOriginal(sipAccount));
            //}

            m_service.UpdateSIPAccount(this.ServiceContext.User.Identity.Name, sipAccount);
        }

        [RequiresAuthentication]
        public void DeleteSIPAccount(SIPAccount sipAccount)
        {
            //if (sipAccount.Owner != this.ServiceContext.User.Identity.Name)
            //{
            //    throw new ApplicationException("You are not authorised to delete this record.");
            //}
            //else
            //{
            //    if ((sipAccount.EntityState == EntityState.Detached))
            //    {
            //        this.ObjectContext.SIPAccounts.Attach(sipAccount);
            //    }
            //    this.ObjectContext.SIPAccounts.DeleteObject(sipAccount);
            //}

            m_service.DeleteSIPAccount(this.ServiceContext.User.Identity.Name, sipAccount);
        }

        [RequiresAuthentication]
        public IQueryable<SIPDialogue> GetSIPDialogues()
        {
            return this.ObjectContext.SIPDialogues.Where(x => x.Owner == this.ServiceContext.User.Identity.Name);
        }

        [RequiresAuthentication]
        public IQueryable<SIPDialPlan> GetSIPDialplans()
        {
            //return this.ObjectContext.SIPDialPlans.Where(x => x.Owner == this.ServiceContext.User.Identity.Name);

            return m_service.GetSIPSIPDialPlans(this.ServiceContext.User.Identity.Name);
        }

        [RequiresAuthentication]
        public void InsertSIPDialplan(SIPDialPlan sipDialplan)
        {
            //string serviceLevel = (from cust in this.ObjectContext.Customers where cust.Name == this.ServiceContext.User.Identity.Name select cust.ServiceLevel).FirstOrDefault();

            //if (!serviceLevel.IsNullOrBlank() && serviceLevel.ToLower() == CustomerServiceLevels.Free.ToString().ToLower())
            //{
            //    // Check the number of dialplans is within limits.
            //    if ((from dialplan in this.ObjectContext.SIPDialPlans where dialplan.Owner == this.ServiceContext.User.Identity.Name select dialplan).Count() >= DIALPLAN_COUNT_FREE_SERVICE)
            //    {
            //        throw new ApplicationException("The dial plan cannot be added as your existing dial plan count has reached the allowed limit for your service level."); 
            //    }
            //}

            //sipDialplan.Owner = this.ServiceContext.User.Identity.Name;
            //sipDialplan.Inserted = DateTime.UtcNow.ToString("o");
            //sipDialplan.MaxExecutionCount = SIPDialPlan.DEFAULT_MAXIMUM_EXECUTION_COUNT;

            //if (sipDialplan.ScriptType == SIPDialPlanScriptTypesEnum.TelisWizard)
            //{
            //    // Set the default script.
            //    sipDialplan.DialPlanScript = "require 'teliswizard'";
            //}
            //if (sipDialplan.ScriptType == SIPDialPlanScriptTypesEnum.SimpleWizard)
            //{
            //    // Set the default script.
            //    sipDialplan.DialPlanScript = "require 'simplewizard'";
            //}

            //if ((sipDialplan.EntityState != EntityState.Detached))
            //{
            //    this.ObjectContext.ObjectStateManager.ChangeObjectState(sipDialplan, EntityState.Added);
            //}
            //else
            //{
            //    this.ObjectContext.SIPDialPlans.AddObject(sipDialplan);
            //}

            //if (sipDialplan.ScriptType == SIPDialPlanScriptTypesEnum.TelisWizard)
            //{
            //    // Create a new SIP dialplan options record.
            //    SIPDialplanOption options = this.ObjectContext.SIPDialplanOptions.CreateObject();
            //    options.ID = Guid.NewGuid().ToString();
            //    options.Owner = sipDialplan.Owner;
            //    options.DialPlanID = sipDialplan.ID;
            //    this.ObjectContext.SIPDialplanOptions.AddObject(options);
            //}

            m_service.InsertSIPDialPlan(this.ServiceContext.User.Identity.Name, sipDialplan);
        }

        [RequiresAuthentication]
        public void UpdateSIPDialplan(SIPDialPlan currentSIPDialplan)
        {
            //if (currentSIPDialplan.Owner != this.ServiceContext.User.Identity.Name)
            //{
            //    throw new ApplicationException("You are not authorised to update this record.");
            //}
            //else
            //{
            //    currentSIPDialplan.LastUpdate = DateTimeOffset.UtcNow.ToString("o");
            //    this.ObjectContext.SIPDialPlans.AttachAsModified(currentSIPDialplan, this.ChangeSet.GetOriginal(currentSIPDialplan));
            //}

            m_service.UpdateSIPDialPlan(this.ServiceContext.User.Identity.Name, currentSIPDialplan);
        }

        [RequiresAuthentication]
        public void DeleteSIPDialplan(SIPDialPlan sipDialplan)
        {
            //if (sipDialplan.Owner != this.ServiceContext.User.Identity.Name)
            //{
            //    throw new ApplicationException("You are not authorised to delete this record.");
            //}
            //else
            //{
            //    if ((sipDialplan.EntityState == EntityState.Detached))
            //    {
            //        this.ObjectContext.SIPDialPlans.Attach(sipDialplan);
            //    }
            //    this.ObjectContext.SIPDialPlans.DeleteObject(sipDialplan);
            //}

            m_service.DeleteSIPDialPlan(this.ServiceContext.User.Identity.Name, sipDialplan);
        }

        [Invoke]
        [RequiresAuthentication]
        public void CopySIPDialplan(string sipDialplanID)
        {
            m_service.CopySIPDialPlan(this.ServiceContext.User.Identity.Name, sipDialplanID);
        }

        [Invoke]
        [RequiresAuthentication]
        public void ChangeSIPDialplanName(string sipDialplanID, string name)
        {
            m_service.ChangeSIPDialPlanName(this.ServiceContext.User.Identity.Name, sipDialplanID, name);
        }

        [RequiresAuthentication]
        public IQueryable<SIPDialplanLookup> GetSIPDialplanLookups()
        {
            return this.ObjectContext.SIPDialplanLookups.Where(x => x.Owner == this.ServiceContext.User.Identity.Name);
        }

        [RequiresAuthentication]
        public void InsertSIPDialplanLookup(SIPDialplanLookup sipDialplanLookup)
        {
            sipDialplanLookup.Owner = this.ServiceContext.User.Identity.Name;

            if ((sipDialplanLookup.EntityState != EntityState.Detached))
            {
                this.ObjectContext.ObjectStateManager.ChangeObjectState(sipDialplanLookup, EntityState.Added);
            }
            else
            {
                this.ObjectContext.SIPDialplanLookups.AddObject(sipDialplanLookup);
            }
        }

        [RequiresAuthentication]
        public void UpdateSIPDialplanLookup(SIPDialplanLookup currentSIPDialplanLookup)
        {
            if (currentSIPDialplanLookup.Owner != this.ServiceContext.User.Identity.Name)
            {
                throw new ApplicationException("You are not authorised to update this record.");
            }
            else
            {
                this.ObjectContext.SIPDialplanLookups.AttachAsModified(currentSIPDialplanLookup, this.ChangeSet.GetOriginal(currentSIPDialplanLookup));
            }
        }

        [RequiresAuthentication]
        public void DeleteSIPDialplanLookup(SIPDialplanLookup sipDialplanLookup)
        {
            if (sipDialplanLookup.Owner != this.ServiceContext.User.Identity.Name)
            {
                throw new ApplicationException("You are not authorised to delete this record.");
            }
            else
            {
                if ((sipDialplanLookup.EntityState == EntityState.Detached))
                {
                    this.ObjectContext.SIPDialplanLookups.Attach(sipDialplanLookup);
                }
                this.ObjectContext.SIPDialplanLookups.DeleteObject(sipDialplanLookup);
            }
        }

        [RequiresAuthentication]
        public IQueryable<SIPDialplanOption> GetSIPDialplanOptions()
        {
            return this.ObjectContext.SIPDialplanOptions.Where(x => x.Owner == this.ServiceContext.User.Identity.Name);
        }

        [RequiresAuthentication]
        public void InsertSIPDialplanOption(SIPDialplanOption sipDialplanOption)
        {
            sipDialplanOption.Owner = this.ServiceContext.User.Identity.Name;

            if ((sipDialplanOption.EntityState != EntityState.Detached))
            {
                this.ObjectContext.ObjectStateManager.ChangeObjectState(sipDialplanOption, EntityState.Added);
            }
            else
            {
                this.ObjectContext.SIPDialplanOptions.AddObject(sipDialplanOption);
            }
        }

        [RequiresAuthentication]
        public void UpdateSIPDialplanOption(SIPDialplanOption currentSIPDialplanOption)
        {
            if (currentSIPDialplanOption.Owner != this.ServiceContext.User.Identity.Name)
            {
                throw new ApplicationException("You are not authorised to update this record.");
            }
            else
            {
                this.ObjectContext.SIPDialplanOptions.AttachAsModified(currentSIPDialplanOption, this.ChangeSet.GetOriginal(currentSIPDialplanOption));
            }
        }

        [RequiresAuthentication]
        public void DeleteSIPDialplanOption(SIPDialplanOption sipDialplanOption)
        {
            if (sipDialplanOption.Owner != this.ServiceContext.User.Identity.Name)
            {
                throw new ApplicationException("You are not authorised to delete this record.");
            }
            else
            {
                if ((sipDialplanOption.EntityState == EntityState.Detached))
                {
                    this.ObjectContext.SIPDialplanOptions.Attach(sipDialplanOption);
                }
                this.ObjectContext.SIPDialplanOptions.DeleteObject(sipDialplanOption);
            }
        }

        [RequiresAuthentication]
        public IQueryable<SIPDialplanProvider> GetSIPDialplanProviders()
        {
            return this.ObjectContext.SIPDialplanProviders.Where(x => x.Owner == this.ServiceContext.User.Identity.Name);
        }

        [RequiresAuthentication]
        public void InsertSIPDialplanProvider(SIPDialplanProvider sipDialplanProvider)
        {
            sipDialplanProvider.Owner = this.ServiceContext.User.Identity.Name;

            if ((sipDialplanProvider.EntityState != EntityState.Detached))
            {
                this.ObjectContext.ObjectStateManager.ChangeObjectState(sipDialplanProvider, EntityState.Added);
            }
            else
            {
                this.ObjectContext.SIPDialplanProviders.AddObject(sipDialplanProvider);
            }
        }

        [RequiresAuthentication]
        public void UpdateSIPDialplanProvider(SIPDialplanProvider currentSIPDialplanProvider)
        {
            if (currentSIPDialplanProvider.Owner != this.ServiceContext.User.Identity.Name)
            {
                throw new ApplicationException("You are not authorised to update this record.");
            }
            else
            {
                this.ObjectContext.SIPDialplanProviders.AttachAsModified(currentSIPDialplanProvider, this.ChangeSet.GetOriginal(currentSIPDialplanProvider));
            }
        }

        [RequiresAuthentication]
        public void DeleteSIPDialplanProvider(SIPDialplanProvider sipDialplanProvider)
        {
            if (sipDialplanProvider.Owner != this.ServiceContext.User.Identity.Name)
            {
                throw new ApplicationException("You are not authorised to delete this record.");
            }
            else
            {
                if ((sipDialplanProvider.EntityState == EntityState.Detached))
                {
                    this.ObjectContext.SIPDialplanProviders.Attach(sipDialplanProvider);
                }
                this.ObjectContext.SIPDialplanProviders.DeleteObject(sipDialplanProvider);
            }
        }

        [RequiresAuthentication]
        public IQueryable<SIPDialplanRoute> GetSIPDialplanRoutes()
        {
            return this.ObjectContext.SIPDialplanRoutes.Where(x => x.Owner == this.ServiceContext.User.Identity.Name);
        }

        [RequiresAuthentication]
        public void InsertSIPDialplanRoute(SIPDialplanRoute sipDialplanRoute)
        {
            sipDialplanRoute.Owner = this.ServiceContext.User.Identity.Name;

            if ((sipDialplanRoute.EntityState != EntityState.Detached))
            {
                this.ObjectContext.ObjectStateManager.ChangeObjectState(sipDialplanRoute, EntityState.Added);
            }
            else
            {
                this.ObjectContext.SIPDialplanRoutes.AddObject(sipDialplanRoute);
            }
        }

        [RequiresAuthentication]
        public void UpdateSIPDialplanRoute(SIPDialplanRoute currentSIPDialplanRoute)
        {
            if (currentSIPDialplanRoute.Owner != this.ServiceContext.User.Identity.Name)
            {
                throw new ApplicationException("You are not authorised to update this record.");
            }
            else
            {
                this.ObjectContext.SIPDialplanRoutes.AttachAsModified(currentSIPDialplanRoute, this.ChangeSet.GetOriginal(currentSIPDialplanRoute));
            }
        }

        [RequiresAuthentication]
        public void DeleteSIPDialplanRoute(SIPDialplanRoute sipDialplanRoute)
        {
            if (sipDialplanRoute.Owner != this.ServiceContext.User.Identity.Name)
            {
                throw new ApplicationException("You are not authorised to delete this record.");
            }
            else
            {
                if ((sipDialplanRoute.EntityState == EntityState.Detached))
                {
                    this.ObjectContext.SIPDialplanRoutes.Attach(sipDialplanRoute);
                }
                this.ObjectContext.SIPDialplanRoutes.DeleteObject(sipDialplanRoute);
            }
        }

        [RequiresAuthentication]
        public IQueryable<SIPDomain> GetSIPDomains()
        {
            return this.ObjectContext.SIPDomains.Where(x => x.Owner == this.ServiceContext.User.Identity.Name || x.Owner == null);
        }

        [RequiresAuthentication]
        public IQueryable<SIPProvider> GetSIPProviders()
        {
            //return this.ObjectContext.SIPProviders.Where(x => x.Owner == this.ServiceContext.User.Identity.Name);
            return m_service.GetSIPProviders(this.ServiceContext.User.Identity.Name);
        }

        [RequiresAuthentication]
        public void InsertSIPProvider(SIPProvider sipProvider)
        {
            //string serviceLevel = (from cust in this.ObjectContext.Customers where cust.Name == this.ServiceContext.User.Identity.Name select cust.ServiceLevel).FirstOrDefault();

            //if (!serviceLevel.IsNullOrBlank() && serviceLevel.ToLower() == CustomerServiceLevels.Free.ToString().ToLower())
            //{
            //    // Check the number of SIP providers is within limits.
            //    if ((from provider in this.ObjectContext.SIPProviders where provider.Owner == this.ServiceContext.User.Identity.Name select provider).Count() >= PROVIDER_COUNT_FREE_SERVICE)
            //    {
            //        throw new ApplicationException("The SIP provider cannot be added as your existing SIP provider count has reached the allowed limit for your service level.");
            //    }
            //}

            //string validationError = SIPProvider.Validate(sipProvider);
            //if (validationError != null)
            //{
            //    throw new ApplicationException(validationError);
            //}
            ////else if (m_providerRegDisabled && sipProvider.RegisterEnabled)
            ////{
            ////    throw new ApplicationException("SIP provider registrations are disabled.");
            ////}

            //sipProvider.Owner = this.ServiceContext.User.Identity.Name;
            //sipProvider.Inserted = DateTimeOffset.UtcNow.ToString("o");

            //if ((sipProvider.EntityState != EntityState.Detached))
            //{
            //    this.ObjectContext.ObjectStateManager.ChangeObjectState(sipProvider, EntityState.Added);
            //}
            //else
            //{
            //    this.ObjectContext.SIPProviders.AddObject(sipProvider);
            //}

            //this.ObjectContext.SaveChanges();

            //SIPProviderBindingSynchroniser.SIPProviderAdded(sipProvider);

            m_service.InsertSIPProvider(this.ServiceContext.User.Identity.Name, sipProvider);
        }

        [RequiresAuthentication]
        public void UpdateSIPProvider(SIPProvider sipProvider)
        {
            //if (sipProvider.Owner != this.ServiceContext.User.Identity.Name)
            //{
            //    throw new ApplicationException("You are not authorised to update this record.");
            //}
            //else
            //{
            //    string validationError = SIPProvider.Validate(sipProvider);
            //    if (validationError != null)
            //    {
            //        throw new ApplicationException(validationError);
            //    }
            //    //else if (m_providerRegDisabled && sipProvider.RegisterEnabled)
            //    //{
            //    //    throw new ApplicationException("SIP provider registrations are disabled.");
            //    //}

            //    this.ObjectContext.SIPProviders.AttachAsModified(sipProvider, this.ChangeSet.GetOriginal(sipProvider));

            //    SIPProviderBindingSynchroniser.SIPProviderUpdated(sipProvider);
            //}

            m_service.UpdateSIPProvider(this.ServiceContext.User.Identity.Name, sipProvider);
        }

        [RequiresAuthentication]
        public void DeleteSIPProvider(SIPProvider sipProvider)
        {
            //if (sipProvider.Owner != this.ServiceContext.User.Identity.Name)
            //{
            //    throw new ApplicationException("You are not authorised to delete this record.");
            //}
            //else
            //{
            //    if ((sipProvider.EntityState == EntityState.Detached))
            //    {
            //        this.ObjectContext.SIPProviders.Attach(sipProvider);
            //    }
            //    this.ObjectContext.SIPProviders.DeleteObject(sipProvider);

            //    SIPProviderBindingSynchroniser.SIPProviderDeleted(sipProvider);
            //}


            m_service.DeleteSIPProvider(this.ServiceContext.User.Identity.Name, sipProvider);
        }

        [RequiresAuthentication]
        public IQueryable<SIPProviderBinding> GetSIPProviderBindings()
        {
            //return this.ObjectContext.SIPProviderBindings.Where(x => x.Owner == this.ServiceContext.User.Identity.Name);

            return m_service.GetSIPProviderBindings(this.ServiceContext.User.Identity.Name);
        }

        [RequiresAuthentication]
        public IQueryable<SIPRegistrarBinding> GetSIPRegistrarBindings()
        {
            //return this.ObjectContext.SIPRegistrarBindings.Where(x => x.Owner == this.ServiceContext.User.Identity.Name);
            return m_service.GetSIPRegistrarBindings(this.ServiceContext.User.Identity.Name);
        }

        [RequiresAuthentication]
        public IQueryable<SimpleWizardRule> GetSimpleWizardRules()
        {
            return m_service.GetSimpleWizardRules(this.ServiceContext.User.Identity.Name);
            //return this.ObjectContext.SimpleWizardDialPlanRules.Where(x => x.Owner == this.ServiceContext.User.Identity.Name);
        }

        [RequiresAuthentication]
        public void InsertSimpleWizardRule(SimpleWizardRule rule)
        {
            //rule.Owner = this.ServiceContext.User.Identity.Name;

            //if (rule.EntityState != EntityState.Detached)
            //{
            //    this.ObjectContext.ObjectStateManager.ChangeObjectState(rule, EntityState.Added);
            //}
            //else
            //{
            //    this.ObjectContext.SimpleWizardDialPlanRules.AddObject(rule);
            //}

            m_service.InsertSimpleWizardRule(this.ServiceContext.User.Identity.Name, rule);
        }

        [RequiresAuthentication]
        public void UpdateSimpleWizardRule(SimpleWizardRule rule)
        {
            //if (rule.Owner != this.ServiceContext.User.Identity.Name)
            //{
            //    throw new ApplicationException("You are not authorised to update this record.");
            //}
            //else
            //{
            //    this.ObjectContext.SimpleWizardDialPlanRules.AttachAsModified(rule, this.ChangeSet.GetOriginal(rule));
            //}

            m_service.UpdateSimpleWizardRule(this.ServiceContext.User.Identity.Name, rule);
        }

        [RequiresAuthentication]
        public void DeleteSimpleWizardRule(SimpleWizardRule rule)
        {
            //if (rule.Owner != this.ServiceContext.User.Identity.Name)
            //{
            //    throw new ApplicationException("You are not authorised to update this record.");
            //}
            //else
            //{
            //    this.ObjectContext.SimpleWizardDialPlanRules.AttachAsModified(rule, this.ChangeSet.GetOriginal(rule));
            //}

            m_service.DeleteSimpleWizardRule(this.ServiceContext.User.Identity.Name, rule);
        }
    }
}


