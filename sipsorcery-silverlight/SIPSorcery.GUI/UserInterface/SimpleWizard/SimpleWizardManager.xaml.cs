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
        //private const int ASSETVIEW_HEIGHT_WITH_CRM_OPTION = 254;   // The height of teh asset view panel when the CRM option is being displayed.
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
        private bool m_intialised;
        private bool m_gridReady;
        private DataGrid m_currentGrid;     // The data grid for the active tab panel.
        private double m_originalGridHeight;   // The original height of the data grid before the CRM options were displayed.

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

            // Set up the outgoing rules grid.
            m_outgoingRulesUpdateControl.SetStatusMessage(SimpleWizardOutRuleControl.ADD_TEXT, false);
            m_outgoingRulesUpdateControl.Add += SaveRule;
            m_outgoingRulesUpdateControl.Update += SaveRule;
            m_outgoingRulesPanel.SetTitle("Outgoing Call Rules");
            m_outgoingRulesPanel.MenuEnableFilter(false);
            m_outgoingRulesPanel.MenuEnableHelp(false);
            m_outgoingRulesPanel.MenuEnableAdd(true);
            m_outgoingRulesPanel.GetAssetList = GetOutgoingRules;
            m_outgoingRulesPanel.Add += () => { m_outgoingRulesUpdateControl.SetRuleToUpdate(null); };

            // Set up the incoming rules grid.
            m_incomingRulesUpdateControl.SetStatusMessage(SimpleWizardInRuleControl.ADD_TEXT, false);
            m_incomingRulesUpdateControl.Add += SaveRule;
            m_incomingRulesUpdateControl.Update += SaveRule;
            m_incomingRulesUpdateControl.SetToSIPAccounts(m_riaContext.SIPAccounts);
            m_crmSetUpControl.Closed += CloseCRMControl;
            m_crmSetUpControl.Add += SaveCRMAccount;
            m_crmSetUpControl.Update += SaveCRMAccount;
            m_crmSetUpControl.Delete += DeleteCRMAccount;
            m_incomingRulesPanel.SetTitle("Incoming Call Rules");
            m_incomingRulesPanel.MenuEnableFilter(false);
            m_incomingRulesPanel.MenuEnableHelp(false);
            m_incomingRulesPanel.MenuEnableAdd(true);
            m_incomingRulesPanel.GetAssetList = GetIncomingRules;
            m_incomingRulesPanel.Add += () => { m_incomingRulesUpdateControl.SetRuleToUpdate(null); };

            m_intialised = true;
            m_currentGrid = m_outgoingRulesDataGrid;

            m_outgoingRulesPanel.RefreshAsync();
        }

        public void DisableSelectionChanges()
        {
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
                    m_currentGrid = m_outgoingRulesDataGrid;
                    m_outgoingRulesPanel.RefreshAsync();
                }
                else if (selectedTab.Header.ToString() == "Incoming Call Rules")
                {
                    m_currentGrid = m_incomingRulesDataGrid;
                    m_incomingRulesPanel.RefreshAsync();
                }
            }
        }

        #region Dial Plan Rules.

        private void GetOutgoingRules(int offset, int count)
        {
            GetRules(offset, count, m_outgoingRulesPanel, SIPCallDirection.Out);
        }

        private void GetIncomingRules(int offset, int count)
        {
            GetRules(offset, count, m_incomingRulesPanel, SIPCallDirection.In);
        }

        private void GetRules(int offset, int count, AssetViewPanel panel, SIPCallDirection ruleDirection)
        {
            if (!m_routesPanelRefreshInProgress)
            {
                m_routesPanelRefreshInProgress = true;

                m_riaContext.SimpleWizardDialPlanRules.Clear();

                var routesQuery = m_riaContext.GetSimpleWizardDialPlanRulesQuery().Where(x => x.DialPlanID == m_dialPlan.ID && x.Direction == ruleDirection.ToString()).OrderBy(x => x.Priority).Skip(offset).Take(count);
                routesQuery.IncludeTotalCount = true;
                m_riaContext.Load(routesQuery, LoadBehavior.RefreshCurrent, RulesLoaded, panel);
            }
        }

        private void RulesLoaded(LoadOperation lo)
        {
            AssetViewPanel panel = lo.UserState as AssetViewPanel;

            if (lo.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "Error loading rules for " + m_dialPlan.DialPlanName + " dial plan. " + lo.Error.Message);
                lo.MarkErrorAsHandled();
            }
            else
            {
                panel.AssetListTotal = lo.TotalEntityCount;
                panel.SetAssetListSource(m_riaContext.SimpleWizardDialPlanRules);
            }

            m_gridReady = true;
            m_routesPanelRefreshInProgress = false;
        }

        private void RulesDataGridClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DataGrid dataGrid = (DataGrid)sender;
            if (dataGrid.CurrentColumn != null && dataGrid.CurrentColumn.Header as string != "Delete")
            {
                SimpleWizardDialPlanRule rule = dataGrid.SelectedItem as SimpleWizardDialPlanRule;
                if (rule != null)
                {
                    if (dataGrid == m_outgoingRulesDataGrid)
                    {
                        m_outgoingRulesUpdateControl.SetRuleToUpdate(rule);
                    }
                    else if (dataGrid == m_incomingRulesDataGrid)
                    {
                        m_incomingRulesUpdateControl.SetRuleToUpdate(rule);
                    }
                }
            }
        }

        private void SaveRule(SimpleWizardDialPlanRule rule)
        {
            if (rule.ID == Guid.Empty.ToString())
            {
                rule.ID = Guid.NewGuid().ToString();
                rule.Owner = m_owner;
                rule.DialPlanID = m_dialPlan.ID;

                m_riaContext.SimpleWizardDialPlanRules.Add(rule);
            }

            if (rule.Direction == SIPCallDirection.Out.ToString())
            {
                m_outgoingRulesUpdateControl.SetStatusMessage("Saving...", true);
            }
            else
            {
                m_incomingRulesUpdateControl.SetStatusMessage("Saving...", true);
            }

            m_riaContext.SubmitChanges(SaveRuleComplete, rule);
        }

        public void SaveRuleComplete(SubmitOperation so)
        {
            var rule = (SimpleWizardDialPlanRule)so.UserState;

            if (so.HasError)
            {
                if (rule.Direction == SIPCallDirection.Out.ToString())
                {
                    m_outgoingRulesUpdateControl.SetErrorMessage(so.Error.Message);
                }
                else
                {
                    m_incomingRulesUpdateControl.SetErrorMessage(so.Error.Message);
                }
                so.MarkErrorAsHandled();
            }
            else
            {
                var updatedRule = m_riaContext.SimpleWizardDialPlanRules.SingleOrDefault(x => x.ID == rule.ID);

                if (updatedRule != null)
                {
                    if (rule.Direction == SIPCallDirection.Out.ToString())
                    {
                        m_outgoingRulesUpdateControl.SetStatusMessage(SimpleWizardOutRuleControl.UPDATE_TEXT, false);
                        m_outgoingRulesUpdateControl.SetRuleToUpdate(updatedRule);
                    }
                    else
                    {
                        m_incomingRulesUpdateControl.SetStatusMessage(SimpleWizardInRuleControl.UPDATE_TEXT, false);
                        m_incomingRulesUpdateControl.SetRuleToUpdate(updatedRule);
                    }
                }
                else
                {
                    // If the rule was deleted during the middle of an update.
                    if (rule.Direction == SIPCallDirection.Out.ToString())
                    {
                        m_outgoingRulesUpdateControl.SetStatusMessage(SimpleWizardOutRuleControl.ADD_TEXT, false);
                        m_outgoingRulesUpdateControl.SetRuleToUpdate(null);
                    }
                    else
                    {
                        m_incomingRulesUpdateControl.SetStatusMessage(SimpleWizardInRuleControl.ADD_TEXT, false);
                        m_incomingRulesUpdateControl.SetRuleToUpdate(null);
                    }
                }
            }
        }

        private void DeleteSimpleWizardRule(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SimpleWizardDialPlanRule rule = m_currentGrid.SelectedItem as SimpleWizardDialPlanRule;
            LogActivityMessage_External(MessageLevelsEnum.Info, "Deleting simple wizard rule for " + rule.Description + ".");
            m_riaContext.SimpleWizardDialPlanRules.Remove(rule);
            m_riaContext.SubmitChanges(DeleteComplete, null);
        }

        private void DeleteComplete(SubmitOperation so)
        {
            if (so.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, so.Error.Message);
                so.MarkErrorAsHandled();
            }
        }

        #endregion

        #region CRM Account Set Up.

        private void SetUpCRMClicked()
        {
            if (m_crmSetUpControl.Visibility == System.Windows.Visibility.Collapsed)
            {
                if (m_originalGridHeight == 0)
                {
                    m_originalGridHeight = m_incomingRulesDataGrid.Height;
                }

                m_crmSetUpControl.Visibility = Visibility.Visible;
                m_incomingRulesDataGrid.Height = m_incomingRulesDataGrid.Height - m_crmSetUpControl.Height;

                m_riaContext.CRMAccounts.Clear();

                var crmAccountQuery = m_riaContext.GetCRMAccountsQuery().OrderBy(x => x.ID).Take(1);
                m_riaContext.Load(crmAccountQuery, LoadBehavior.RefreshCurrent, CRMAccountLoaded, null);
            }
        }

        private void CloseCRMControl()
        {
            if (m_crmSetUpControl.Visibility == System.Windows.Visibility.Visible)
            {
                m_incomingRulesDataGrid.Height = m_originalGridHeight;
                m_crmSetUpControl.Visibility = System.Windows.Visibility.Collapsed;
            }
        }

        private void CRMAccountLoaded(LoadOperation lo)
        {
            if (lo.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "Error loading CRM Account for " + m_dialPlan.DialPlanName + " dial plan. " + lo.Error.Message);
                lo.MarkErrorAsHandled();
            }
            else
            {
                m_crmSetUpControl.SetCRMAccount(m_riaContext.CRMAccounts.FirstOrDefault());
            }
        }

        private void SaveCRMAccount(CRMAccount crmAccount)
        {
            if (crmAccount.ID == Guid.Empty.ToString())
            {
                crmAccount.ID = Guid.NewGuid().ToString();
                crmAccount.Owner = m_owner;

                m_riaContext.CRMAccounts.Add(crmAccount);
            }

            m_riaContext.SubmitChanges(SaveCRMAccountComplete, crmAccount);
        }

        public void SaveCRMAccountComplete(SubmitOperation so)
        {
            if (so.HasError)
            {
                m_crmSetUpControl.SetErrorMessage(so.Error.Message);
                so.MarkErrorAsHandled();
            }
            else
            {
                m_crmSetUpControl.SetStatusMessage(SimpleWizardInRuleControl.UPDATE_TEXT, false);
                m_crmSetUpControl.SetCRMAccount(m_riaContext.CRMAccounts.FirstOrDefault());
            }
        }

        private void DeleteCRMAccount(CRMAccount crmAccount)
        {
            LogActivityMessage_External(MessageLevelsEnum.Info, "Deleting CRM Account for " + m_dialPlan.DialPlanName + ".");
            m_riaContext.CRMAccounts.Remove(crmAccount);
            m_riaContext.SubmitChanges(DeleteCRMAccountComplete, null);
        }

        public void DeleteCRMAccountComplete(SubmitOperation so)
        {
            if (so.HasError)
            {
                m_crmSetUpControl.SetErrorMessage(so.Error.Message);
                so.MarkErrorAsHandled();
            }
            else
            {
                m_crmSetUpControl.SetStatusMessage(SimpleWizardInRuleControl.ADD_TEXT, false);
                m_crmSetUpControl.SetCRMAccount(null);
            }
        }

        #endregion

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
    }
}