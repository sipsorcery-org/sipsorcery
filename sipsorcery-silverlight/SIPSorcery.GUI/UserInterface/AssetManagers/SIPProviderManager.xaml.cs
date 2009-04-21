using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.ServiceModel;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SIPSorcery.SIP.App;
using SIPSorcery.Persistence;
using SIPSorcery.Silverlight.Messaging;
using SIPSorcery.SIPSorceryProvisioningClient;
using SIPSorcery.UIControls;

namespace SIPSorcery
{
    public partial class SIPProviderManager : UserControl
	{
        private ActivityMessageDelegate LogActivityMessage_External;
        private ActivityProgressDelegate ShowActivityProgress_External;
        private SIPSorceryPersistor m_persistor;

        private int m_sipProvidersPanelOffset;
        private int m_sipProvidersPanelCount;
        private int m_sipRegistrationAgentBindingsPanelOffset;
        private int m_sipRegistrationAgentBindingsPanelCount;

        private bool m_initialLoadComplete;
        private bool m_sipProvidersCountComplete;
        private bool m_sipProvidersLoadComplete;
        private bool m_sipProvidersLoadInProgress;
        private bool m_sipProvidersPanelRefreshInProgress;
        private bool m_sipRegistrationAgentBindingsLoaded;
        private bool m_sipRegistrationAgentBindingsPanelRefreshInProgress;

        private string m_owner;
        private string m_sipProvidersWhere;                     // Used when filtering is enabled.
        private string m_sipProviderBindingsWhere;              // Used when filtering is enabled.
        private ObservableCollection<SIPProvider> m_sipProviders;
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
            ActivityProgressDelegate showActivityProgress,
            SIPSorceryPersistor persistor,
            string owner)
		{
			InitializeComponent();

            LogActivityMessage_External = logActivityMessage;
            ShowActivityProgress_External = showActivityProgress;
            m_persistor = persistor;
            m_owner = owner;
            m_sipProviderBindingsWhere = "RegisterActive = True";

            m_sipProvidersPanel.SetTitle("SIP Providers");
            m_sipProvidersPanel.MenuEnableFilter(false);
            m_sipProvidersPanel.MenuEnableDelete(false);
            m_sipProvidersPanel.Add += new MenuButtonClickedDelegate(SIPProvidersPanel_Add);
            m_sipProvidersPanel.GetAssetList = GetSIPProviders;

            m_sipProviderRegistrationsPanel.SetTitle("SIP Provider Registrations");
            m_sipProviderRegistrationsPanel.MenuEnableAdd(false);
            m_sipProviderRegistrationsPanel.MenuEnableFilter(false);
            m_sipProviderRegistrationsPanel.MenuEnableDelete(false);
            m_sipProviderRegistrationsPanel.GetAssetList = GetSIPProviderBindings;
		}

        public void Initialise()
        {
            Initialised = true;

            m_persistor.GetSIPProvidersCountComplete += GetSIPProvidersCountComplete;
            m_persistor.GetSIPProvidersComplete += GetSIPProvidersComplete;
            m_persistor.UpdateSIPProviderComplete += UpdateSIPProviderComplete;
            m_persistor.AddSIPProviderComplete += AddSIPProviderComplete;
            m_persistor.DeleteSIPProviderComplete += DeleteSIPProviderComplete;
            m_persistor.GetSIPProviderBindingsCountComplete += GetSIPProviderBindingsCountComplete;
            m_persistor.GetSIPProviderBindingsComplete += GetSIPProviderBindingsComplete;

            Load();
        }

        private void Load()
        {
            if (!m_sipProvidersCountComplete)
            {
                m_persistor.GetSIPProvidersCountAsync(m_sipProvidersWhere);
            }
            else if (!m_sipProvidersLoadComplete)
            {
                LogActivityMessage_External(MessageLevelsEnum.Info, "Loading SIP Providers...");
                m_sipProvidersPanel.RefreshAsync();
            }
            else if (!m_sipRegistrationAgentBindingsLoaded)
            {
                LogActivityMessage_External(MessageLevelsEnum.Info, "Loading SIP Provider Bindings...");
                m_sipProviderRegistrationsPanel.RefreshAsync();
            }
            else
            {
                m_initialLoadComplete = true;
            }
        }

        public void SIPMonitorMachineEventHandler(SIPMonitorMachineEvent machineEvent)
        {
            // Update the bindings display.
            if (m_initialLoadComplete && !m_sipRegistrationAgentBindingsPanelRefreshInProgress)
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
                m_sipProvidersLoadInProgress = true;

                m_sipProvidersPanelOffset = offset;
                m_sipProvidersPanelCount = count;
                m_persistor.GetSIPProvidersCountAsync(m_sipProvidersWhere);
            }
            else
            {
                LogActivityMessage_External(MessageLevelsEnum.Warn, "A SIP Providers refresh is already in progress.");
            }
        }

        private void GetSIPProvidersCountComplete(GetSIPProvidersCountCompletedEventArgs e)
        {
            m_sipProvidersCountComplete = true;

            try
            {
                m_sipProvidersPanel.AssetListTotal = e.Result;
                LogActivityMessage_External(MessageLevelsEnum.Info, "SIP Providers count " + e.Result + " " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + ".");
                m_persistor.GetSIPProvidersAsync(m_sipProvidersWhere, m_sipProvidersPanelOffset, m_sipProvidersPanelCount);
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error retrieving the SIP Provider count. " + excpMessage);

                m_sipProvidersPanelRefreshInProgress = false;
                m_sipProvidersLoadInProgress = false;
            }
        }

        private void GetSIPProvidersComplete(GetSIPProvidersCompletedEventArgs e)
        {
            m_sipProvidersLoadComplete = true;

            try
            {
                m_sipProviders = e.Result;
                m_sipProvidersPanel.SetAssetListSource(m_sipProviders);

                LogActivityMessage_External(MessageLevelsEnum.Info, "SIP Providers successfully loaded " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + ".");

                if (!m_initialLoadComplete)
                {
                    Load();
                }
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error retrieving SIP Providers. " + excpMessage);
            }
            finally
            {
                m_sipProvidersPanelRefreshInProgress = false;
                m_sipProvidersLoadInProgress = false;
            }
        }

        private void UpdateSIPProvider(SIPProvider sipProvider)
        {
            //LogActivityMessage_External(MessageLevelsEnum.Info, "Attempting to update SIP Provider " + sipProvider.ProviderName + ".");
            m_persistor.UpdateSIPProviderAsync(sipProvider);
        }

        private void UpdateSIPProviderComplete(UpdateSIPProviderCompletedEventArgs e)
        {
            try
            {
                // The SIP Provider returned by the web service call is the account after the update operation was carried out.
                SIPProvider sipProvider = e.Result;
                //LogActivityMessage_External(MessageLevelsEnum.Info, "Update completed successfully for " + sipProvider.ProviderName + ".");
                if (m_editControl != null)
                {
                    m_editControl.WriteStatusMessage(MessageLevelsEnum.Info, "Update completed successfully for " + sipProvider.ProviderName + ".");
                }
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                //LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error performing a SIP Provider update." + excpMessage);
                if (m_editControl != null)
                {
                    m_editControl.WriteStatusMessage(MessageLevelsEnum.Error, "There was an error performing a SIP Provider update." + excpMessage);
                }
            }
        }

        private void SIPProvidersDataGrid_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (m_initialLoadComplete && !m_sipProvidersLoadInProgress && m_sipProviders.Count > 0)
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
            m_persistor.AddSIPProviderAsync(sipProvider);
        }

        private void AddSIPProviderComplete(AddSIPProviderCompletedEventArgs e)
        {
            try
            {
                SIPProvider sipProvider = e.Result;

                if(m_addControl != null)
                {
                    m_addControl.WriteStatusMessage(MessageLevelsEnum.Info, "SIP Provider was successfully created for " + sipProvider.ProviderName + ".");
                }

                if (m_sipProviders == null)
                {
                    m_sipProvidersPanel.RefreshAsync();
                }
                else
                {
                    m_sipProviders.Add(sipProvider);
                    m_sipProvidersPanel.AssetAdded();
                }
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                if (m_addControl != null)
                {
                    m_addControl.WriteStatusMessage(MessageLevelsEnum.Error, "Error adding SIP Provider. " + excpMessage);
                }
                else
                {
                    LogActivityMessage_External(MessageLevelsEnum.Error, "Error adding SIP Provider. " + excpMessage);
                }
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
                LogActivityMessage_External(MessageLevelsEnum.Info, "Sending delete request for SIP Provider " + sipProvider.ProviderName + ".");
                m_persistor.DeleteSIPProviderAsync(sipProvider);
            }
        }

        private void DeleteSIPProviderComplete(DeleteSIPProviderCompletedEventArgs e)
        {
            try
            {
                SIPProvider sipProvider = e.Result;

                LogActivityMessage_External(MessageLevelsEnum.Info, "Delete completed successfully for " + sipProvider.ProviderName + ".");

                for (int index = 0; index < m_sipProviders.Count; index++)
                {
                    if (m_sipProviders[index].Id == sipProvider.Id)
                    {
                        m_sipProviders.RemoveAt(index);
                        break;
                    }
                }

                m_sipProvidersPanel.AssetDeleted();
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error deleting the SIP Provider. " + excpMessage);
            }
        }

        #endregion

        #region SIP Registration Agent Bindings Functions.

        private void GetSIPProviderBindings(int offset, int count)
        {
            if (!m_sipRegistrationAgentBindingsPanelRefreshInProgress)
            {
                m_sipRegistrationAgentBindingsPanelRefreshInProgress = true;

                m_sipRegistrationAgentBindingsPanelOffset = offset;
                m_sipRegistrationAgentBindingsPanelCount = count;
                m_persistor.GetSIPProviderBindingsCountAsync(m_sipProviderBindingsWhere);
            }
            else
            {
                LogActivityMessage_External(MessageLevelsEnum.Warn, "A SIP Provider Bindings refresh is already in progress.");
            }
        }

        private void GetSIPProviderBindingsCountComplete(GetSIPProviderBindingsCountCompletedEventArgs e)
        {
            try
            {
                m_sipProviderRegistrationsPanel.AssetListTotal = e.Result;
                LogActivityMessage_External(MessageLevelsEnum.Info, e.Result + " SIP Provider Bindings are registered.");
                m_persistor.GetSIPProviderBindingsAsync(m_sipProviderBindingsWhere, m_sipRegistrationAgentBindingsPanelOffset, m_sipRegistrationAgentBindingsPanelCount);
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error retrieving the number of SIP Provider bindings. " + excpMessage);

                m_sipRegistrationAgentBindingsPanelRefreshInProgress = false;
            }
        }

        private void GetSIPProviderBindingsComplete(GetSIPProviderBindingsCompletedEventArgs e)
        {
            m_sipRegistrationAgentBindingsLoaded = true;

            try
            {
                m_sipProviderRegistrationsPanel.SetAssetListSource(e.Result);

                if (!m_initialLoadComplete)
                {
                    Load();
                }

                LogActivityMessage_External(MessageLevelsEnum.Info, "SIP Provider Bindings successfully loaded.");
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error retrieving the SIP Provider bindings. " + excpMessage);
            }
            finally
            {
                m_sipRegistrationAgentBindingsPanelRefreshInProgress = false;
            }
        }

        #endregion

        private void DetailsControlClosed()
        {
            m_sipProvidersPanel.CloseDetailsPane();
            m_selectedSIPProvider = null;
        }
	}
}