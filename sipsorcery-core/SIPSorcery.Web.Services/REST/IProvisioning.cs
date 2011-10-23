using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;
using SIPSorcery.Entities;

namespace SIPSorcery.Web.Services 
{
    public class JSONSIPAccount
    {
        public string ID;
        public string Username;
    }

    [ServiceContract(Namespace = "http://www.sipsorcery.com/provisioning/rest/v1.0")]
    public interface IProvisioning 
    {
        [OperationContract]
        [WebGet(UriTemplate = "isalive", ResponseFormat = WebMessageFormat.Json)]
        bool IsAlive();

        [OperationContract]
        [WebGet(UriTemplate = "customer/login?username={username}&password={password}", ResponseFormat=WebMessageFormat.Json)]
        string Login(string username, string password);

        [OperationContract]
        [WebGet(UriTemplate = "customer/logout", ResponseFormat = WebMessageFormat.Json)]
        void Logout();

        [OperationContract]
        [WebGet(UriTemplate = "sipdomain/get?where={where}&offset={offset}&count={count}", ResponseFormat = WebMessageFormat.Json)]
        List<SIPDomain> GetSIPDomains(string where, int offset, int count);

        [OperationContract]
        [WebGet(UriTemplate = "sipaccount/count?where={where}", ResponseFormat = WebMessageFormat.Json)]
        int GetSIPAccountsCount(string where);

        [OperationContract]
        [WebGet(UriTemplate = "sipaccount/get?where={where}&offset={offset}&count={count}", ResponseFormat = WebMessageFormat.Json)]
        JSONResult<List<SIPAccountJSON>> GetSIPAccounts(string where, int offset, int count);

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = "sipaccount/add", RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
        JSONResult<string> AddSIPAccount(SIPAccountJSON sipAccount);

        [OperationContract]
        [WebGet(UriTemplate = "sipaccountbinding/count?where={where}", ResponseFormat = WebMessageFormat.Json)]
        int GetSIPAccountBindingsCount(string where);

        [OperationContract]
        [WebGet(UriTemplate = "sipaccountbinding/get?where={where}&offset={offset}&count={count}", ResponseFormat = WebMessageFormat.Json)]
        List<SIPRegistrarBinding> GetSIPAccountBindings(string where, int offset, int count);

        [OperationContract]
        [WebGet(UriTemplate = "sipprovider/count?where={where}", ResponseFormat = WebMessageFormat.Json)]
        int GetSIPProvidersCount(string where);

        [OperationContract]
        [WebGet(UriTemplate = "sipprovider/get?where={where}&offset={offset}&count={count}", ResponseFormat = WebMessageFormat.Json)]
        List<SIPProvider> GetSIPProviders(string where, int offset, int count);

        [OperationContract]
        [WebGet(UriTemplate = "sipproviderbinding/count?where={where}", ResponseFormat = WebMessageFormat.Json)]
        int GetSIPProviderBindingsCount(string where);

        [OperationContract]
        [WebGet(UriTemplate = "sipproviderbinding/get?where={where}&offset={offset}&count={count}", ResponseFormat = WebMessageFormat.Json)]
        List<SIPProviderBinding> GetSIPProviderBindings(string where, int offset, int count);

        [OperationContract]
        [WebGet(UriTemplate = "dialplan/count?where={where}", ResponseFormat = WebMessageFormat.Json)]
        int GetDialPlansCount(string where);

        [OperationContract]
        [WebGet(UriTemplate = "dialplan/get?where={where}&offset={offset}&count={count}", ResponseFormat = WebMessageFormat.Json)]
        List<SIPDialPlan> GetDialPlans(string where, int offset, int count);

        [OperationContract]
        [WebGet(UriTemplate = "call/count?where={where}", ResponseFormat = WebMessageFormat.Json)]
        int GetCallsCount(string where);

        [OperationContract]
        [WebGet(UriTemplate = "call/get?where={where}&offset={offset}&count={count}", ResponseFormat = WebMessageFormat.Json)]
        List<SIPDialogue> GetCalls(string where, int offset, int count);

        [OperationContract]
        [WebGet(UriTemplate = "cdr/count?where={where}", ResponseFormat = WebMessageFormat.Json)]
        int GetCDRsCount(string where);

        [OperationContract]
        [WebGet(UriTemplate = "cdr/get?where={where}&offset={offset}&count={count}", ResponseFormat = WebMessageFormat.Json)]
        List<CDR> GetCDRs(string where, int offset, int count);
    }
}
