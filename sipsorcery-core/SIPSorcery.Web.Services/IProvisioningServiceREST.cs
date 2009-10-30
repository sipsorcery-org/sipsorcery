using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;
using SIPSorcery.CRM;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Web.Services {

    [ServiceContract(Namespace = "http://www.sipsorcery.com/provisioning/rest")]
    public interface IProvisioningServiceREST {

        [OperationContract]
        [WebGet(UriTemplate = "isalive")]
        bool IsAlive();

        [OperationContract]
        [WebGet(UriTemplate = "login?username={username}&password={password}")]
        string Login(string username, string password);

        [OperationContract]
        [WebGet(UriTemplate = "logout")]
        void Logout();

        [OperationContract]
        [WebGet(UriTemplate = "sipdomains/{whereExpression}/{offset}/ {count}")]
        List<SIPDomain> GetSIPDomains(string whereExpression, int offset, int count);

        [OperationContract]
        [WebGet(UriTemplate = "getsipaccountscount?whereexpression={whereexpression}")]
        int GetSIPAccountsCount(string whereExpression);

        [OperationContract]
        [WebGet(UriTemplate = "getsipaccounts?whereexpression={whereexpression}&offset={offset}&count={count}")]
        List<SIPAccount> GetSIPAccounts(string whereExpression, int offset, int count);

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
