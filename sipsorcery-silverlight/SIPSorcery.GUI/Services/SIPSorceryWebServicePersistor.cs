using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.ServiceModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SIPSorcery.CRM;
using SIPSorcery.SIP.App;
using SIPSorcery.Silverlight.Messaging;
using SIPSorcery.SIPSorceryProvisioningClient;

namespace SIPSorcery.Persistence {
    public class SIPSorceryWebServicePersistor : SIPSorceryPersistor {
        public override event IsAliveCompleteDelegate IsAliveComplete;
        public override event LoginCompleteDelegate LoginComplete;
        public override event LogoutCompleteDelegate LogoutComplete;
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

        public override event MethodInvokerDelegate SessionExpired = () => { };

        private ProvisioningServiceClient m_provisioningServiceProxy;

        public SIPSorceryWebServicePersistor(string serverURL, string authid) {
            //BasicHttpBinding binding = new BasicHttpBinding();
            BasicHttpSecurityMode securitymode = (serverURL.StartsWith("https")) ? BasicHttpSecurityMode.Transport : BasicHttpSecurityMode.None;
            BasicHttpCustomHeaderBinding binding = new BasicHttpCustomHeaderBinding(new SecurityHeader(authid), securitymode);
            
            EndpointAddress address = new EndpointAddress(serverURL);
            m_provisioningServiceProxy = new ProvisioningServiceClient(binding, address);

            // Provisioning web service delegates.
            m_provisioningServiceProxy.IsAliveCompleted += IsAliveCompleted;
            m_provisioningServiceProxy.LoginCompleted += LoginCompleted;
            m_provisioningServiceProxy.LogoutCompleted += LogoutCompleted;
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
        }

        private bool IsUnauthorised(Exception excp) {
            if (excp != null && excp.GetType() == typeof(RawFaultException)) {
                RawFaultException rawExcp = (RawFaultException)excp;
                if (rawExcp.FaultType == typeof(UnauthorizedAccessException)) {
                    return true;
                }
            }
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

        private void GetSIPDomainsCompleted(object sender, GetSIPDomainsCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else {
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
            else {
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
            else {
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
            else {
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
            else {
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
            else {
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
            else {
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
            else {
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
            else {
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
            else {
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
            else {
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
            else {
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
            else {
                GetSIPProviderBindingsCountComplete(e);
            }
        }

        #endregion

        #region Dial Plans.

        private void GetDialPlansCountCompleted(object sender, GetDialPlansCountCompletedEventArgs e) {
            if (IsUnauthorised(e.Error)) {
                SessionExpired();
            }
            else {
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
            else {
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
            else {
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
            else {
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
            else {
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
            else {
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
            else {
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
            else {
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
            else {
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
            else {
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
            else {
                GetCDRsComplete(e);
            }
        }

        public override void CreateCustomerAsync(Customer customer) {
            m_provisioningServiceProxy.CreateCustomerAsync(customer);
        }

        private void CreateCustomerCompleted(object sender, AsyncCompletedEventArgs e) {
            CreateCustomerComplete(e);
        }

        public override void DeleteCustomerAsync(string customerUsername) {
            m_provisioningServiceProxy.DeleteCustomerAsync(customerUsername);
        }

        private void DeleteCustomerCompleted(object sender, AsyncCompletedEventArgs e) {
            DeleteCustomerComplete(e);
        }
    }
}
