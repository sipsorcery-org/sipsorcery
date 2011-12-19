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
    public partial class SIPAccountManager : UserControl
    {
        private const int SIPACCOUNTS_DISPLAY_COUNT = 25;
        private const int SIPBINDINGS_DISPLAY_COUNT = 25;

        private ActivityMessageDelegate LogActivityMessage_External;
        private SIPEntitiesDomainContext m_riaContext;

        private SIPAccount m_selectedSIPAccount;
        private SIPAccountDetailsControl m_addControl;
        private SIPAccountDetailsControl m_editControl;

        private string m_owner;
        public bool Initialised;
        private bool m_sipAccountsPanelRefreshInProgress;
        private bool m_sipBindingsPanelRefreshInProgress;

        public SIPAccountManager()
        {
            InitializeComponent();
        }

        public SIPAccountManager(
            ActivityMessageDelegate logActivityMessage,
            string owner,
            SIPEntitiesDomainContext riaContext)
        {
            InitializeComponent();

            LogActivityMessage_External = logActivityMessage;
            m_owner = owner;
            m_riaContext = riaContext;

            m_sipAccountsPanel.SetTitle("SIP Accounts");
            m_sipAccountsPanel.DisplayCount = SIPACCOUNTS_DISPLAY_COUNT;
            m_sipAccountsPanel.MenuEnableFilter(false);
            m_sipAccountsPanel.MenuEnableHelp(false);
            m_sipAccountsPanel.Add += SIPAccountsAssetViewer_Add;
            m_sipAccountsPanel.GetAssetList = GetSIPAccounts;

            m_sipBindingsPanel.SetTitle("SIP Bindings");
            m_sipBindingsPanel.DisplayCount = SIPBINDINGS_DISPLAY_COUNT;
            m_sipBindingsPanel.MenuEnableAdd(false);
            m_sipBindingsPanel.MenuEnableFilter(false);
            m_sipBindingsPanel.MenuEnableHelp(false);
            m_sipBindingsPanel.GetAssetList = GetSIPBindings;
        }

        public void Initialise()
        {
            Initialised = true;

            GetSIPAccounts(0, SIPACCOUNTS_DISPLAY_COUNT);
            GetSIPBindings(0, SIPBINDINGS_DISPLAY_COUNT);
        }

        public void SIPMonitorMachineEventHandler(SIPSorcery.SIP.App.SIPMonitorMachineEvent machineEvent)
        {
            // Update the bindings display.
            if (!m_sipAccountsPanelRefreshInProgress)
            {
                m_sipBindingsPanel.RefreshAsync();
            }
        }

        #region SIP Account functions.

        private void GetSIPAccounts(int offset, int count)
        {
            if (!m_sipAccountsPanelRefreshInProgress)
            {
                m_sipAccountsPanelRefreshInProgress = true;
                m_riaContext.SIPAccounts.Clear();
                var query = m_riaContext.GetSIPAccountsQuery().OrderBy(x => x.SIPUsername).Skip(offset).Take(count);
                query.IncludeTotalCount = true;
                m_riaContext.Load(query, LoadBehavior.RefreshCurrent, GetSIPAccountsCompleted, null);
            }
            else
            {
                LogActivityMessage_External(MessageLevelsEnum.Warn, "A SIP accounts refresh is already in progress.");
            }
        }

        private void GetSIPAccountsCompleted(LoadOperation<SIPAccount> lo)
        {
            if (lo.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error loading the SIP Accounts. " + lo.Error.Message);
                lo.MarkErrorAsHandled();
            }
            else
            {
                m_sipAccountsPanel.AssetListTotal = lo.TotalEntityCount;
                m_sipAccountsPanel.SetAssetListSource(m_riaContext.SIPAccounts);
                //LogActivityMessage_External(MessageLevelsEnum.Info, "SIP Accounts successfully loaded, total " + lo.TotalEntityCount + " " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + ".");
            }

            m_sipAccountsPanelRefreshInProgress = false;
        }

        private void SIPAccountAdd(SIPAccount sipAccount)
        {
            m_riaContext.SIPAccounts.Add(sipAccount);
            m_riaContext.SubmitChanges(SIPAccountAddComplete, sipAccount);
        }

        private void SIPAccountAddComplete(SubmitOperation so)
        {
            if (so.HasError)
            {
                if (m_addControl != null)
                {
                    m_addControl.WriteStatusMessage(MessageLevelsEnum.Error, so.Error.Message);
                }
                else
                {
                    LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error adding a SIP Account. " + so.Error.Message);
                }

                m_riaContext.SIPAccounts.Remove((SIPAccount)so.UserState);
                so.MarkErrorAsHandled();
            }
            else
            {
                if (m_addControl != null)
                {
                    SIPAccount sipAccount = (SIPAccount)so.UserState;
                    SIPAccountsAssetViewer_Add();
                    m_addControl.WriteStatusMessage(MessageLevelsEnum.Info, "SIP Account was successfully created for " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + ".");
                }

                m_sipAccountsPanel.AssetAdded();
            }
        }

        private void SIPAccountUpdate(SIPAccount sipAccount)
        {
            //LogActivityMessage_External(MessageLevelsEnum.Info, "Attempting to update " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + ".");
            m_riaContext.SubmitChanges(UpdateSIPAccountComplete, sipAccount);
        }

        private void UpdateSIPAccountComplete(SubmitOperation so)
        {
            if (so.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error updating the SIP account. " + so.Error.Message);
                so.MarkErrorAsHandled();
            }
            else
            {
                if (m_editControl != null)
                {
                    SIPAccount sipAccount = (SIPAccount)so.UserState;
                    m_editControl.WriteStatusMessage(MessageLevelsEnum.Info, "Update completed successfully for " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + ".");
                }
            }
        }

        private void SIPAccountsAssetViewer_Add()
        {
            if (m_riaContext.SIPDomains.Count() == 0)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "A SIP account cannot be added as there are no available SIP domains loaded.");
            }
            else
            {
                m_selectedSIPAccount = null;
                m_addControl = new SIPAccountDetailsControl(DetailsControlModesEnum.Add, null, m_owner, SIPAccountAdd, null, DetailsControlClosed, m_riaContext);
                m_sipAccountsPanel.SetDetailsElement(m_addControl);
            }
        }

        private void SIPAccountsDataGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (m_riaContext.SIPAccounts.Count() > 0)
                {
                    DataGrid dataGrid = (DataGrid)sender;
                    if (dataGrid.SelectedItem != null && dataGrid.CurrentColumn.Header as string != "Delete")
                    {
                        SIPAccount sipAccount = (SIPAccount)m_sipAccountsDataGrid.SelectedItem;

                        if (m_selectedSIPAccount == null || m_selectedSIPAccount != sipAccount)
                        {
                            m_selectedSIPAccount = sipAccount;
                            m_editControl = new SIPAccountDetailsControl(DetailsControlModesEnum.Edit, sipAccount, m_owner, null, SIPAccountUpdate, DetailsControlClosed, m_riaContext);
                            m_sipAccountsPanel.SetDetailsElement(m_editControl);
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "Exception showing SIP account details. " + excp.Message);
                m_selectedSIPAccount = null;
            }
        }

        private void DeleteSIPAccount(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SIPAccount sipAccount = m_sipAccountsDataGrid.SelectedItem as SIPAccount;
            MessageBoxResult confirmDelete = MessageBox.Show("Press Ok to delete " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + ".", "Confirm Delete", MessageBoxButton.OKCancel);
            if (confirmDelete == MessageBoxResult.OK)
            {
                //LogActivityMessage_External(MessageLevelsEnum.Info, "Sending delete request for SIP Account " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + ".");
                m_riaContext.SIPAccounts.Remove(sipAccount);
                m_riaContext.SubmitChanges(DeleteSIPAccountComplete, sipAccount);
            }
        }

        private void DeleteSIPAccountComplete(SubmitOperation so)
        {
            if (so.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error deleting the SIP account. " + so.Error.Message);
                so.MarkErrorAsHandled();
            }
            else
            {
                SIPAccount sipAccount = (SIPAccount)so.UserState;
                //LogActivityMessage_External(MessageLevelsEnum.Info, "Delete completed successfully for " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + ".");
                m_sipAccountsPanel.AssetDeleted();
            }
        }

        private void ShowPassword(Object sender, MouseEventArgs e)
        {
            StackPanel panel = (StackPanel)sender;
            panel.Children[0].Visibility = Visibility.Collapsed;
            panel.Children[1].Visibility = Visibility.Visible;
        }

        private void HidePassword(Object sender, MouseEventArgs e)
        {
            StackPanel panel = (StackPanel)sender;
            panel.Children[0].Visibility = Visibility.Visible;
            panel.Children[1].Visibility = Visibility.Collapsed;
        }

        #endregion

        #region SIP Registrar Bindings Functions.

        private void GetSIPBindings(int offset, int count)
        {
            if (!m_sipBindingsPanelRefreshInProgress)
            {
                m_sipBindingsPanelRefreshInProgress = true;

                m_riaContext.SIPRegistrarBindings.Clear();
                var query = m_riaContext.GetSIPRegistrarBindingsQuery().OrderBy(x => x.SIPAccountName).Skip(offset).Take(count);
                query.IncludeTotalCount = true;
                m_riaContext.Load(query, LoadBehavior.RefreshCurrent, SIRegistrarBindingsLoaded, null);
            }
            else
            {
                LogActivityMessage_External(MessageLevelsEnum.Warn, "A SIP bindings refresh is already in progress.");
            }
        }

        private void SIRegistrarBindingsLoaded(LoadOperation<SIPRegistrarBinding> lo)
        {
            if (lo.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error retrieving SIP registrar bindings. " + lo.Error.Message);
                lo.MarkErrorAsHandled();
            }
            else
            {
                m_sipBindingsPanel.AssetListTotal = lo.TotalEntityCount;
                m_sipBindingsPanel.SetAssetListSource(m_riaContext.SIPRegistrarBindings);
                LogActivityMessage_External(MessageLevelsEnum.Info, lo.TotalEntityCount + " SIP registrar bindings are registered.");
            }

            m_sipBindingsPanelRefreshInProgress = false;
        }

        #endregion

        private void DetailsControlClosed()
        {
            m_selectedSIPAccount = null;
            m_sipAccountsPanel.CloseDetailsPane();
        }
    }
}