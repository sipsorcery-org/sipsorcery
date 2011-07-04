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
    public partial class SimpleDialPlanWizard : UserControl
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

        public SimpleDialPlanWizard()
        {
            InitializeComponent();
        }

        public SimpleDialPlanWizard(
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

            m_routesPanel.SetTitle("Routes");
            m_routesPanel.MenuEnableFilter(false);
            m_routesPanel.MenuEnableHelp(true);
            m_routesPanel.MenuEnableAdd(false);
            m_routesPanel.GetAssetList = GetRoutes;
            m_routesPanel.Help += () => { ToggleRoutesHelp(true); };
            ToggleRoutesHelp(false);

            m_intialised = true;
       }

        public void DisableSelectionChanges()
        {
            //m_speedDialsDataForm.CancelEdit();
            //m_enumsDataForm.CancelEdit();
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
                    //m_speedDialsPanel.RefreshAsync();
                }
                else if (selectedTab.Header.ToString() == "CNAM")
                {
                   // m_cnamPanel.RefreshAsync();
                }
                else if (selectedTab.Header.ToString() == "ENUM")
                {
                    //m_enumsPanel.RefreshAsync();
                }
                else if (selectedTab.Header.ToString() == "Dial Plan Providers")
                {
                   // m_providersPanel.RefreshAsync();
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

        #region Routes.

        private void GetRoutes(int offset, int count)
        {
            if (!m_routesPanelRefreshInProgress)
            {
                m_routesPanelRefreshInProgress = true;

                m_riaContext.SIPDialplanRoutes.Clear();

                var routesQuery = m_riaContext.GetSIPDialplanRoutesQuery().Where(x => x.DialPlanID == m_dialPlan.ID).OrderBy(x => x.RouteName).Skip(offset).Take(count);
                routesQuery.IncludeTotalCount = true;
                m_riaContext.Load(routesQuery, LoadBehavior.RefreshCurrent, RoutesLoaded, null);
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

                m_riaContext.Load(m_riaContext.GetSIPDialplanOptionsQuery().Where(x => x.DialPlanID == m_dialPlan.ID), LoadBehavior.RefreshCurrent, OptionsLoaded, null);
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
                    //if (dataForm == m_enumsDataForm)
                    //{
                    //    lookupType = (int)SIPDialPlanLookupTypes.ENUM;
                    //}
                    //else if (dataForm == m_cnamDataForm)
                    //{
                    //    lookupType = (int)SIPDialPlanLookupTypes.CNAM;
                    //}

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