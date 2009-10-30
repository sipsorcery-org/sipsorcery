using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Text;
using SIPSorcery.CRM;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Web.Services {

    [ServiceContract(Namespace = "http://www.sipsorcery.com/provisioning")]
    public interface IProvisioningService {

        [OperationContract] bool IsAlive();
        [OperationContract] bool AreNewAccountsEnabled();
        [OperationContract] void CreateCustomer(Customer customer);
        [OperationContract] void DeleteCustomer(string customerUsername);
        [OperationContract] string Login(string username, string password);
        [OperationContract] void Logout();
        [OperationContract] Customer GetCustomer(string username);
        [OperationContract] void UpdateCustomer(Customer customer);
        [OperationContract] void UpdateCustomerPassword(string username, string oldPassword, string newPassword);
        [OperationContract] List<SIPDomain> GetSIPDomains(string filterExpression, int offset, int count);
        [OperationContract] int GetSIPAccountsCount(string whereExpression);
        [OperationContract] List<SIPAccount> GetSIPAccounts(string whereExpression, int offset, int count);
        [OperationContract] SIPAccount AddSIPAccount(SIPAccount sipAccount);
        [OperationContract] SIPAccount UpdateSIPAccount(SIPAccount sipAccount);
        [OperationContract] SIPAccount DeleteSIPAccount(SIPAccount sipAccount);
        [OperationContract] int GetSIPRegistrarBindingsCount(string whereExpression);
        [OperationContract] List<SIPRegistrarBinding> GetSIPRegistrarBindings(string whereExpression, int offset, int count);
        [OperationContract] int GetSIPProvidersCount(string whereExpression);
        [OperationContract] List<SIPProvider> GetSIPProviders(string whereExpression, int offset, int count);
        [OperationContract] SIPProvider AddSIPProvider(SIPProvider sipProvider);
        [OperationContract] SIPProvider UpdateSIPProvider(SIPProvider sipProvider);
        [OperationContract] SIPProvider DeleteSIPProvider(SIPProvider sipProvider);
        [OperationContract] int GetSIPProviderBindingsCount(string whereExpression);
        [OperationContract] List<SIPProviderBinding> GetSIPProviderBindings(string whereExpression, int offset, int count);
        [OperationContract] int GetDialPlansCount(string whereExpression);
        [OperationContract] List<SIPDialPlan> GetDialPlans(string whereExpression, int offset, int count);
        [OperationContract] SIPDialPlan AddDialPlan(SIPDialPlan dialPlan);
        [OperationContract] SIPDialPlan UpdateDialPlan(SIPDialPlan dialPlan);
        [OperationContract] SIPDialPlan DeleteDialPlan(SIPDialPlan dialPlan);
        [OperationContract] int GetCallsCount(string whereExpression);
        [OperationContract] List<SIPDialogueAsset> GetCalls(string whereExpression, int offset, int count);
        [OperationContract] int GetCDRsCount(string whereExpression);
        [OperationContract] List<SIPCDRAsset> GetCDRs(string whereExpression, int offset, int count);
        [OperationContract] void ExtendSession(int minutes);
        [OperationContract] int GetTimeZoneOffsetMinutes();
    }
}
