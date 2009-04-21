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
using SIPSorcery.SIP.App;
using SIPSorcery.Silverlight.Messaging;
using SIPSorcery.SIPSorceryProvisioningClient;

namespace SIPSorcery.Persistence
{
    public class SIPSorceryWebServicePersistor : SIPSorceryPersistor
    {
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

        private SIPProvisioningWebServiceClient m_provisioningServiceProxy;

        public SIPSorceryWebServicePersistor(string serverURL, string authid)
        {
            //BasicHttpBinding binding = new BasicHttpBinding();
            BasicHttpCustomHeaderBinding binding = new BasicHttpCustomHeaderBinding(new SecurityHeader(authid));
            EndpointAddress address = new EndpointAddress(serverURL);
            m_provisioningServiceProxy = new SIPProvisioningWebServiceClient(binding, address);

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
        }

        public override void IsAliveAsync()
        {
            m_provisioningServiceProxy.IsAliveAsync();
        }

        private void IsAliveCompleted(object sender, IsAliveCompletedEventArgs e) {
            IsAliveComplete(e);
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
            GetSIPDomainsComplete(e);
        }

        #region SIP Accounts.

        public override void GetSIPAccountsCountAsync(string whereExpression) {
            m_provisioningServiceProxy.GetSIPAccountsCountAsync(whereExpression);
        }

        private void GetSIPAccountsCountCompleted(object sender, GetSIPAccountsCountCompletedEventArgs e) {
            GetSIPAccountsCountComplete(e);
        }

        public override void GetSIPAccountsAsync(string whereExpression, int offset, int count) {
            m_provisioningServiceProxy.GetSIPAccountsAsync(whereExpression, offset, count);
        }

        private void GetSIPAccountsCompleted(object sender, GetSIPAccountsCompletedEventArgs e) {
            GetSIPAccountsComplete(e);
        }

        public override void AddSIPAccountAsync(SIPAccount sipAccount) {
            m_provisioningServiceProxy.AddSIPAccountAsync(sipAccount);
        }

        private void AddSIPAccountCompleted(object sender, AddSIPAccountCompletedEventArgs e) {
            AddSIPAccountComplete(e);
        }

        public override void UpdateSIPAccount(SIPAccount sipAccount) {
            m_provisioningServiceProxy.UpdateSIPAccountAsync(sipAccount);
        }

        private void UpdateSIPAccountCompleted(object sender, UpdateSIPAccountCompletedEventArgs e) {
            UpdateSIPAccountComplete(e);
        }

        public override void DeleteSIPAccount(SIPAccount sipAccount) {
            m_provisioningServiceProxy.DeleteSIPAccountAsync(sipAccount);
        }

        private void DeleteSIPAccountCompleted(object sender, DeleteSIPAccountCompletedEventArgs e) {
            DeleteSIPAccountComplete(e);
        }

        #endregion

        #region SIP Providers.

        public override void GetSIPProvidersCountAsync(string where) {
            m_provisioningServiceProxy.GetSIPProvidersCountAsync(where);
        }

        private void GetSIPProvidersCountCompleted(object sender, GetSIPProvidersCountCompletedEventArgs e) {
            GetSIPProvidersCountComplete(e);
        }

        public override void GetSIPProvidersAsync(string where, int offset, int count) {
            m_provisioningServiceProxy.GetSIPProvidersAsync(where, offset, count);
        }

        private void GetSIPProvidersCompleted(object sender, GetSIPProvidersCompletedEventArgs e) {
            GetSIPProvidersComplete(e);
        }

        public override void DeleteSIPProviderAsync(SIPProvider sipProvider) {
            m_provisioningServiceProxy.DeleteSIPProviderAsync(sipProvider);
        }

        private void DeleteSIPProviderCompleted(object sender, DeleteSIPProviderCompletedEventArgs e) {
            DeleteSIPProviderComplete(e);
        }

        public override void UpdateSIPProviderAsync(SIPProvider sipProvider) {
            m_provisioningServiceProxy.UpdateSIPProviderAsync(sipProvider);
        }

        private void UpdateSIPProviderCompleted(object sender, UpdateSIPProviderCompletedEventArgs e) {
            UpdateSIPProviderComplete(e);
        }

        public override void AddSIPProviderAsync(SIPProvider sipProvider) {
            m_provisioningServiceProxy.AddSIPProviderAsync(sipProvider);
        }

        private void AddSIPProviderCompleted(object sender, AddSIPProviderCompletedEventArgs e) {
            AddSIPProviderComplete(e);
        }

        public override void GetSIPProviderBindingsAsync(string where, int offset, int count) {
            m_provisioningServiceProxy.GetSIPProviderBindingsAsync(where, offset, count);
        }

        private void GetSIPProviderBindingsCompleted(object sender, GetSIPProviderBindingsCompletedEventArgs e) {
            GetSIPProviderBindingsComplete(e);
        }

        public override void GetSIPProviderBindingsCountAsync(string whereExpression) {
            m_provisioningServiceProxy.GetSIPProviderBindingsCountAsync(whereExpression);
        }

        private void GetSIPProviderBindingsCountCompleted(object sender, GetSIPProviderBindingsCountCompletedEventArgs e) {
            GetSIPProviderBindingsCountComplete(e);
        }

        #endregion

        #region Dial Plans.

        private void GetDialPlansCountCompleted(object sender, GetDialPlansCountCompletedEventArgs e)
        {
            if (GetDialPlansCountComplete != null)
            {
                GetDialPlansCountComplete(e);
            }
        }

        public override void GetDialPlansCountAsync(string where)
        {
            m_provisioningServiceProxy.GetDialPlansCountAsync(where);
        }

        private void GetDialPlansCompleted(object sender, GetDialPlansCompletedEventArgs e)
        {
            if (GetDialPlansComplete != null)
            {
                GetDialPlansComplete(e);
            }
        }
        
        public override void GetDialPlansAsync(string where, int offset, int count)
        {
            m_provisioningServiceProxy.GetDialPlansAsync(where, offset, count);
        }

        public override void DeleteDialPlanAsync(SIPDialPlan dialPlan)
        {
            m_provisioningServiceProxy.DeleteDialPlanAsync(dialPlan);
        }

        private void DeleteDialPlanCompleted(object sender, DeleteDialPlanCompletedEventArgs e)
        {
            if (DeleteDialPlanComplete != null)
            {
                DeleteDialPlanComplete(e);
            }
        }

        public override void AddDialPlanAsync(SIPDialPlan dialPlan)
        {
            m_provisioningServiceProxy.AddDialPlanAsync(dialPlan);
        }

        private void AddDialPlanCompleted(object sender, AddDialPlanCompletedEventArgs e)
        {
            if (AddDialPlanComplete != null)
            {
                AddDialPlanComplete(e);
            }
        }

        public override void UpdateDialPlanAsync(SIPDialPlan dialPlan)
        {
            m_provisioningServiceProxy.UpdateDialPlanAsync(dialPlan);
        }

        private void UpdateDialPlanCompleted(object sender, UpdateDialPlanCompletedEventArgs e)
        {
            if (UpdateDialPlanComplete != null)
            {
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
            GetRegistrarBindingsComplete(e);
        }

        public override void GetRegistrarBindingsCountAsync(string whereExpression) {
            m_provisioningServiceProxy.GetSIPRegistrarBindingsCountAsync(whereExpression);
        }

        private void GetSIPRegistrarBindingsCountCompleted(object sender, GetSIPRegistrarBindingsCountCompletedEventArgs e) {
            GetRegistrarBindingsCountComplete(e);
        }

        public override void GetCallsCountAsync(string whereExpression) {
            m_provisioningServiceProxy.GetCallsCountAsync(whereExpression);
        }

        private void m_provisioningServiceProxy_GetCallsCountCompleted(object sender, GetCallsCountCompletedEventArgs e) {
            GetCallsCountComplete(e);
        }

        public override void GetCallsAsync(string whereExpressionn, int offset, int count) {
            m_provisioningServiceProxy.GetCallsAsync(whereExpressionn, offset, count);
        }

        private void m_provisioningServiceProxy_GetCallsCompleted(object sender, GetCallsCompletedEventArgs e) {
            GetCallsComplete(e);
        }

        public override void GetCDRsCountAsync(string whereExpression) {
            m_provisioningServiceProxy.GetCDRsCountAsync(whereExpression);
        }

        private void GetCDRsCountCompleted(object sender, GetCDRsCountCompletedEventArgs e) {
            GetCDRsCountComplete(e);
        }

        public override void GetCDRsAsync(string whereExpression, int offset, int count) {
            m_provisioningServiceProxy.GetCDRsAsync(whereExpression, offset, count);
        }

        private void GetCDRsCompleted(object sender, GetCDRsCompletedEventArgs e) {
            GetCDRsComplete(e);
        }
    }
}
