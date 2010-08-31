using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Net.Browser;
using System.ServiceModel;
using System.ServiceModel.Channels;
using SIPSorcery.CRM;
using SIPSorcery.SIP.App;
using SIPSorcery.Silverlight.Messaging;
using SIPSorcery.SIPSorceryProvisioningClient;
using SIPSorcery.Web.Services;

namespace SIPSorcery.Persistence {

    public class SIPSorceryWebServicePersistor : SIPSorceryPersistor {

        private static int MAX_WCF_MESSAGE_SIZE = 1000000;   // Limit messages to 1MB.

        public override event IsAliveCompleteDelegate IsAliveComplete;
        public override event TestExceptionCompleteDelegate TestExceptionComplete;
        public override event AreNewAccountsEnabledCompleteDelegate AreNewAccountsEnabledComplete;
        public override event CheckInviteCodeCompleteDelegate CheckInviteCodeComplete;
        public override event LoginCompleteDelegate LoginComplete;
        public override event LogoutCompleteDelegate LogoutComplete;
        public override event GetCustomerCompleteDelegate GetCustomerComplete;
        public override event UpdateCustomerCompleteDelegate UpdateCustomerComplete;
        public override event UpdateCustomerPasswordCompleteDelegate UpdateCustomerPasswordComplete;
        public override event GetSIPAccountsCompleteDelegate GetSIPAccountsComplete;
        public override event GetSIPAccountsCountCompleteDelegate GetSIPAccountsCountComplete;
        public override event AddSIPAccountCompleteDelegate AddSIPAccountComplete;
        public override event UpdateSIPAccountCompleteDelegate UpdateSIPAccountComplete;
        public override event DeleteSIPAccountCompleteDelegate DeleteSIPAccountComplete;
        public override event GetDialPlansCountCompleteDelegate GetDialPlansCountComplete;
        public override event GetDialPlansCompleteDelegate GetDialPlansComplete;
        public override event DeleteDialPlanCompleteDelegate DeleteDialPlanComplete;
        public override event AddDialPlanCompleteDelegate AddDialPlanComplete;
        public override event UpdateDialPlanCompleteDelegate UpdateDialPlanComplete;
        public override event GetSIPProvidersCountCompleteDelegate GetSIPProvidersCountComplete;
        public override event GetSIPProvidersCompleteDelegate GetSIPProvidersComplete;
        public override event DeleteSIPProviderCompleteDelegate DeleteSIPProviderComplete;
        public override event AddSIPProviderCompleteDelegate AddSIPProviderComplete;
        public override event UpdateSIPProviderCompleteDelegate UpdateSIPProviderComplete;
        public override event GetSIPDomainsCompleteDelegate GetSIPDomainsComplete;
        public override event GetRegistrarBindingsCompleteDelegate GetRegistrarBindingsComplete;
        public override event GetRegistrarBindingsCountCompleteDelegate GetRegistrarBindingsCountComplete;
        public override event GetSIPProviderBindingsCompleteDelegate GetSIPProviderBindingsComplete;
        public override event GetSIPProviderBindingsCountCompleteDelegate GetSIPProviderBindingsCountComplete;
        public override event GetCallsCountCompleteDelegate GetCallsCountComplete;
        public override event GetCallsCompleteDelegate GetCallsComplete;
        public override event GetCDRsCountCompleteDelegate GetCDRsCountComplete;
        public override event GetCDRsCompleteDelegate GetCDRsComplete;
        public override event CreateCustomerCompleteDelegate CreateCustomerComplete;
        public override event DeleteCustomerCompleteDelegate DeleteCustomerComplete;
        public override event GetTimeZoneOffsetMinutesCompleteDelegate GetTimeZoneOffsetMinutesComplete;
        public override event ExtendSessionCompleteDelegate ExtendSessionComplete;

        public override event MethodInvokerDelegate SessionExpired = () => { };

        private ProvisioningServiceClient m_provisioningServiceProxy;

        public SIPSorceryWebServicePersistor(string serverURL, string authid) {

            BasicHttpSecurityMode securitymode = (serverURL.StartsWith("https")) ? BasicHttpSecurityMode.Transport : BasicHttpSecurityMode.None;
            SIPSorcerySecurityHeader securityHeader = new SIPSorcerySecurityHeader(authid);
            SIPSorceryCustomHeader sipSorceryHeader = new SIPSorceryCustomHeader(new List<MessageHeader>(){securityHeader});
            BasicHttpCustomHeaderBinding binding = new BasicHttpCustomHeaderBinding(sipSorceryHeader, securitymode);
            binding.MaxReceivedMessageSize = MAX_WCF_MESSAGE_SIZE;
            
            EndpointAddress address = new EndpointAddress(serverURL);
            m_provisioningServiceProxy = new ProvisioningServiceClient(binding, address);

            // Provisioning web service delegates.
            m_provisioningServiceProxy.IsAliveCompleted += IsAliveCompleted;
            m_provisioningServiceProxy.TestExceptionCompleted += TestExceptionCompleted;
            m_provisioningServiceProxy.AreNewAccountsEnabledCompleted += AreNewAccountsEnabledCompleted;
            m_provisioningServiceProxy.CheckInviteCodeCompleted += CheckInviteCodeCompleted;
            m_provisioningServiceProxy.LoginCompleted += LoginCompleted;
            m_provisioningServiceProxy.LogoutCompleted += LogoutCompleted;
            m_provisioningServiceProxy.GetCustomerCompleted += GetCustomerCompleted;
            m_provisioningServiceProxy.UpdateCustomerCompleted += UpdateCustomerCompleted;
            m_provisioningServiceProxy.UpdateCustomerPasswordCompleted += UpdateCustomerPasswordCompleted;
            m_provisioningServiceProxy.GetSIPAccountsCompleted += GetSIPAccountsCompleted;
            m_provisioningServiceProxy.GetSIPAccountsCountCompleted += GetSIPAccountsCountCompleted;
            m_provisioningServiceProxy.AddSIPAccountCompleted += AddSIPAccountCompleted;
            m_provisioningServiceProxy.UpdateSIPAccountCompleted += UpdateSIPAccountCompleted;
            m_provisioningServiceProxy.DeleteSIPAccountCompleted += DeleteSIPAccountCompleted;
            m_provisioningServiceProxy.GetDialPlansCountCompleted += GetDialPlansCountCompleted;
            m_provisioningServiceProxy.GetDialPlansCompleted += GetDialPlansCompleted;
            m_provisioningServiceProxy.UpdateDialPlanCompleted += UpdateDialPlanCompleted;
            m_provisioningServiceProxy.AddDialPlanCompleted += AddDialPlanCompleted;
            m_provisioningServiceProxy.DeleteDialPlanCompleted += DeleteDialPlanCompleted;
            m_provisioningServiceProxy.GetSIPProvidersCountCompleted += GetSIPProvidersCountCompleted;
            m_provisioningServiceProxy.GetSIPProvidersCompleted += GetSIPProvidersCompleted;
            m_provisioningServiceProxy.AddSIPProviderCompleted += AddSIPProviderCompleted;
            m_provisioningServiceProxy.UpdateSIPProviderCompleted += UpdateSIPProviderCompleted;
            m_provisioningServiceProxy.DeleteSIPProviderCompleted += DeleteSIPProviderCompleted;
            m_provisioningServiceProxy.GetSIPDomainsCompleted += GetSIPDomainsCompleted;
            m_provisioningServiceProxy.GetSIPRegistrarBindingsCompleted += GetSIPRegistrarBindingsCompleted;
            m_provisioningServiceProxy.GetSIPRegistrarBindingsCountCompleted += GetSIPRegistrarBindingsCountCompleted;
            m_provisioningServiceProxy.GetSIPProviderBindingsCompleted += GetSIPProviderBindingsCompleted;
            m_provisioningServiceProxy.GetSIPProviderBindingsCountCompleted += GetSIPProviderBindingsCountCompleted;
            m_provisioningServiceProxy.GetCallsCountCompleted += m_provisioningServiceProxy_GetCallsCountCompleted;
            m_provisioningServiceProxy.GetCallsCompleted += m_provisioningServiceProxy_GetCallsCompleted;
            m_provisioningServiceProxy.GetCDRsCountCompleted += GetCDRsCountCompleted;
            m_provisioningServiceProxy.GetCDRsCompleted += GetCDRsCompleted;
            m_provisioningServiceProxy.CreateCustomerCompleted += CreateCustomerCompleted;
            m_provisioningServiceProxy.DeleteCustomerCompleted += DeleteCustomerCompleted;
            m_provisioningServiceProxy.GetTimeZoneOffsetMinutesCompleted += GetTimeZoneOffsetMinutesCompleted;
            m_provisioningServiceProxy.ExtendSessionCompleted += ExtendSessionCompleted;
        }

        private bool IsUnauthorised(Exception excp) {
            /*if (excp != null && excp.GetType() == typeof(RawFaultException)) {
                RawFaultException rawExcp = (RawFaultException)excp;
                if (rawExcp.FaultType == typeof(UnauthorizedAccessException)) {
                    return true;
                }
            }*/
            return false;
        }

        public override void IsAliveAsync() {
            m_provisioningServiceProxy.IsAliveAsync();
        }

        private void IsAliveCompleted(object sender, IsAliveCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else {
                IsAliveComplete(e);
            }
        }

        public override void TestExceptionAsync() {
            m_provisioningServiceProxy.TestExceptionAsync();
        }

        private void TestExceptionCompleted(object sender, AsyncCompletedEventArgs e) {
            if (TestExceptionComplete != null) {
                TestExceptionComplete(e);
            }
        }

        public override void AreNewAccountsEnabledAsync() {
            m_provisioningServiceProxy.AreNewAccountsEnabledAsync();
        }

        private void AreNewAccountsEnabledCompleted(object sender, AreNewAccountsEnabledCompletedEventArgs e) {
            AreNewAccountsEnabledComplete(e);
        }

        public override void CheckInviteCodeAsync(string inviteCode)
        {
            m_provisioningServiceProxy.CheckInviteCodeAsync(inviteCode);
        }


        private void CheckInviteCodeCompleted(object sender, CheckInviteCodeCompletedEventArgs e)
        {
            CheckInviteCodeComplete(e);
        }

        public override void LoginAsync(string username, string password) {
            m_provisioningServiceProxy.LoginAsync(username, password);
        }

        private void LoginCompleted(object sender, LoginCompletedEventArgs e) {
            LoginComplete(e);
        }

        public override void LogoutAsync() {
            m_provisioningServiceProxy.LogoutAsync();
        }

        private void LogoutCompleted(object sender, AsyncCompletedEventArgs e) {
            LogoutComplete(e);
        }

        public override void ExtendSessionAsync(int minutes) {
            m_provisioningServiceProxy.ExtendSessionAsync(minutes);
        }

        private void ExtendSessionCompleted(object sender, AsyncCompletedEventArgs e) {
            ExtendSessionComplete(e);
        }

        public override void GetTimeZoneOffsetMinutesAsync() {
            m_provisioningServiceProxy.GetTimeZoneOffsetMinutesAsync();
        }

        private void GetTimeZoneOffsetMinutesCompleted(object sender, GetTimeZoneOffsetMinutesCompletedEventArgs e) {
            GetTimeZoneOffsetMinutesComplete(e);
        }

        public override void GetCustomerAsync(string username) {
            m_provisioningServiceProxy.GetCustomerAsync(username);
        }

        private void GetCustomerCompleted(object sender, GetCustomerCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if(GetCustomerComplete != null){
                GetCustomerComplete(e);
            }
        }

        public override void UpdateCustomerAsync(Customer customer) {
            m_provisioningServiceProxy.UpdateCustomerAsync(customer);
        }

        private void UpdateCustomerCompleted(object sender, AsyncCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (UpdateCustomerComplete != null) {
                UpdateCustomerComplete(e);
            }
        }

        public override void UpdateCustomerPassword(string username, string oldPassword, string newPassword) {
            m_provisioningServiceProxy.UpdateCustomerPasswordAsync(username, oldPassword, newPassword);
        }

        private void UpdateCustomerPasswordCompleted(object sender, AsyncCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if(UpdateCustomerPasswordComplete != null){
                UpdateCustomerPasswordComplete(e);
            }
        }

        private void GetSIPDomainsCompleted(object sender, GetSIPDomainsCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if(GetSIPDomainsComplete != null){
                GetSIPDomainsComplete(e);
            }
        }

        #region SIP Accounts.

        public override void GetSIPAccountsCountAsync(string whereExpression) {
            m_provisioningServiceProxy.GetSIPAccountsCountAsync(whereExpression);
        }

        private void GetSIPAccountsCountCompleted(object sender, GetSIPAccountsCountCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (GetSIPAccountsCountComplete != null) {
                GetSIPAccountsCountComplete(e);
            }
        }

        public override void GetSIPAccountsAsync(string whereExpression, int offset, int count) {
            m_provisioningServiceProxy.GetSIPAccountsAsync(whereExpression, offset, count);
        }

        private void GetSIPAccountsCompleted(object sender, GetSIPAccountsCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (GetSIPAccountsComplete != null) {
                GetSIPAccountsComplete(e);
            }
        }

        public override void AddSIPAccountAsync(SIPAccount sipAccount) {
            m_provisioningServiceProxy.AddSIPAccountAsync(sipAccount);
        }

        private void AddSIPAccountCompleted(object sender, AddSIPAccountCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (AddSIPAccountComplete != null) {
                AddSIPAccountComplete(e);
            }
        }

        public override void UpdateSIPAccount(SIPAccount sipAccount) {
            m_provisioningServiceProxy.UpdateSIPAccountAsync(sipAccount);
        }

        private void UpdateSIPAccountCompleted(object sender, UpdateSIPAccountCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (UpdateSIPAccountComplete != null) {
                UpdateSIPAccountComplete(e);
            }
        }

        public override void DeleteSIPAccount(SIPAccount sipAccount) {
            m_provisioningServiceProxy.DeleteSIPAccountAsync(sipAccount);
        }

        private void DeleteSIPAccountCompleted(object sender, DeleteSIPAccountCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (DeleteSIPAccountComplete != null) {
                DeleteSIPAccountComplete(e);
            }
        }

        #endregion

        #region SIP Providers.

        public override void GetSIPProvidersCountAsync(string where) {
            m_provisioningServiceProxy.GetSIPProvidersCountAsync(where);
        }

        private void GetSIPProvidersCountCompleted(object sender, GetSIPProvidersCountCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (GetSIPProvidersCountComplete != null) {
                GetSIPProvidersCountComplete(e);
            }
        }

        public override void GetSIPProvidersAsync(string where, int offset, int count) {
            m_provisioningServiceProxy.GetSIPProvidersAsync(where, offset, count);
        }

        private void GetSIPProvidersCompleted(object sender, GetSIPProvidersCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (GetSIPProvidersComplete != null) {
                GetSIPProvidersComplete(e);
            }
        }

        public override void DeleteSIPProviderAsync(SIPProvider sipProvider) {
            m_provisioningServiceProxy.DeleteSIPProviderAsync(sipProvider);
        }

        private void DeleteSIPProviderCompleted(object sender, DeleteSIPProviderCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (DeleteSIPProviderComplete != null) {
                DeleteSIPProviderComplete(e);
            }
        }

        public override void UpdateSIPProviderAsync(SIPProvider sipProvider) {
            m_provisioningServiceProxy.UpdateSIPProviderAsync(sipProvider);
        }

        private void UpdateSIPProviderCompleted(object sender, UpdateSIPProviderCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (UpdateSIPProviderComplete != null) {
                UpdateSIPProviderComplete(e);
            }
        }

        public override void AddSIPProviderAsync(SIPProvider sipProvider) {
            m_provisioningServiceProxy.AddSIPProviderAsync(sipProvider);
        }

        private void AddSIPProviderCompleted(object sender, AddSIPProviderCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (AddSIPProviderComplete != null) {
                AddSIPProviderComplete(e);
            }
        }

        public override void GetSIPProviderBindingsAsync(string where, int offset, int count) {
            m_provisioningServiceProxy.GetSIPProviderBindingsAsync(where, offset, count);
        }

        private void GetSIPProviderBindingsCompleted(object sender, GetSIPProviderBindingsCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (GetSIPProviderBindingsComplete != null) {
                GetSIPProviderBindingsComplete(e);
            }
        }

        public override void GetSIPProviderBindingsCountAsync(string whereExpression) {
            m_provisioningServiceProxy.GetSIPProviderBindingsCountAsync(whereExpression);
        }

        private void GetSIPProviderBindingsCountCompleted(object sender, GetSIPProviderBindingsCountCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (GetSIPProviderBindingsCountComplete != null) {
                GetSIPProviderBindingsCountComplete(e);
            }
        }

        #endregion

        #region Dial Plans.

        private void GetDialPlansCountCompleted(object sender, GetDialPlansCountCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (GetDialPlansCountComplete != null) {
                GetDialPlansCountComplete(e);
            }
        }

        public override void GetDialPlansCountAsync(string where) {
            m_provisioningServiceProxy.GetDialPlansCountAsync(where);
        }

        private void GetDialPlansCompleted(object sender, GetDialPlansCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (GetDialPlansComplete != null) {
                GetDialPlansComplete(e);
            }
        }

        public override void GetDialPlansAsync(string where, int offset, int count) {
            m_provisioningServiceProxy.GetDialPlansAsync(where, offset, count);
        }

        public override void DeleteDialPlanAsync(SIPDialPlan dialPlan) {
            m_provisioningServiceProxy.DeleteDialPlanAsync(dialPlan);
        }

        private void DeleteDialPlanCompleted(object sender, DeleteDialPlanCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (DeleteDialPlanComplete != null) {
                DeleteDialPlanComplete(e);
            }
        }

        public override void AddDialPlanAsync(SIPDialPlan dialPlan) {
            m_provisioningServiceProxy.AddDialPlanAsync(dialPlan);
        }

        private void AddDialPlanCompleted(object sender, AddDialPlanCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (AddDialPlanComplete != null) {
                AddDialPlanComplete(e);
            }
        }

        public override void UpdateDialPlanAsync(SIPDialPlan dialPlan) {
            m_provisioningServiceProxy.UpdateDialPlanAsync(dialPlan);
        }

        private void UpdateDialPlanCompleted(object sender, UpdateDialPlanCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (UpdateDialPlanComplete != null) {
                UpdateDialPlanComplete(e);
            };
        }

        #endregion

        public override void GetSIPDomainsAsync(string where, int offset, int count) {
            m_provisioningServiceProxy.GetSIPDomainsAsync(where, offset, count);
        }

        public override void GetRegistrarBindingsAsync(string where, int offset, int count) {
            m_provisioningServiceProxy.GetSIPRegistrarBindingsAsync(where, offset, count);
        }

        private void GetSIPRegistrarBindingsCompleted(object sender, GetSIPRegistrarBindingsCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (GetRegistrarBindingsComplete != null) {
                GetRegistrarBindingsComplete(e);
            }
        }

        public override void GetRegistrarBindingsCountAsync(string whereExpression) {
            m_provisioningServiceProxy.GetSIPRegistrarBindingsCountAsync(whereExpression);
        }

        private void GetSIPRegistrarBindingsCountCompleted(object sender, GetSIPRegistrarBindingsCountCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (GetRegistrarBindingsCountComplete != null) {
                GetRegistrarBindingsCountComplete(e);
            }
        }

        public override void GetCallsCountAsync(string whereExpression) {
            m_provisioningServiceProxy.GetCallsCountAsync(whereExpression);
        }

        private void m_provisioningServiceProxy_GetCallsCountCompleted(object sender, GetCallsCountCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (GetCallsCountComplete != null) {
                GetCallsCountComplete(e);
            }
        }

        public override void GetCallsAsync(string whereExpressionn, int offset, int count) {
            m_provisioningServiceProxy.GetCallsAsync(whereExpressionn, offset, count);
        }

        private void m_provisioningServiceProxy_GetCallsCompleted(object sender, GetCallsCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (GetCallsComplete != null) {
                GetCallsComplete(e);
            }
        }

        public override void GetCDRsCountAsync(string whereExpression) {
            m_provisioningServiceProxy.GetCDRsCountAsync(whereExpression);
        }

        private void GetCDRsCountCompleted(object sender, GetCDRsCountCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (GetCDRsCountComplete != null) {
                GetCDRsCountComplete(e);
            }
        }

        public override void GetCDRsAsync(string whereExpression, int offset, int count) {
            m_provisioningServiceProxy.GetCDRsAsync(whereExpression, offset, count);
        }

        private void GetCDRsCompleted(object sender, GetCDRsCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else if (GetCDRsComplete != null) {
                GetCDRsComplete(e);
            }
        }

        public override void CreateCustomerAsync(Customer customer) {
            m_provisioningServiceProxy.CreateCustomerAsync(customer);
        }

        private void CreateCustomerCompleted(object sender, AsyncCompletedEventArgs e) {
            if (CreateCustomerComplete != null) {
                CreateCustomerComplete(e);
            }
        }

        public override void DeleteCustomerAsync(string customerUsername) {
            m_provisioningServiceProxy.DeleteCustomerAsync(customerUsername);
        }

        private void DeleteCustomerCompleted(object sender, AsyncCompletedEventArgs e) {
            if (DeleteCustomerComplete != null) {
                DeleteCustomerComplete(e);
            }
        }
    }
}
