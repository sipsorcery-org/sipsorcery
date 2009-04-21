using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorcery.SIPSorceryProvisioningClient;

namespace SIPSorcery.Persistence
{
    public class SIPSorceryGUITestPersistor : SIPSorceryPersistor
    {
        public static string DUMMY_OWNER = "dummy";

        private static string m_dummyAuthId = "dummy";
        private static string m_dummyDomain = "dummy.com";

        private static ObservableCollection<SIPDomain> m_sipDomains = new ObservableCollection<SIPDomain>()
        {
            new SIPDomain(m_dummyDomain, DUMMY_OWNER, new List<string>())
        };

        private static ObservableCollection<SIPAccount> m_sipAccounts = new ObservableCollection<SIPAccount>()
        {
            new SIPAccount(DUMMY_OWNER , m_dummyDomain, "bunyip", "password", "default"),
            new SIPAccount(DUMMY_OWNER , m_dummyDomain, "muppet", "password", "default"),
        };

        private static ObservableCollection<SIPRegistrarBinding> m_sipBindings = new ObservableCollection<SIPRegistrarBinding>()
        {
            new SIPRegistrarBinding(m_sipAccounts[0], SIPURI.ParseSIPURI("sip:dummy@127.0.0.1"), null, 1, "Dummy UAv1",
                 SIPEndPoint.ParseSIPEndPoint("127.0.0.1:5060"), SIPEndPoint.ParseSIPEndPoint("sip:10.0.0.1:5060"), SIPEndPoint.ParseSIPEndPoint("11.0.0.1:5060"), 3600)
        };

        private static ObservableCollection<SIPProvider> m_sipProviders = new ObservableCollection<SIPProvider>()
        {
            new SIPProvider(DUMMY_OWNER, "Provider1", "dummy", "password", SIPURI.ParseSIPURIRelaxed("dummy.com"), null, null, null, null, 0, null, null, null, false, true)
        };

        private static ObservableCollection<SIPDialPlan> m_dialPlans = new ObservableCollection<SIPDialPlan>()
        {
            new SIPDialPlan(DUMMY_OWNER, "default", null, "sys.Log('Hello World)", SIPDialPlanScriptTypesEnum.Ruby)
        };

        private static ObservableCollection<SIPProvider> m_sipProviderBindings = new ObservableCollection<SIPProvider>();
  
        public override event IsAliveCompleteDelegate IsAliveComplete;
        public override event LoginCompleteDelegate LoginComplete;
        public override event LogoutCompleteDelegate LogoutComplete;
        public override event GetSIPDomainsCompleteDelegate GetSIPDomainsComplete;
        public override event GetSIPAccountsCountCompleteDelegate GetSIPAccountsCountComplete;
        public override event GetSIPAccountsCompleteDelegate GetSIPAccountsComplete;
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
        public override event GetRegistrarBindingsCompleteDelegate GetRegistrarBindingsComplete;
        public override event GetRegistrarBindingsCountCompleteDelegate GetRegistrarBindingsCountComplete;
        public override event GetSIPProviderBindingsCompleteDelegate GetSIPProviderBindingsComplete;
        public override event GetSIPProviderBindingsCountCompleteDelegate GetSIPProviderBindingsCountComplete;
        public override event GetCallsCountCompleteDelegate GetCallsCountComplete;
        public override event GetCallsCompleteDelegate GetCallsComplete;
        public override event GetCDRsCountCompleteDelegate GetCDRsCountComplete;
        public override event GetCDRsCompleteDelegate GetCDRsComplete;

        public override void IsAliveAsync()
        {
            if (IsAliveComplete != null)
            {
                IsAliveComplete(new IsAliveCompletedEventArgs(new object[]{true}, null, false, null));
            }
        }

        public override void LoginAsync(string username, string password)
        {
            if (LoginComplete != null)
            {
                LoginComplete(new LoginCompletedEventArgs(new object[] { m_dummyAuthId }, null, false, null));
            } 
        }

        public override void LogoutAsync()
        {
            if (LogoutComplete != null)
            {
                LogoutComplete(new AsyncCompletedEventArgs(null, false, null));
            } 
        }

        public override void GetSIPDomainsAsync(string where, int offset, int count)
        { 
            if(GetSIPDomainsComplete != null)
            {
                GetSIPDomainsComplete(new GetSIPDomainsCompletedEventArgs(new object[]{m_sipDomains}, null, false, null));
            }
        }
        
        public override void GetSIPAccountsCountAsync(string where)
        {
            if (GetSIPAccountsCountComplete != null)
            {
                GetSIPAccountsCountComplete(new GetSIPAccountsCountCompletedEventArgs(new object[] { m_sipAccounts.Count }, null, false, null));
            }
        }

        public override void GetSIPAccountsAsync(string where, int offset, int count)
        {
            if (GetSIPAccountsComplete != null)
            {
                GetSIPAccountsComplete(new GetSIPAccountsCompletedEventArgs(new object[] { m_sipAccounts }, null, false, null));
            }
        }

        public override void AddSIPAccountAsync(SIPAccount sipAccount)
        {
            if (AddSIPAccountComplete != null)
            {
                AddSIPAccountComplete(new AddSIPAccountCompletedEventArgs(new object[]{sipAccount}, null, false, null));
            }
        }

        public override void UpdateSIPAccount(SIPAccount sipAccount)
        {
            if (UpdateSIPAccountComplete != null)
            {
                UpdateSIPAccountComplete(new UpdateSIPAccountCompletedEventArgs(new object[]{sipAccount}, null, false, null));
            }
        }

        public override void DeleteSIPAccount(SIPAccount sipAccount)
        {
            if (DeleteSIPAccountComplete != null)
            {
                DeleteSIPAccountComplete(new DeleteSIPAccountCompletedEventArgs(new object[]{sipAccount}, null, false, null));
            }
        }

        public override void GetRegistrarBindingsAsync(string where, int offset, int count)
        {
            if (GetRegistrarBindingsComplete != null)
            {
                GetRegistrarBindingsComplete(new GetSIPRegistrarBindingsCompletedEventArgs(new object[] { m_sipBindings }, null, false, null));
            }
        }

        public override void GetRegistrarBindingsCountAsync(string whereExpression)
        {
            if (GetRegistrarBindingsCountComplete != null)
            {
                GetRegistrarBindingsCountComplete(new GetSIPRegistrarBindingsCountCompletedEventArgs(new object[] { m_sipBindings.Count }, null, false, null));
            }
        }
        
        public override void GetDialPlansCountAsync(string where)
        {
            if (GetDialPlansCountComplete != null)
            {
                GetDialPlansCountComplete(new GetDialPlansCountCompletedEventArgs(new object[]{m_dialPlans.Count}, null, false, null));
            }
        }

        public override void GetDialPlansAsync(string where, int offset, int count)
        {
            if (GetDialPlansComplete != null)
            {
                GetDialPlansComplete(new GetDialPlansCompletedEventArgs(new object[]{m_dialPlans}, null, false, null));
            }
        }

        public override void DeleteDialPlanAsync(SIPDialPlan dialPlan)
        {
            if (DeleteDialPlanComplete != null)
            {
                DeleteDialPlanComplete(new DeleteDialPlanCompletedEventArgs(new object[] { dialPlan }, null, false, null));
            }
        }

        public override void UpdateDialPlanAsync(SIPDialPlan dialPlan)
        {
            if (UpdateDialPlanComplete != null)
            {
                UpdateDialPlanComplete(new UpdateDialPlanCompletedEventArgs(new object[] { dialPlan }, null, false, null));
            }
        }

        public override void AddDialPlanAsync(SIPDialPlan dialPlan)
        {
            if (AddDialPlanComplete != null)
            {
                AddDialPlanComplete(new AddDialPlanCompletedEventArgs(new object[] { dialPlan }, null, false, null));
            }
        }

        public override void GetSIPProvidersCountAsync(string where)
        {
            //getSIPProvidersCountCompleteHandler(new GetSIPProvidersCountCompletedEventArgs(new object[] { m_sipProviders.Count }, null, false, null));
        }

        public override void GetSIPProvidersAsync(string where, int offset, int count)
        {
            //getSIPProvidersCompleteHandler(new GetSIPProvidersCompletedEventArgs(new object[] { m_sipProviders }, null, false, null));
        }

        public override void DeleteSIPProviderAsync(SIPProvider sipProvider)
        {
            if (DeleteSIPProviderComplete != null)
            {
                DeleteSIPProviderComplete(new DeleteSIPProviderCompletedEventArgs(new object[] { sipProvider }, null, false, null));
            }
        }

        public override void UpdateSIPProviderAsync(SIPProvider sipProvider)
        {
            if (UpdateSIPProviderComplete != null)
            {
                UpdateSIPProviderComplete(new UpdateSIPProviderCompletedEventArgs(new object[]{sipProvider}, null, false, null));
            }
        }

        public override void AddSIPProviderAsync(SIPProvider sipProvider)
        {
            if (AddSIPProviderComplete != null)
            {
                AddSIPProviderComplete(new AddSIPProviderCompletedEventArgs(new object[] { sipProvider }, null, false, null));
            }
        }

        public override void GetSIPProviderBindingsAsync(string where, int offset, int count) {

        }

        public override void GetSIPProviderBindingsCountAsync(string whereExpression) {

        }

        public override void GetCallsCountAsync(string whereExpression)
        {
            throw new NotImplementedException();
        }

        public override void GetCallsAsync(string whereExpressionn, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override void GetCDRsCountAsync(string whereExpression)
        {
            throw new NotImplementedException();
        }

        public override void GetCDRsAsync(string whereExpressionn, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }
}
