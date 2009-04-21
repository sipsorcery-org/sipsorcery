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
using SIPSorcery.SIP.App;
using SIPSorcery.SIPSorceryProvisioningClient;

namespace SIPSorcery.Persistence
{
    public delegate void IsAliveCompleteDelegate(IsAliveCompletedEventArgs e);
    public delegate void LoginCompleteDelegate(LoginCompletedEventArgs e);
    public delegate void LogoutCompleteDelegate(AsyncCompletedEventArgs e);
    public delegate void GetSIPAccountsCountCompleteDelegate(GetSIPAccountsCountCompletedEventArgs e);
    public delegate void GetSIPAccountsCompleteDelegate(GetSIPAccountsCompletedEventArgs e);
    public delegate void AddSIPAccountCompleteDelegate(AddSIPAccountCompletedEventArgs e);
    public delegate void UpdateSIPAccountCompleteDelegate(UpdateSIPAccountCompletedEventArgs e);
    public delegate void DeleteSIPAccountCompleteDelegate(DeleteSIPAccountCompletedEventArgs e);
    public delegate void GetDialPlansCountCompleteDelegate(GetDialPlansCountCompletedEventArgs e);
    public delegate void GetDialPlansCompleteDelegate(GetDialPlansCompletedEventArgs e);
    public delegate void DeleteDialPlanCompleteDelegate(DeleteDialPlanCompletedEventArgs e);
    public delegate void AddDialPlanCompleteDelegate(AddDialPlanCompletedEventArgs e);
    public delegate void UpdateDialPlanCompleteDelegate(UpdateDialPlanCompletedEventArgs e);
    public delegate void GetSIPProvidersCountCompleteDelegate(GetSIPProvidersCountCompletedEventArgs e);
    public delegate void GetSIPProvidersCompleteDelegate(GetSIPProvidersCompletedEventArgs e);
    public delegate void DeleteSIPProviderCompleteDelegate(DeleteSIPProviderCompletedEventArgs e);
    public delegate void AddSIPProviderCompleteDelegate(AddSIPProviderCompletedEventArgs e);
    public delegate void UpdateSIPProviderCompleteDelegate(UpdateSIPProviderCompletedEventArgs e);
    public delegate void GetSIPDomainsCompleteDelegate(GetSIPDomainsCompletedEventArgs e);
    public delegate void GetRegistrarBindingsCompleteDelegate(GetSIPRegistrarBindingsCompletedEventArgs e);
    public delegate void GetRegistrarBindingsCountCompleteDelegate(GetSIPRegistrarBindingsCountCompletedEventArgs e);
    public delegate void GetSIPProviderBindingsCompleteDelegate(GetSIPProviderBindingsCompletedEventArgs e);
    public delegate void GetSIPProviderBindingsCountCompleteDelegate(GetSIPProviderBindingsCountCompletedEventArgs e);
    public delegate void GetCallsCountCompleteDelegate(GetCallsCountCompletedEventArgs e);
    public delegate void GetCallsCompleteDelegate(GetCallsCompletedEventArgs e);
    public delegate void GetCDRsCountCompleteDelegate(GetCDRsCountCompletedEventArgs e);
    public delegate void GetCDRsCompleteDelegate(GetCDRsCompletedEventArgs e);
    
    public enum SIPPersistorTypesEnum
    {
        Unknown = 0,
        GUITest = 1,        // Persistor that does not require any external communications and uses dummy asset lists. Allows the GUI to be worked on in isolation.
        WebService = 2,
        //HTTP = 3,
    }

    public class SIPSorceryPersistorFactory
    {
        public static SIPSorceryPersistor CreateSIPSorceryPersistor(SIPPersistorTypesEnum persistorType, string persistorConnStr, string authToken)
        {
            switch (persistorType)
            {
                case SIPPersistorTypesEnum.GUITest:
                    return new SIPSorceryGUITestPersistor();

                case SIPPersistorTypesEnum.WebService:
                    return new SIPSorceryWebServicePersistor(persistorConnStr, authToken);

                default:
                    throw new ArgumentException("Persistor type " + persistorType + " is not supported by the SIPSorcery persistor factory.");
            }
        }
    }

    public abstract class SIPSorceryPersistor
    {
        public abstract event IsAliveCompleteDelegate IsAliveComplete;
        public abstract event LoginCompleteDelegate LoginComplete;
        public abstract event LogoutCompleteDelegate LogoutComplete;
        public abstract event GetSIPAccountsCountCompleteDelegate GetSIPAccountsCountComplete;
        public abstract event GetSIPAccountsCompleteDelegate GetSIPAccountsComplete;
        public abstract event AddSIPAccountCompleteDelegate AddSIPAccountComplete;
        public abstract event UpdateSIPAccountCompleteDelegate UpdateSIPAccountComplete;
        public abstract event DeleteSIPAccountCompleteDelegate DeleteSIPAccountComplete;
        public abstract event GetDialPlansCountCompleteDelegate GetDialPlansCountComplete;
        public abstract event GetDialPlansCompleteDelegate GetDialPlansComplete;
        public abstract event DeleteDialPlanCompleteDelegate DeleteDialPlanComplete;
        public abstract event AddDialPlanCompleteDelegate AddDialPlanComplete;
        public abstract event UpdateDialPlanCompleteDelegate UpdateDialPlanComplete;
        public abstract event GetSIPProvidersCountCompleteDelegate GetSIPProvidersCountComplete;
        public abstract event GetSIPProvidersCompleteDelegate GetSIPProvidersComplete;
        public abstract event DeleteSIPProviderCompleteDelegate DeleteSIPProviderComplete;
        public abstract event AddSIPProviderCompleteDelegate AddSIPProviderComplete;
        public abstract event UpdateSIPProviderCompleteDelegate UpdateSIPProviderComplete;
        public abstract event GetSIPDomainsCompleteDelegate GetSIPDomainsComplete;
        public abstract event GetRegistrarBindingsCompleteDelegate GetRegistrarBindingsComplete;
        public abstract event GetRegistrarBindingsCountCompleteDelegate GetRegistrarBindingsCountComplete;
        public abstract event GetSIPProviderBindingsCompleteDelegate GetSIPProviderBindingsComplete;
        public abstract event GetSIPProviderBindingsCountCompleteDelegate GetSIPProviderBindingsCountComplete;
        public abstract event GetCallsCountCompleteDelegate GetCallsCountComplete;
        public abstract event GetCallsCompleteDelegate GetCallsComplete;
        public abstract event GetCDRsCountCompleteDelegate GetCDRsCountComplete;
        public abstract event GetCDRsCompleteDelegate GetCDRsComplete;

        public abstract void IsAliveAsync();
        public abstract void LoginAsync(string username, string password);
        public abstract void LogoutAsync();
        public abstract void GetSIPAccountsCountAsync(string where);    
        public abstract void GetSIPAccountsAsync(string where, int offset, int count);
        public abstract void AddSIPAccountAsync(SIPAccount sipAccount);
        public abstract void UpdateSIPAccount(SIPAccount sipAccount);
        public abstract void DeleteSIPAccount(SIPAccount sipAccount);
        public abstract void GetDialPlansCountAsync(string where);
        public abstract void GetDialPlansAsync(string where, int offset, int count);
        public abstract void DeleteDialPlanAsync(SIPDialPlan dialPlan);
        public abstract void UpdateDialPlanAsync(SIPDialPlan dialPlan);
        public abstract void AddDialPlanAsync(SIPDialPlan dialPlan);
        public abstract void GetSIPProvidersCountAsync(string where);
        public abstract void GetSIPProvidersAsync(string where, int offset, int count);
        public abstract void DeleteSIPProviderAsync(SIPProvider sipProvider);
        public abstract void UpdateSIPProviderAsync(SIPProvider sipProvider);
        public abstract void AddSIPProviderAsync(SIPProvider sipProvider);
        public abstract void GetSIPDomainsAsync(string where, int offset, int count);
        public abstract void GetRegistrarBindingsAsync(string where, int offset, int count);
        public abstract void GetRegistrarBindingsCountAsync(string whereExpression);
        public abstract void GetSIPProviderBindingsAsync(string where, int offset, int count);
        public abstract void GetSIPProviderBindingsCountAsync(string whereExpression);
        public abstract void GetCallsCountAsync(string whereExpression);
        public abstract void GetCallsAsync(string whereExpressionn, int offset, int count);
        public abstract void GetCDRsCountAsync(string whereExpression);
        public abstract void GetCDRsAsync(string whereExpressionn, int offset, int count);
    }
}
