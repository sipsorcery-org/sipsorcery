using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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
using SIPSorcery.UIControls;
using SIPSorcery.SIPSorceryProvisioningClient;

namespace SIPSorcery
{   
    public delegate void ManagerHeightChanged(double newHeight);

    public partial class SIPAccountManager : UserControl
	{
        private const int SIPACCOUNTS_DISPLAY_COUNT = 25;
        private const int SIPBINDINGS_DISPLAY_COUNT = 25;

        private ActivityMessageDelegate LogActivityMessage_External;
        private ActivityProgressDelegate ShowActivityProgress_External;
        private GetDialPlanNamesDelegate GetDialPlanNames_External;
        private SIPSorceryPersistor m_persistor;

        private ObservableCollection<SIPAccount> m_sipAccounts;
        private Dictionary<string, List<string>> m_ownerDomains = new Dictionary<string, List<string>>();           // [owner, List<domain names>]. 

        private SIPAccount m_selectedSIPAccount;
        private SIPAccountDetailsControl m_addControl;
        private SIPAccountDetailsControl m_editControl;
        private int m_sipAccountsPanelOffset;
        private int m_sipAccountsPanelCount;
        private int m_sipBindingsPanelOffset;
        private int m_sipBindingsPanelCount;

        private string m_owner;
        private string m_sipDomainsWhere;       // Used when filtering is enabled.
        private string m_sipAccountsWhere;      // Used when filtering is enabled.
        private string m_sipBindingsWhere;      // Used when filtering is enabled.

        public bool Initialised;
        private bool m_initialLoadComplete;
        private bool m_sipDomainsLoaded;
        private bool m_sipAccountsLoaded;
        private bool m_sipBindingsLoaded;
        private bool m_sipAccountLoadInProgress;
        private bool m_sipAccountsPanelRefreshInProgress;
        private bool m_sipBindingsPanelRefreshInProgress;

        public SIPAccountManager()
        {
            InitializeComponent();
        }

        public SIPAccountManager(
            ActivityMessageDelegate logActivityMessage,
            ActivityProgressDelegate showActivityProgress,
            SIPSorceryPersistor persistor,
            GetDialPlanNamesDelegate getDialPlanNames,
            string owner)
		{
			InitializeComponent();

            LogActivityMessage_External = logActivityMessage;
            ShowActivityProgress_External = showActivityProgress;
            GetDialPlanNames_External = getDialPlanNames;
            m_persistor = persistor;
            m_owner = owner;

            m_sipAccountsPanel.SetTitle("SIP Accounts");
            m_sipAccountsPanel.DisplayCount = SIPACCOUNTS_DISPLAY_COUNT;
            m_sipAccountsPanel.MenuEnableFilter(false);
            m_sipAccountsPanel.MenuEnableDelete(false);
            m_sipAccountsPanel.Add += new MenuButtonClickedDelegate(SIPAccountsAssetViewer_Add);
            m_sipAccountsPanel.GetAssetList = GetSIPAccounts;

            m_sipBindingsPanel.SetTitle("SIP Bindings");
            m_sipBindingsPanel.DisplayCount = SIPBINDINGS_DISPLAY_COUNT;
            m_sipBindingsPanel.MenuEnableAdd(false);
            m_sipBindingsPanel.MenuEnableFilter(false);
            m_sipBindingsPanel.MenuEnableDelete(false);
            m_sipBindingsPanel.GetAssetList = GetSIPBindings;
		}

        public void Initialise()
        {
            Initialised = true;

            m_persistor.GetSIPDomainsComplete += GetSIPDomainsComplete;
            m_persistor.GetSIPAccountsCountComplete += GetSIPAccountsCountComplete;
            m_persistor.GetSIPAccountsComplete += GetSIPAccountsComplete;
            m_persistor.UpdateSIPAccountComplete += UpdateSIPAccountComplete;
            m_persistor.DeleteSIPAccountComplete += DeleteSIPAccountComplete;
            m_persistor.AddSIPAccountComplete += AddSIPAccountComplete;
            m_persistor.GetRegistrarBindingsCountComplete += GetRegistrarBindingsCountComplete;
            m_persistor.GetRegistrarBindingsComplete += GetRegistrarBindingsComplete;

            Load();
        }

        private void Load()
        {
            try
            {
                if (!m_sipDomainsLoaded)
                {
                    ShowActivityProgress_External(25);
                    LogActivityMessage_External(MessageLevelsEnum.Info, "Loading SIP Domains...");
                    m_persistor.GetSIPDomainsAsync(m_sipDomainsWhere, 0, Int32.MaxValue);
                }
                else if (!m_sipAccountsLoaded)
                {
                    ShowActivityProgress_External(50);
                    LogActivityMessage_External(MessageLevelsEnum.Info, "Loading SIP Accounts...");
                    m_sipAccountsPanel.RefreshAsync();
                }
                else if (!m_sipBindingsLoaded)
                {
                    ShowActivityProgress_External(75);
                    LogActivityMessage_External(MessageLevelsEnum.Info, "Loading SIP Bindings...");
                    m_sipBindingsPanel.RefreshAsync();
                }
                else
                {
                    ShowActivityProgress_External(null);
                    m_initialLoadComplete = true;
                }
            }
            catch (Exception excp)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "Exception thrown loading the SIP Account Manager. " + excp.Message);
            }
        }

        public void Close()
        {
            m_persistor.GetSIPAccountsCountComplete -= GetSIPAccountsCountComplete;
            m_persistor.GetSIPAccountsComplete -= GetSIPAccountsComplete;
            m_persistor.GetSIPDomainsComplete -= GetSIPDomainsComplete;
            m_persistor.UpdateSIPAccountComplete -= UpdateSIPAccountComplete;
            m_persistor.DeleteSIPAccountComplete -= DeleteSIPAccountComplete;
            m_persistor.AddSIPAccountComplete -= AddSIPAccountComplete;
            m_persistor.GetRegistrarBindingsCountComplete -= GetRegistrarBindingsCountComplete;
        }

        public void SIPMonitorMachineEventHandler(SIPMonitorMachineEvent machineEvent)
        {
            // Update the bindings display.
            if (m_initialLoadComplete && !m_sipAccountsPanelRefreshInProgress)
            {
                m_sipBindingsPanel.RefreshAsync();
            }
        }

        private void GetSIPDomainsComplete(GetSIPDomainsCompletedEventArgs e)
        {
            try
            {
                ObservableCollection<SIPDomain> domains = e.Result;

                if (domains != null)
                {
                    LogActivityMessage_External(MessageLevelsEnum.Info, domains.Count + " successfully loaded.");

                    foreach (SIPDomain domain in domains)
                    {
                        if (m_ownerDomains.ContainsKey(m_owner))
                        {
                            List<string> ownerDomains = m_ownerDomains[m_owner];
                            if (!ownerDomains.Contains(domain.Domain))
                            {
                                ownerDomains.Add(domain.Domain);
                            }
                        }
                        else
                        {
                            m_ownerDomains.Add(m_owner, new List<string>() { domain.Domain });
                        }
                    }
                }
                else
                {
                    LogActivityMessage_External(MessageLevelsEnum.Warn, "No domains could be loaded.");
                }

                m_sipDomainsLoaded = true;
                Load();
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error retrieving the SIP Domains. " + excpMessage);
            }
        }

        #region SIP Account functions.

        private void GetSIPAccounts(int offset, int count)
        {
            if (!m_sipAccountsPanelRefreshInProgress)
            {
                m_sipAccountsPanelRefreshInProgress = true;
                m_sipAccountLoadInProgress = true;

                m_sipAccountsPanelOffset = offset;
                m_sipAccountsPanelCount = count;
                m_persistor.GetSIPAccountsCountAsync(m_sipAccountsWhere);
            }
            else
            {
                LogActivityMessage_External(MessageLevelsEnum.Warn, "A SIP accounts refresh is already in progress.");
            }
        }

        private void GetSIPAccountsCountComplete(GetSIPAccountsCountCompletedEventArgs e)
        {
            try
            {
                m_sipAccountsPanel.AssetListTotal = e.Result;
                m_persistor.GetSIPAccountsAsync(m_sipAccountsWhere, m_sipAccountsPanelOffset, m_sipAccountsPanelCount);
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error retrieving the number of SIP accounts. " + excpMessage);

                m_sipAccountLoadInProgress = false;
                m_sipAccountsPanelRefreshInProgress = false;
            }
        }

        private void GetSIPAccountsComplete(GetSIPAccountsCompletedEventArgs e)
        {
            m_sipAccountsLoaded = true;

            try
            {
                m_sipAccounts = e.Result;
                m_sipAccountsPanel.SetAssetListSource(m_sipAccounts);

                if (!m_initialLoadComplete)
                {
                    Load();
                }

                LogActivityMessage_External(MessageLevelsEnum.Info, "SIP Accounts successfully loaded " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + ".");
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error retrieving SIP accounts. " + excpMessage);
            }
            finally
            {
                m_sipAccountLoadInProgress = false;
                m_sipAccountsPanelRefreshInProgress = false;
            }
        }

        private void SIPAccountAdd(SIPAccount sipAccount)
        {
            m_persistor.AddSIPAccountAsync(sipAccount);
        }

        private void AddSIPAccountComplete(AddSIPAccountCompletedEventArgs e)
        {
            try
            {
                SIPAccount sipAccount = e.Result;

                if (m_addControl != null)
                {
                    m_addControl.WriteStatusMessage(MessageLevelsEnum.Info, "SIP Account was successfully created for " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + ".");
                }

                if (m_sipAccounts == null)
                {
                    m_sipAccountsPanel.RefreshAsync();
                }
                else
                {
                    m_sipAccounts.Add(sipAccount);
                    m_sipAccountsPanel.AssetAdded();
                }
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                if (m_addControl != null)
                {
                    m_addControl.WriteStatusMessage(MessageLevelsEnum.Error, excpMessage);
                }
                else
                {
                    LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error adding a SIP Account. " + excpMessage);
                }
            }
        }

        private void SIPAccountUpdate(SIPAccount sipAccount)
        {
            //LogActivityMessage_External(MessageLevelsEnum.Info, "Attempting to update " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + ".");
            m_persistor.UpdateSIPAccount(sipAccount);
        }

        private void UpdateSIPAccountComplete(UpdateSIPAccountCompletedEventArgs e)
        {
            try
            {
                // The SIP account returned by the web service call is the account after the update operation was carried out.
                SIPAccount sipAccount = e.Result;

                if (m_editControl != null)
                {
                    m_editControl.WriteStatusMessage(MessageLevelsEnum.Info, "Update completed successfully for " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + ".");
                }
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                if (m_editControl != null)
                {
                    m_editControl.WriteStatusMessage(MessageLevelsEnum.Error, excpMessage);
                }
                else
                {
                    LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error performing a SIP Account update." + excpMessage);
                }
            }
        }

        private void SIPAccountsAssetViewer_Add()
        {
            m_selectedSIPAccount = null;
            m_addControl = new SIPAccountDetailsControl(DetailsControlModesEnum.Add, null, m_owner, SIPAccountAdd, null, DetailsControlClosed, GetDialPlanNames_External, GetSIPDomains);
            m_sipAccountsPanel.SetDetailsElement(m_addControl);
        }

        private void SIPAccountsDataGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (m_initialLoadComplete && !m_sipAccountLoadInProgress && m_sipAccounts.Count > 0)
                {
                    DataGrid dataGrid = (DataGrid)sender;
                    if (dataGrid.CurrentColumn.Header as string != "Delete")
                    {
                        SIPAccount sipAccount = (SIPAccount)m_sipAccountsDataGrid.SelectedItem;

                        if (m_selectedSIPAccount == null || m_selectedSIPAccount != sipAccount)
                        {
                            m_selectedSIPAccount = sipAccount;
                            m_editControl = new SIPAccountDetailsControl(DetailsControlModesEnum.Edit, sipAccount, m_owner, null, SIPAccountUpdate, DetailsControlClosed, GetDialPlanNames_External, GetSIPDomains);
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
                LogActivityMessage_External(MessageLevelsEnum.Info, "Sending delete request for SIP Account " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + ".");
                m_persistor.DeleteSIPAccount(sipAccount);
            }
        }

        private void DeleteSIPAccountComplete(DeleteSIPAccountCompletedEventArgs e)
        {
            try
            {
                SIPAccount sipAccount = e.Result;

                LogActivityMessage_External(MessageLevelsEnum.Info, "Delete completed successfully for " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + ".");

                for (int index = 0; index < m_sipAccounts.Count; index++)
                {
                    if (m_sipAccounts[index].Id == sipAccount.Id)
                    {
                        m_sipAccounts.RemoveAt(index);
                        break;
                    }
                }

                m_sipAccountsPanel.AssetDeleted();
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error deleting the SIP Account. " + excpMessage);
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

        private List<string> GetSIPDomains(string owner)
        {
            if (m_ownerDomains.ContainsKey(owner))
            {
                return m_ownerDomains[owner];
            }
            else
            {
                return null;
            }
        }

        #endregion

        #region SIP Registrar Bindings Functions.

        private void GetSIPBindings(int offset, int count)
        {
            if (!m_sipBindingsPanelRefreshInProgress)
            {
                m_sipBindingsPanelRefreshInProgress = true;

                m_sipBindingsPanelOffset = offset;
                m_sipBindingsPanelCount = count;
                m_persistor.GetRegistrarBindingsCountAsync(m_sipBindingsWhere);
            }
            else
            {
                LogActivityMessage_External(MessageLevelsEnum.Warn, "A SIP bindings refresh is already in progress.");
            }
        }

        private void GetRegistrarBindingsCountComplete(GetSIPRegistrarBindingsCountCompletedEventArgs e)
        {
            try
            {
                m_sipBindingsPanel.AssetListTotal = e.Result;
                LogActivityMessage_External(MessageLevelsEnum.Info, e.Result + " SIP Bindings are registered.");
                m_persistor.GetRegistrarBindingsAsync(m_sipBindingsWhere, m_sipBindingsPanelOffset, m_sipBindingsPanelCount);
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error retrieving the number of SIP bindings. " + excpMessage);

                m_sipBindingsPanelRefreshInProgress = false;
            }
        }

        private void GetRegistrarBindingsComplete(GetSIPRegistrarBindingsCompletedEventArgs e)
        {
            m_sipBindingsLoaded = true;

            try
            {
                m_sipBindingsPanel.SetAssetListSource(e.Result);

                if (!m_initialLoadComplete)
                {
                    Load();
                }

                LogActivityMessage_External(MessageLevelsEnum.Info, "Bindings successfully loaded.");
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error retrieving SIP bindings. " + excpMessage);
            }
            finally
            {
                m_sipBindingsPanelRefreshInProgress = false;
            }
        }

        #endregion

        private void DetailsControlClosed()
        {
            m_selectedSIPAccount = null;
            m_sipAccountsPanel.CloseDetailsPane();
        }
	}
}