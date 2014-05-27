using System;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.DomainServices.Client;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Input;
using SIPSorcery.Entities;
using SIPSorcery.Entities.Services;
using SIPSorcery.UIControls;

namespace SIPSorcery
{
    public partial class DialPlanWizard : UserControl
    {
        private const int LOOKUP_TABLES_PAGE_SIZE = 5;
        private const double HELP_CANVAS_HEIGHT = 110;
        private const double DATA_GRID_HEIGHT = 231;

        private ActivityMessageDelegate LogActivityMessage_External;
        private DialPlanUpdateDelegate DialPlanAdd_External;
        private DialPlanUpdateDelegate DialPlanUpdate_External;
        private ControlClosedDelegate ControlClosed_External;

        private SIPEntitiesDomainContext m_riaContext;
        private SIPDialPlan m_dialPlan;
        private string m_owner;
        private bool m_speedDialsPanelRefreshInProgress;
        private bool m_enumsPanelRefreshInProgress;
        private bool m_cnamPanelRefreshInProgress;
        private bool m_providersPanelRefreshInProgress;
        private bool m_routesPanelRefreshInProgress;
        private bool m_optionsRefreshInProgress;
        private bool m_intialised;
        private bool m_gridReady;

        public DialPlanWizard()
        {
            InitializeComponent();
        }

        public DialPlanWizard(
            ActivityMessageDelegate logActivityMessage,
            SIPDialPlan dialPlan,
            string owner,
            DialPlanUpdateDelegate dialPlanAdd,
            DialPlanUpdateDelegate dialPlanUpdate,
            ControlClosedDelegate closed,
            SIPEntitiesDomainContext riaContext)
        {
            InitializeComponent();

            LogActivityMessage_External = logActivityMessage;
            m_dialPlan = dialPlan;
            m_owner = owner;
            DialPlanAdd_External = dialPlanAdd;
            DialPlanUpdate_External = dialPlanUpdate;
            ControlClosed_External = closed;
            m_riaContext = riaContext;

            m_riaContext.RejectChanges();

            m_speedDialsPanel.SetTitle("Speed Dials");
            m_speedDialsPanel.MenuEnableFilter(false);
            m_speedDialsPanel.MenuEnableHelp(true);
            m_speedDialsPanel.MenuEnableAdd(false);
            m_speedDialsPanel.GetAssetList = GetSpeedDials;
            m_speedDialsPanel.Help += () => { ToggleSpeedDialsHelp(true); };
            ToggleSpeedDialsHelp(false);

            m_enumsPanel.SetTitle("ENUMs");
            m_enumsPanel.MenuEnableFilter(false);
            m_enumsPanel.MenuEnableHelp(true);
            m_enumsPanel.MenuEnableAdd(false);
            m_enumsPanel.GetAssetList = GetENUMs;
            m_enumsPanel.Help += () => { ToggleENUMHelp(true); };
            ToggleENUMHelp(false);

            m_cnamPanel.SetTitle("CNAMs");
            m_cnamPanel.MenuEnableFilter(false);
            m_cnamPanel.MenuEnableHelp(true);
            m_cnamPanel.MenuEnableAdd(false);
            m_cnamPanel.GetAssetList = GetCNAMs;
            m_cnamPanel.Help += () => { ToggleCNAMHelp(true); };
            ToggleCNAMHelp(false);

            m_providersPanel.SetTitle("Dial Plan Providers");
            m_providersPanel.MenuEnableFilter(false);
            m_providersPanel.MenuEnableHelp(true);
            m_providersPanel.MenuEnableAdd(false);
            m_providersPanel.GetAssetList = GetDialPlanProviders;
            m_providersPanel.Help += () => { ToggleDialPlanProvidersHelp(true); };
            ToggleDialPlanProvidersHelp(false);

            m_routesPanel.SetTitle("Routes");
            m_routesPanel.MenuEnableFilter(false);
            m_routesPanel.MenuEnableHelp(true);
            m_routesPanel.MenuEnableAdd(false);
            m_routesPanel.GetAssetList = GetRoutes;
            m_routesPanel.Help += () => { ToggleRoutesHelp(true); };
            ToggleRoutesHelp(false);

            m_intialised = true;

            m_speedDialsPanel.RefreshAsync();
        }

        public void DisableSelectionChanges()
        {
            m_speedDialsDataForm.CancelEdit();
            m_enumsDataForm.CancelEdit();
            m_gridReady = false;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            ControlClosed_External();
        }

        private void TabControl_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (m_intialised)
            {
                TabItem selectedTab = e.AddedItems[0] as TabItem;

                if (selectedTab.Header.ToString() == "Speed Dials")
                {
                    m_speedDialsPanel.RefreshAsync();
                }
                else if (selectedTab.Header.ToString() == "CNAM")
                {
                    m_cnamPanel.RefreshAsync();
                }
                else if (selectedTab.Header.ToString() == "ENUM")
                {
                    m_enumsPanel.RefreshAsync();
                }
                else if (selectedTab.Header.ToString() == "Dial Plan Providers")
                {
                    m_providersPanel.RefreshAsync();
                }
                else if (selectedTab.Header.ToString() == "Routes")
                {
                    m_routesPanel.RefreshAsync();
                }
                else if (selectedTab.Header.ToString() == "Options")
                {
                    GetOptions();
                }
            }
        }

        #region ENUMs.

        private void GetENUMs(int offset, int count)
        {
            if (!m_enumsPanelRefreshInProgress)
            {
                m_enumsPanelRefreshInProgress = true;

                DisableSelectionChanges();
                m_riaContext.SIPDialplanLookups.Clear();

                var enumsQuery = m_riaContext.GetSIPDialplanLookupsQuery().Where(x => x.DialPlanID == m_dialPlan.ID && x.LookupType == (int)SIPDialPlanLookupTypes.ENUM).OrderBy(x => x.LookupKey).Skip(offset).Take(count);
                enumsQuery.IncludeTotalCount = true;
                m_riaContext.Load<SIPDialplanLookup>(enumsQuery, LoadBehavior.RefreshCurrent, ENUMsLoaded, null);
            }
        }

        private void ENUMsLoaded(LoadOperation lo)
        {
            if (lo.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "Error loading ENUMs. " + lo.Error.Message);
                lo.MarkErrorAsHandled();
            }
            else
            {
                m_enumsPanel.AssetListTotal = lo.TotalEntityCount;
                m_enumsPanel.SetAssetListSource(m_riaContext.SIPDialplanLookups);
                m_enumsDataForm.ItemsSource = m_riaContext.SIPDialplanLookups;
            }

            m_gridReady = true;
            m_enumsPanelRefreshInProgress = false;
        }

        private void DeleteENUM(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SIPDialplanLookup enumEntry = m_enumsDataGrid.SelectedItem as SIPDialplanLookup;
            MessageBoxResult confirmDelete = MessageBox.Show("Press Ok to delete ENUM " + enumEntry.LookupKey + ".", "Confirm Delete", MessageBoxButton.OKCancel);
            if (confirmDelete == MessageBoxResult.OK)
            {
                e.Cancel = true;
                m_riaContext.SIPDialplanLookups.Remove(enumEntry);
                m_riaContext.SubmitChanges(SubmitComplete, null);
            }
            else
            {
                e.Cancel = true;
            }
        }

        private void ENUMsDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (m_gridReady)
            {
                m_enumsDataForm.CurrentItem = (sender as DataGrid).SelectedItem;
            }
        }

        private void ToggleENUMHelp(bool displayHelp)
        {
            if (displayHelp)
            {
                m_enumHelpCanvas.Visibility = Visibility.Visible;
                m_enumsDataGrid.Height = DATA_GRID_HEIGHT - HELP_CANVAS_HEIGHT;
            }
            else
            {
                m_enumHelpCanvas.Visibility = Visibility.Collapsed;
                m_enumsDataGrid.Height = DATA_GRID_HEIGHT + HELP_CANVAS_HEIGHT;
            }
        }

        private void CloseEnumHelp_Clicked(object sender, System.Windows.RoutedEventArgs e)
        {
            ToggleENUMHelp(false);
        }

        #endregion

        #region Speed Dials.

        private void GetSpeedDials(int offset, int count)
        {
            if (!m_speedDialsPanelRefreshInProgress)
            {
                m_speedDialsPanelRefreshInProgress = true;

                DisableSelectionChanges();
                m_riaContext.SIPDialplanLookups.Clear();

               var speedDialsQuery = m_riaContext.GetSIPDialplanLookupsQuery().Where(x => x.DialPlanID == m_dialPlan.ID && x.LookupType == (int)SIPDialPlanLookupTypes.SpeedDial).OrderBy(x => x.LookupKey).Skip(offset).Take(count);
                speedDialsQuery.IncludeTotalCount = true;
                m_riaContext.Load<SIPDialplanLookup>(speedDialsQuery, LoadBehavior.RefreshCurrent, SpeedDialsLoaded, null);
            }
        }

        private void SpeedDialsLoaded(LoadOperation lo)
        {
            if (lo.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "Error loading Speed Dials. " + lo.Error.Message);
                lo.MarkErrorAsHandled();
            }
            else
            {
                m_speedDialsPanel.AssetListTotal = lo.TotalEntityCount;
                m_speedDialsPanel.SetAssetListSource(m_riaContext.SIPDialplanLookups);
                m_speedDialsDataForm.ItemsSource = m_riaContext.SIPDialplanLookups;
            }

            m_gridReady = true;
            m_speedDialsPanelRefreshInProgress = false;
        }

        private void DeleteSpeedDial(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SIPDialplanLookup speedDial = m_speedDialsDataForm.CurrentItem as SIPDialplanLookup;
            MessageBoxResult confirmDelete = MessageBox.Show("Press Ok to delete speed dial " + speedDial.LookupKey + ".", "Confirm Delete", MessageBoxButton.OKCancel);
            if (confirmDelete == MessageBoxResult.OK)
            {
                e.Cancel = true;
                m_riaContext.SIPDialplanLookups.Remove(speedDial);
                m_riaContext.SubmitChanges(SubmitComplete, null);
            }
            else
            {
                e.Cancel = true;
            }
        }

        private void SpeedDialsDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (m_gridReady)
            {
                m_speedDialsDataForm.CurrentItem = (sender as DataGrid).SelectedItem;
            }
        }

        private void ToggleSpeedDialsHelp(bool displayHelp)
        {
            if (displayHelp)
            {
                m_speedDialHelpCanvas.Visibility = Visibility.Visible;
                m_speedDialsDataGrid.Height = DATA_GRID_HEIGHT - HELP_CANVAS_HEIGHT;
            }
            else
            {
                m_speedDialHelpCanvas.Visibility = Visibility.Collapsed;
                m_speedDialsDataGrid.Height = DATA_GRID_HEIGHT + HELP_CANVAS_HEIGHT;
            }
        }

        private void CloseSpeedDialHelp_Clicked(object sender, System.Windows.RoutedEventArgs e)
        {
            ToggleSpeedDialsHelp(false);
        }

        #endregion

        #region CNAMs.

        private void GetCNAMs(int offset, int count)
        {
            if (!m_cnamPanelRefreshInProgress)
            {
                m_cnamPanelRefreshInProgress = true;

                DisableSelectionChanges();
                m_riaContext.SIPDialplanLookups.Clear();

                var cnamsQuery = m_riaContext.GetSIPDialplanLookupsQuery().Where(x => x.DialPlanID == m_dialPlan.ID && x.LookupType == (int)SIPDialPlanLookupTypes.CNAM).OrderBy(x => x.LookupKey).Skip(offset).Take(count);
                cnamsQuery.IncludeTotalCount = true;
                m_riaContext.Load<SIPDialplanLookup>(cnamsQuery, LoadBehavior.RefreshCurrent, CNAMsLoaded, null);
            }
        }

        private void CNAMsLoaded(LoadOperation lo)
        {
            if (lo.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "Error loading CNAMs. " + lo.Error.Message);
                lo.MarkErrorAsHandled();
            }
            else
            {
                m_cnamPanel.AssetListTotal = lo.TotalEntityCount;
                m_cnamPanel.SetAssetListSource(m_riaContext.SIPDialplanLookups);
                m_cnamDataForm.ItemsSource = m_riaContext.SIPDialplanLookups;
            }

            m_gridReady = true;
            m_cnamPanelRefreshInProgress = false;
        }

        private void DeleteCNAM(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SIPDialplanLookup cnamEntry = m_cnamDataGrid.SelectedItem as SIPDialplanLookup;
            MessageBoxResult confirmDelete = MessageBox.Show("Press Ok to delete CNAM " + cnamEntry.LookupKey + ".", "Confirm Delete", MessageBoxButton.OKCancel);
            if (confirmDelete == MessageBoxResult.OK)
            {
                e.Cancel = true;
                m_riaContext.SIPDialplanLookups.Remove(cnamEntry);
                m_riaContext.SubmitChanges(SubmitComplete, null);
            }
            else
            {
                e.Cancel = true;
            }
        }

        private void CNAMsDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (m_gridReady)
            {
                m_cnamDataForm.CurrentItem = (sender as DataGrid).SelectedItem;
            }
        }

        private void ToggleCNAMHelp(bool displayHelp)
        {
            if (displayHelp)
            {
                m_cnamHelpCanvas.Visibility = Visibility.Visible;
                m_cnamDataGrid.Height = DATA_GRID_HEIGHT - HELP_CANVAS_HEIGHT;
            }
            else
            {
                m_cnamHelpCanvas.Visibility = Visibility.Collapsed;
                m_cnamDataGrid.Height = DATA_GRID_HEIGHT + HELP_CANVAS_HEIGHT;
            }
        }

        private void CloseCNAMHelp_Clicked(object sender, System.Windows.RoutedEventArgs e)
        {
            ToggleCNAMHelp(false);
        }

        #endregion

        #region Dial Plan Providers.

        private void GetDialPlanProviders(int offset, int count)
        {
            if (!m_providersPanelRefreshInProgress)
            {
                m_providersPanelRefreshInProgress = true;

                m_riaContext.SIPDialplanProviders.Clear();

                var providersQuery = m_riaContext.GetSIPDialplanProvidersQuery().Where(x => x.DialPlanID == m_dialPlan.ID).OrderBy(x => x.ProviderName).Skip(offset).Take(count);
                providersQuery.IncludeTotalCount = true;
                m_riaContext.Load<SIPDialplanProvider>(providersQuery, LoadBehavior.RefreshCurrent, DialPlanProvidersLoaded, null);
            }
        }

        private void DialPlanProvidersLoaded(LoadOperation lo)
        {
            if (lo.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "Error loading Dial Plan Providers. " + lo.Error.Message);
                lo.MarkErrorAsHandled();
            }
            else
            {
                m_providersPanel.AssetListTotal = lo.TotalEntityCount;
                m_providersPanel.SetAssetListSource(m_riaContext.SIPDialplanProviders);
                m_providersDataForm.ItemsSource = m_riaContext.SIPDialplanProviders;
            }

            m_providersPanelRefreshInProgress = false;
        }

        private void DeleteDialPlanProvider(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SIPDialplanProvider providerEntry = m_providersDataGrid.SelectedItem as SIPDialplanProvider;
            MessageBoxResult confirmDelete = MessageBox.Show("Press Ok to delete Dial Plan Provider " + providerEntry.ProviderName + ".", "Confirm Delete", MessageBoxButton.OKCancel);
            if (confirmDelete == MessageBoxResult.OK)
            {
                e.Cancel = true;
                m_riaContext.SIPDialplanProviders.Remove(providerEntry);
                m_riaContext.SubmitChanges(SubmitComplete, null);
            }
            else
            {
                e.Cancel = true;
            }
        }

        private void EditEndingProvider(object sender, System.Windows.Controls.DataFormEditEndingEventArgs e)
        {
            if (m_gridReady)
            {
                DataForm dataForm = sender as DataForm;

                if (dataForm.Mode == DataFormMode.AddNew)
                {
                    SIPDialplanProvider provider = dataForm.CurrentItem as SIPDialplanProvider;
                    provider.ID = Guid.NewGuid().ToString();
                    provider.DialPlanID = m_dialPlan.ID;
                    provider.Owner = m_owner;
                }
            }
        }

        private void ProvidersDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (m_gridReady)
            {
                m_providersDataForm.CurrentItem = (sender as DataGrid).SelectedItem;
            }
        }

        private void ToggleDialPlanProvidersHelp(bool displayHelp)
        {
            if (displayHelp)
            {
                m_dialplanProvidersHelpCanvas.Visibility = Visibility.Visible;
                m_providersDataGrid.Height = DATA_GRID_HEIGHT - HELP_CANVAS_HEIGHT;
            }
            else
            {
                m_dialplanProvidersHelpCanvas.Visibility = Visibility.Collapsed;
                m_providersDataGrid.Height = DATA_GRID_HEIGHT + HELP_CANVAS_HEIGHT;
            }
        }

        private void CloseDialPlanProvidersHelp_Clicked(object sender, System.Windows.RoutedEventArgs e)
        {
            ToggleDialPlanProvidersHelp(false);
        }

        #endregion

        #region Routes.

        private void GetRoutes(int offset, int count)
        {
            if (!m_routesPanelRefreshInProgress)
            {
                m_routesPanelRefreshInProgress = true;

                m_riaContext.SIPDialplanRoutes.Clear();

                var routesQuery = m_riaContext.GetSIPDialplanRoutesQuery().Where(x => x.DialPlanID == m_dialPlan.ID).OrderBy(x => x.RouteName).Skip(offset).Take(count);
                routesQuery.IncludeTotalCount = true;
                m_riaContext.Load<SIPDialplanRoute>(routesQuery, LoadBehavior.RefreshCurrent, RoutesLoaded, null);
            }
        }

        private void RoutesLoaded(LoadOperation lo)
        {
            if (lo.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "Error loading Routes. " + lo.Error.Message);
                lo.MarkErrorAsHandled();
            }
            else
            {
                m_routesPanel.AssetListTotal = lo.TotalEntityCount;
                m_routesPanel.SetAssetListSource(m_riaContext.SIPDialplanRoutes);
                m_routesDataForm.ItemsSource = m_riaContext.SIPDialplanRoutes;
            }

            m_routesPanelRefreshInProgress = false;
        }

        private void DeleteRoute(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SIPDialplanRoute routeEntry = m_routesDataGrid.SelectedItem as SIPDialplanRoute;
            MessageBoxResult confirmDelete = MessageBox.Show("Press Ok to delete Route " + routeEntry.RouteName + ".", "Confirm Delete", MessageBoxButton.OKCancel);
            if (confirmDelete == MessageBoxResult.OK)
            {
                e.Cancel = true;
                m_riaContext.SIPDialplanRoutes.Remove(routeEntry);
                m_riaContext.SubmitChanges(SubmitComplete, null);
            }
            else
            {
                e.Cancel = true;
            }
        }

        private void EditEndingRoute(object sender, System.Windows.Controls.DataFormEditEndingEventArgs e)
        {
            if (m_gridReady)
            {
                DataForm dataForm = sender as DataForm;

                if (dataForm.Mode == DataFormMode.AddNew)
                {
                    SIPDialplanRoute route = dataForm.CurrentItem as SIPDialplanRoute;
                    route.ID = Guid.NewGuid().ToString();
                    route.DialPlanID = m_dialPlan.ID;
                    route.Owner = m_owner;
                }
            }
        }

        private void RoutesDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (m_gridReady)
            {
                m_routesDataForm.CurrentItem = (sender as DataGrid).SelectedItem;
            }
        }

        private void ToggleRoutesHelp(bool displayHelp)
        {
            if (displayHelp)
            {
                m_routesHelpCanvas.Visibility = Visibility.Visible;
                m_routesDataGrid.Height = DATA_GRID_HEIGHT - HELP_CANVAS_HEIGHT;
            }
            else
            {
                m_routesHelpCanvas.Visibility = Visibility.Collapsed;
                m_routesDataGrid.Height = DATA_GRID_HEIGHT + HELP_CANVAS_HEIGHT;
            }
        }

        private void CloseRoutesHelp_Clicked(object sender, System.Windows.RoutedEventArgs e)
        {
            ToggleRoutesHelp(false);
        }

        #endregion

        #region Options.

        private void GetOptions()
        {
            if (!m_optionsRefreshInProgress)
            {
                m_optionsRefreshInProgress = true;

                m_optionsDataForm.ItemsSource = null;
                m_riaContext.SIPDialplanOptions.Clear();

                m_riaContext.Load<SIPDialplanOption>(m_riaContext.GetSIPDialplanOptionsQuery().Where(x => x.DialPlanID == m_dialPlan.ID), LoadBehavior.RefreshCurrent, OptionsLoaded, null);
            }
        }

        private void OptionsLoaded(LoadOperation lo)
        {
            if (lo.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "Error loading dial plan options. " + lo.Error.Message);
                lo.MarkErrorAsHandled();
            }
            else
            {
                m_optionsDataForm.ItemsSource = m_riaContext.SIPDialplanOptions;
            }

            m_optionsRefreshInProgress = false;
        }

        #endregion

        private void EditEndingLookup(object sender, System.Windows.Controls.DataFormEditEndingEventArgs e)
        {
            if (m_gridReady)
            {
                DataForm dataForm = sender as DataForm;

                if (dataForm.CurrentItem != null)
                {
                    int lookupType = (int)SIPDialPlanLookupTypes.SpeedDial;
                    if (dataForm == m_enumsDataForm)
                    {
                        lookupType = (int)SIPDialPlanLookupTypes.ENUM;
                    }
                    else if (dataForm == m_cnamDataForm)
                    {
                        lookupType = (int)SIPDialPlanLookupTypes.CNAM;
                    }

                    if (dataForm.Mode == DataFormMode.AddNew)
                    {
                        SIPDialplanLookup lookup = dataForm.CurrentItem as SIPDialplanLookup;
                        lookup.ID = Guid.NewGuid().ToString();
                        lookup.DialPlanID = m_dialPlan.ID;
                        lookup.Owner = m_owner;
                        lookup.LookupType = lookupType;
                    }
                }
            }
        }

        private void EditEnded(object sender, System.Windows.Controls.DataFormEditEndedEventArgs e)
        {
            if (e.EditAction == DataFormEditAction.Commit)
            {
                m_riaContext.SubmitChanges(SubmitComplete, null);
            }
        }

        private void SubmitComplete(SubmitOperation so)
        {
            if (so.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, so.Error.Message);
                so.MarkErrorAsHandled();
            }
        }
    }
}