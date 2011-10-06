using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.DomainServices.Client;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SIPSorcery.Entities;
using SIPSorcery.UIControls;
using SIPSorcery.Entities.Services;

namespace SIPSorcery
{
    public partial class SIPProviderManager : UserControl
	{
        private const int SIPPROVIDERS_DISPLAY_COUNT = 25;
        private const int SIPPROVIDERBINDINGS_DISPLAY_COUNT = 25;

        private ActivityMessageDelegate LogActivityMessage_External;

        private SIPEntitiesDomainContext m_riaContext;

        private bool m_sipProvidersPanelRefreshInProgress;
        private bool m_sipRegistrationAgentBindingsPanelRefreshInProgress;

        private string m_owner;
        private SIPProvider m_selectedSIPProvider;
        private SIPProviderDetailsControl m_addControl;
        private SIPProviderDetailsControl m_editControl;

        public bool Initialised { get; private set; }

        public SIPProviderManager()
        {
            InitializeComponent();
        }

        public SIPProviderManager(
            ActivityMessageDelegate logActivityMessage,
            string owner,
            SIPEntitiesDomainContext riaContext)
		{
			InitializeComponent();

            LogActivityMessage_External = logActivityMessage;
            m_owner = owner;
            m_riaContext = riaContext;

            m_sipProvidersPanel.SetTitle("SIP Providers");
            m_sipProvidersPanel.DisplayCount = SIPPROVIDERS_DISPLAY_COUNT;
            m_sipProvidersPanel.MenuEnableFilter(false);
            m_sipProvidersPanel.MenuEnableHelp(false);
            m_sipProvidersPanel.Add += SIPProvidersPanel_Add;
            m_sipProvidersPanel.GetAssetList = GetSIPProviders;

            m_sipProviderRegistrationsPanel.SetTitle("SIP Provider Registrations");
            m_sipProviderRegistrationsPanel.DisplayCount = SIPPROVIDERBINDINGS_DISPLAY_COUNT;
            m_sipProviderRegistrationsPanel.MenuEnableAdd(false);
            m_sipProviderRegistrationsPanel.MenuEnableFilter(false);
            m_sipProviderRegistrationsPanel.MenuEnableHelp(false);
            m_sipProviderRegistrationsPanel.GetAssetList = GetSIPProviderBindings;
		}

        public void Initialise()
        {
            Initialised = true;
            GetSIPProviders(0, SIPPROVIDERS_DISPLAY_COUNT);
            GetSIPProviderBindings(0, SIPPROVIDERBINDINGS_DISPLAY_COUNT);
        }

        public void SIPMonitorMachineEventHandler(SIPSorcery.SIP.App.SIPMonitorMachineEvent machineEvent)
        {
            // Update the bindings display.
            if (!m_sipRegistrationAgentBindingsPanelRefreshInProgress)
            {
                m_sipProviderRegistrationsPanel.RefreshAsync();
            }
        }

        #region SIP Provider functions.

        private void SIPProvidersPanel_Add()
        {
            m_selectedSIPProvider = null;
            m_addControl = new SIPProviderDetailsControl(DetailsControlModesEnum.Add, m_selectedSIPProvider, m_owner, SIPProviderAdd, null, DetailsControlClosed);
            m_sipProvidersPanel.SetDetailsElement(m_addControl);
        }

        private void GetSIPProviders(int offset, int count)
        {
            if (!m_sipProvidersPanelRefreshInProgress)
            {
                m_sipProvidersPanelRefreshInProgress = true;

                m_riaContext.SIPProviders.Clear();
                var query = m_riaContext.GetSIPProvidersQuery().OrderBy(x => x.ProviderName).Skip(offset).Take(count);
                query.IncludeTotalCount = true;
                m_riaContext.Load(query, LoadBehavior.RefreshCurrent, GetSIPProvidersCompleted, null);
            }
            else
            {
                LogActivityMessage_External(MessageLevelsEnum.Warn, "A SIP Providers refresh is already in progress.");
            }
        }

        private void GetSIPProvidersCompleted(LoadOperation<SIPProvider> lo)
        {
            if (lo.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error loading the SIP Providers. " + lo.Error.Message);
                lo.MarkErrorAsHandled();
            }
            else
            {
                m_sipProvidersPanel.AssetListTotal = lo.TotalEntityCount;
                m_sipProvidersPanel.SetAssetListSource(m_riaContext.SIPProviders);
                //LogActivityMessage_External(MessageLevelsEnum.Info, "SIP Provders successfully loaded, total " + lo.TotalEntityCount + " " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + ".");
            }

            m_sipProvidersPanelRefreshInProgress = false;
        }

        private void UpdateSIPProvider(SIPProvider sipProvider)
        {
            m_riaContext.SubmitChanges(UpdateSIPProviderComplete, sipProvider);
        }

        private void UpdateSIPProviderComplete(SubmitOperation so)
        {
            if (so.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error updating the SIP provider. " + so.Error.Message);
                so.MarkErrorAsHandled();
                m_riaContext.RejectChanges();
            }
            else
            {
                if (m_editControl != null)
                {
                    SIPProvider sipProvider = (SIPProvider)so.UserState;
                    m_editControl.WriteStatusMessage(MessageLevelsEnum.Info, "Update completed successfully for SIP provider " + sipProvider.ProviderName + ".");
                }
            }
        }

        private void SIPProvidersDataGrid_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (m_riaContext.SIPProviders.Count() > 0)
                {
                     DataGrid dataGrid = (DataGrid)sender;
                     if (dataGrid.CurrentColumn.Header as string != "Delete")
                     {
                         SIPProvider sipProvider = (SIPProvider)m_sipProvidersDataGrid.SelectedItem;

                         if (m_selectedSIPProvider == null || m_selectedSIPProvider != sipProvider)
                         {
                             m_selectedSIPProvider = sipProvider;
                             m_editControl = new SIPProviderDetailsControl(DetailsControlModesEnum.Edit, m_selectedSIPProvider, m_owner, null, UpdateSIPProvider, DetailsControlClosed);
                             m_sipProvidersPanel.SetDetailsElement(m_editControl);
                         }
                     }
                }
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                LogActivityMessage_External(MessageLevelsEnum.Error, "Exception showing SIP Provider details. " + excpMessage);
                m_selectedSIPProvider = null;
            }
        }

        private void SIPProviderAdd(SIPProvider sipProvider)
        {
            m_riaContext.SIPProviders.Add(sipProvider);
            m_riaContext.SubmitChanges(SIPProviderAddComplete, sipProvider);
        }

        private void SIPProviderAddComplete(SubmitOperation so)
        {
            if (so.HasError)
            {
                if (m_addControl != null)
                {
                    m_addControl.WriteStatusMessage(MessageLevelsEnum.Error, so.Error.Message);
                }
                else
                {
                    LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error adding a SIP Provider. " + so.Error.Message);
                }

                m_riaContext.SIPProviders.Remove((SIPProvider)so.UserState);
                so.MarkErrorAsHandled();
            }
            else
            {
                if (m_addControl != null)
                {
                    SIPProvider sipProvider = (SIPProvider)so.UserState;
                    m_addControl.WriteStatusMessage(MessageLevelsEnum.Info, "SIP Provider was successfully created for " + sipProvider.ProviderName + ".");
                }

                m_sipProvidersPanel.AssetAdded();
            }
        }

        public void ShowPassword(Object sender, MouseEventArgs e)
        {
            StackPanel panel = (StackPanel)sender;
            panel.Children[0].Visibility = Visibility.Collapsed;
            panel.Children[1].Visibility = Visibility.Visible;
        }

        public void HidePassword(Object sender, MouseEventArgs e)
        {
            StackPanel panel = (StackPanel)sender;
            panel.Children[0].Visibility = Visibility.Visible;
            panel.Children[1].Visibility = Visibility.Collapsed;
        }

        private void DeleteSIPProvider(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SIPProvider sipProvider = m_sipProvidersDataGrid.SelectedItem as SIPProvider;
            MessageBoxResult confirmDelete = MessageBox.Show("Press Ok to delete " + sipProvider.ProviderName + ".", "Confirm Delete", MessageBoxButton.OKCancel);
            if (confirmDelete == MessageBoxResult.OK)
            {
                //LogActivityMessage_External(MessageLevelsEnum.Info, "Sending delete request for SIP Provider " + sipProvider.ProviderName + ".");
                m_riaContext.SIPProviders.Remove(sipProvider);
                m_riaContext.SubmitChanges(DeleteSIPProviderComplete, sipProvider);
            }
        }

        private void DeleteSIPProviderComplete(SubmitOperation so)
        {
            if (so.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error deleting the SIP provider. " + so.Error.Message);
                so.MarkErrorAsHandled();
            }
            else
            {
                SIPProvider sipProvider = (SIPProvider)so.UserState;
                //LogActivityMessage_External(MessageLevelsEnum.Info, "Delete completed successfully for " + sipProvider.ProviderName + ".");
                m_sipProvidersPanel.AssetDeleted();
            }
        }

        #endregion

        #region SIP Registration Agent Bindings Functions.

        private void GetSIPProviderBindings(int offset, int count)
        {
            if (!m_sipRegistrationAgentBindingsPanelRefreshInProgress)
            {
                m_sipRegistrationAgentBindingsPanelRefreshInProgress = true;

                m_riaContext.SIPProviderBindings.Clear();
                var query = m_riaContext.GetSIPProviderBindingsQuery().OrderBy(x => x.ProviderName).Skip(offset).Take(count);
                query.IncludeTotalCount = true;
                m_riaContext.Load(query, LoadBehavior.RefreshCurrent, GetSIPProviderBindingsCompleted, null);
            }
            else
            {
                LogActivityMessage_External(MessageLevelsEnum.Warn, "A SIP Provider Bindings refresh is already in progress.");
            }
        }

        private void GetSIPProviderBindingsCompleted(LoadOperation<SIPProviderBinding> lo)
        {
            if (lo.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error retrieving SIP provider bindings. " + lo.Error.Message);
                lo.MarkErrorAsHandled();
            }
            else
            {
                m_sipProviderRegistrationsPanel.AssetListTotal = lo.TotalEntityCount;
                m_sipProviderRegistrationsPanel.SetAssetListSource(m_riaContext.SIPProviderBindings);
                LogActivityMessage_External(MessageLevelsEnum.Info, lo.TotalEntityCount + " SIP provider bindings are registered.");
            }

            m_sipRegistrationAgentBindingsPanelRefreshInProgress = false;
        }

        #endregion

        private void DetailsControlClosed()
        {
            m_sipProvidersPanel.CloseDetailsPane();
            m_selectedSIPProvider = null;
        }
	}
}