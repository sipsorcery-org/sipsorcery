using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Web.Services {

    [ServiceContract(Namespace = "http://www.sipsorcery.com/provisioning/rest")]
    public interface IProvisioningServiceREST {

        [OperationContract]
        [WebGet(UriTemplate = "isalive")]
        bool IsAlive();

        [OperationContract]
        [WebGet(UriTemplate = "customer/login?username={username}&password={password}", ResponseFormat=WebMessageFormat.Json)]
        string Login(string username, string password);

        [OperationContract]
        [WebGet(UriTemplate = "customer/logout")]
        void Logout();

        [OperationContract]
        [WebGet(UriTemplate = "sipdomains?where={whereExpression}&offset={offset}&count={count}")]
        List<SIPDomain> GetSIPDomains(string whereExpression, int offset, int count);

        [OperationContract]
        [WebGet(UriTemplate = "sipaccounts/count?where={whereexpression}")]
        int GetSIPAccountsCount(string whereExpression);

        [OperationContract]
        [WebGet(UriTemplate = "sipaccounts?where={whereexpression}&offset={offset}&count={count}", ResponseFormat = WebMessageFormat.Json)]
        List<SIPAccount> GetSIPAccounts(string whereExpression, int offset, int count);

        [OperationContract]
        [WebGet(UriTemplate = "sipaccount/add?username={username}&password={password}&domain={domain}&avatarurl={avatarurl}", ResponseFormat = WebMessageFormat.Json)]
        string AddSIPAccount(string username, string password, string domain, string avatarURL);

        [OperationContract]
        [WebGet(UriTemplate = "getsipregistrarbindingscount?whereexpression={whereexpression}")]
        int GetSIPRegistrarBindingsCount(string whereExpression);

        [OperationContract]
        [WebGet(UriTemplate = "getsipregistrarbindings?whereexpression={whereexpression}&offset={offset}&count={count}")]
        List<SIPRegistrarBinding> GetSIPRegistrarBindings(string whereExpression, int offset, int count);

        [OperationContract]
        [WebGet(UriTemplate = "getsipproviderscount?whereexpression={whereexpression}")]
        int GetSIPProvidersCount(string whereExpression);

        [OperationContract]
        [WebGet(UriTemplate = "getsipproviders?whereexpression={whereexpression}&offset={offset}&count={count}")]
        List<SIPProvider> GetSIPProviders(string whereExpression, int offset, int count);

        [OperationContract]
        [WebGet(UriTemplate = "getsipproviderbindingscount?whereexpression={whereexpression}")]
        int GetSIPProviderBindingsCount(string whereExpression);

        [OperationContract]
        [WebGet(UriTemplate = "getsipproviderbindings?whereexpression={whereexpression}&offset={offset}&count={count}")]
        List<SIPProviderBinding> GetSIPProviderBindings(string whereExpression, int offset, int count);

        [OperationContract]
        [WebGet(UriTemplate = "getdialplanscount?whereexpression={whereexpression}")]
        int GetDialPlansCount(string whereExpression);

        [OperationContract]
        [WebGet(UriTemplate = "getdialplans?whereexpression={whereexpression}&offset={offset}&count={count}")]
        List<SIPDialPlan> GetDialPlans(string whereExpression, int offset, int count);

        [OperationContract]
        [WebGet(UriTemplate = "getcallscount?whereexpression={whereexpression}")]
        int GetCallsCount(string whereExpression);

        [OperationContract]
        [WebGet(UriTemplate = "getcalls?whereexpression={whereexpression}&offset={offset}&count={count}")]
        List<SIPDialogueAsset> GetCalls(string whereExpression, int offset, int count);

        [OperationContract]
        [WebGet(UriTemplate = "getcdrscount?whereexpression={whereexpression}")]
        int GetCDRsCount(string whereExpression);

        [OperationContract]
        [WebGet(UriTemplate = "getcdrs?whereexpression={whereexpression}&offset={offset}&count={count}")]
        List<SIPCDRAsset> GetCDRs(string whereExpression, int offset, int count);
    }
}
