using System;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.DomainServices.Client;
using System.Windows;
using System.Windows.Browser;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Input;
using SIPSorcery.Entities;
using SIPSorcery.Entities.Services;
using SIPSorcery.SIP;
using SIPSorcery.UIControls;

namespace SIPSorcery
{
    public partial class SimpleWizardManager : UserControl
    {
        private const string DEFAULT_HELP_URL = "http://localhost/sipsorcery-main/Help/SimpleWizardContents";
        private const string DEFAULT_HELP_OPTIONS = "width=700,height=500,scrollbars=1";
        private const int HELP_POPUP_WIDTH = 600;
        private const int HELP_POPUP_HEIGHT = 500;
        //private const int LOOKUP_TABLES_PAGE_SIZE = 5;
        //private const double UPDATE_CONTROL_CANVAS_HEIGHT = 75;
        //private const double DATA_GRID_HEIGHT = 471;
        //private const double DATA_GRID_TOP_MARGIN = 69;

        private ActivityMessageDelegate LogActivityMessage_External;
        private DialPlanUpdateDelegate DialPlanAdd_External;
        private DialPlanUpdateDelegate DialPlanUpdate_External;
        private ControlClosedDelegate ControlClosed_External;

        private SIPEntitiesDomainContext m_riaContext;
        private SIPDialPlan m_dialPlan;
        private string m_owner;
        private bool m_routesPanelRefreshInProgress;
        private bool m_optionsRefreshInProgress;
        private bool m_intialised;
        private bool m_gridReady;

        public SimpleWizardManager()
        {
            InitializeComponent();
        }

        public SimpleWizardManager(
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

            m_outgoingRulesUpdateControl.SetStatusMessage(SimpleWizardUpdateControl.ADD_TEXT, false);
            m_outgoingRulesUpdateControl.Add += SaveOutgoingRule;
            m_outgoingRulesUpdateControl.Update += SaveOutgoingRule;

            m_outgoingRulesPanel.SetTitle("Outgoing Call Rules");
            m_outgoingRulesPanel.MenuEnableFilter(false);
            m_outgoingRulesPanel.MenuEnableHelp(false);
            m_outgoingRulesPanel.MenuEnableAdd(true);
            m_outgoingRulesPanel.GetAssetList = GetOutgoingRules;
            m_outgoingRulesPanel.Add += () => { m_outgoingRulesUpdateControl.SetRuleToUpdate(null); };

            m_intialised = true;

            m_outgoingRulesPanel.RefreshAsync();
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

                if (selectedTab.Header.ToString() == "Outgoing Call Rules")
                {
                    m_outgoingRulesPanel.RefreshAsync();
                }
            }
        }

        #region Outgoing Rules.

        private void GetOutgoingRules(int offset, int count)
        {
            if (!m_routesPanelRefreshInProgress)
            {
                m_routesPanelRefreshInProgress = true;

                m_riaContext.SimpleWizardDialPlanRules.Clear();

                var routesQuery = m_riaContext.GetSimpleWizardDialPlanRulesQuery().Where(x => x.DialPlanID == m_dialPlan.ID && x.Direction == SIPCallDirection.Out.ToString()).OrderBy(x => x.Priority).Skip(offset).Take(count);
                routesQuery.IncludeTotalCount = true;
                m_riaContext.Load(routesQuery, LoadBehavior.RefreshCurrent, OutgoingRulesLoaded, null);
            }
        }

        private void OutgoingRulesLoaded(LoadOperation lo)
        {
            if (lo.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "Error loading Outgoing Rules. " + lo.Error.Message);
                lo.MarkErrorAsHandled();
            }
            else
            {
                m_outgoingRulesPanel.AssetListTotal = lo.TotalEntityCount;
                m_outgoingRulesPanel.SetAssetListSource(m_riaContext.SimpleWizardDialPlanRules);
            }

            m_gridReady = true;
            m_routesPanelRefreshInProgress = false;

            //m_outgoingRulesDataForm.CurrentItem = m_outgoingRulesDataGrid.SelectedItem;
        }

        private void DeleteRule(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SIPDialplanRoute routeEntry = m_outgoingRulesDataGrid.SelectedItem as SIPDialplanRoute;
            MessageBoxResult confirmDelete = MessageBox.Show("Press Ok to delete Route " + routeEntry.RouteName + ".", "Confirm Delete", MessageBoxButton.OKCancel);
            if (confirmDelete == MessageBoxResult.OK)
            {
                e.Cancel = true;
                m_riaContext.SIPDialplanRoutes.Remove(routeEntry);
                m_riaContext.SubmitChanges(DeleteComplete, null);
            }
            else
            {
                e.Cancel = true;
            }
        }

        private void DeleteComplete(SubmitOperation so)
        {
            if (so.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, so.Error.Message);
                so.MarkErrorAsHandled();
            }
        }

        private void EditEndingRule(object sender, System.Windows.Controls.DataFormEditEndingEventArgs e)
        {
            if (m_gridReady)
            {
                DataForm dataForm = sender as DataForm;

                if (dataForm.Mode == DataFormMode.AddNew)
                {
                    SimpleWizardDialPlanRule rule = dataForm.CurrentItem as SimpleWizardDialPlanRule;
                    rule.ID = Guid.NewGuid().ToString();
                    rule.DialPlanID = m_dialPlan.ID;
                    rule.Owner = m_owner;
                    rule.Direction = SIPCallDirection.Out.ToString();
                    rule.Priority = 0;
                }
            }
        }

        private void OutgoingRulesDataGridClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DataGrid dataGrid = (DataGrid)sender;
            if (dataGrid.CurrentColumn != null && dataGrid.CurrentColumn.Header as string != "Delete")
            {
                SimpleWizardDialPlanRule rule = m_outgoingRulesDataGrid.SelectedItem as SimpleWizardDialPlanRule;
                if (rule != null)
                {
                    m_outgoingRulesUpdateControl.SetRuleToUpdate(rule);
                }
            }
        }

        private void SaveOutgoingRule(SimpleWizardDialPlanRule rule)
        {
            if (rule.ID == Guid.Empty.ToString())
            {
                rule.ID = Guid.NewGuid().ToString();
                rule.Owner = m_owner;
                rule.DialPlanID = m_dialPlan.ID;
                rule.Direction = SIPCallDirection.Out.ToString();

                m_riaContext.SimpleWizardDialPlanRules.Add(rule);
            }

            m_outgoingRulesUpdateControl.SetStatusMessage("Saving...", true);
            m_riaContext.SubmitChanges(SaveRuleComplete, rule);
        }

        public void SaveRuleComplete(SubmitOperation so)
        {
            if (so.HasError)
            {
                m_outgoingRulesUpdateControl.SetErrorMessage(so.Error.Message);
                so.MarkErrorAsHandled();
            }
            else
            {
                var updatedRule = m_riaContext.SimpleWizardDialPlanRules.SingleOrDefault(x => x.ID == ((SimpleWizardDialPlanRule)so.UserState).ID);
                if (updatedRule != null)
                {
                    m_outgoingRulesUpdateControl.SetStatusMessage(SimpleWizardUpdateControl.UPDATE_TEXT, false);
                    m_outgoingRulesUpdateControl.SetRuleToUpdate(updatedRule);
                }
                else
                {
                    // If the rule was deleted during the middle of an update.
                    m_outgoingRulesUpdateControl.SetStatusMessage(SimpleWizardUpdateControl.ADD_TEXT, false);
                    m_outgoingRulesUpdateControl.SetRuleToUpdate(null);
                }
            }
        }

        private void DeleteSimpleWizardRule(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SimpleWizardDialPlanRule rule = m_outgoingRulesDataGrid.SelectedItem as SimpleWizardDialPlanRule;
            LogActivityMessage_External(MessageLevelsEnum.Info, "Deleting simple wizard rule for " + rule.Description + ".");
            m_riaContext.SimpleWizardDialPlanRules.Remove(rule);
            m_riaContext.SubmitChanges();
        }

        private void HelpLink_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                Uri helpURI = new Uri(DEFAULT_HELP_URL);
                HtmlPage.Window.Navigate(new Uri(DEFAULT_HELP_URL), "SIPSorceryHelp", DEFAULT_HELP_OPTIONS);
            }
            catch
            {
                LogActivityMessage_External(MessageLevelsEnum.Warn, "Unable to display help, you may have popup windows disabled. Alternatively navigate to " + DEFAULT_HELP_URL + ".");
            }
        }

        #endregion
    }
}