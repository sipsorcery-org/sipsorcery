using System;
using System.Collections.Generic;
using System.Linq;
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

        public string Login(string username, string password)
        {
            throw new NotImplementedException();
        }

        public void Logout()
        {
            throw new NotImplementedException();
        }

        public List<SIPDomain> GetSIPDomains(string where, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public int GetSIPAccountsCount(string where)
        {
            return 10;
        }

        public JSONResult<List<SIPAccountJSON>> GetSIPAccounts(string where, int offset, int count)
        {
            try
            {
                var customer = AuthoriseRequest();

                var result = from sipAccount in m_service.GetSIPAccounts(customer.Name)
                             select new SIPAccountJSON()
                             {
                                 ID = sipAccount.ID,
                                 SIPUsername = sipAccount.SIPUsername
                             };

                return new JSONResult<List<SIPAccountJSON>>() { Success = false, Result = result.ToList() };
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

        public int GetSIPAccountBindingsCount(string where)
        {
            throw new NotImplementedException();
        }

        public List<SIPRegistrarBinding> GetSIPAccountBindings(string where, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public int GetSIPProvidersCount(string where)
        {
            throw new NotImplementedException();
        }

        public List<SIPProvider> GetSIPProviders(string where, int offset, int count)
        {
            throw new NotImplementedException();
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