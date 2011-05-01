using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.DomainServices.Client;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SIPSorcery.Persistence;
using SIPSorcery.Entities;
using SIPSorcery.UIControls;
using SIPSorcery.Entities.Services;

namespace SIPSorcery
{
    public partial class DialPlanManager : UserControl
    {
        private ActivityMessageDelegate LogActivityMessage_External;
        private SIPEntitiesDomainContext m_riaContext;
        private DialPlanAddControl m_addControl;
        private DialPlanUpdateControl m_editControl;
        private DialPlanWizard m_wizardEditControl;
        private SIPDialPlan m_selectedDialPlan;
        private bool m_dialPlansPanelRefreshInProgress;

        private string m_owner;

        public bool Initialised { get; private set; }

        public DialPlanManager()
        {
            InitializeComponent();
        }

        public DialPlanManager(
            ActivityMessageDelegate logActivityMessage,
            string owner,
            SIPEntitiesDomainContext riaContext)
        {
            InitializeComponent();

            LogActivityMessage_External = logActivityMessage;
            m_riaContext = riaContext;
            m_owner = owner;

            m_dialPlansPanel.SetTitle("Dial Plans");
            m_dialPlansPanel.MenuEnableFilter(false);
            m_dialPlansPanel.MenuEnableHelp(false);
            m_dialPlansPanel.Add += DialPlansPanel_Add;
            m_dialPlansPanel.GetAssetList = GetDialPlans;
        }

        public void Initialise()
        {
            Initialised = true;
            //LogActivityMessage_External(MessageLevelsEnum.Info, "Loading Dial Plans...");
            m_dialPlansPanel.RefreshAsync();
        }

        private void GetDialPlans(int offset, int count)
        {
            if (!m_dialPlansPanelRefreshInProgress)
            {
                m_dialPlansPanelRefreshInProgress = true;

                m_riaContext.SIPDialPlans.Clear();
                var query = m_riaContext.GetSIPDialplansQuery().OrderBy(x => x.DialPlanName).Skip(offset).Take(count);
                query.IncludeTotalCount = true;
                m_riaContext.Load(query, LoadBehavior.RefreshCurrent, SIPDialPlansLoaded, null);
            }
            else
            {
                LogActivityMessage_External(MessageLevelsEnum.Warn, "A Dial Plans refresh is already in progress.");
            }
        }

        private void SIPDialPlansLoaded(LoadOperation<SIPDialPlan> lo)
        {
            if (lo.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error loading the Dial Plans. " + lo.Error.Message);
            }
            else
            {
                m_dialPlansPanel.AssetListTotal = lo.TotalEntityCount;
                m_dialPlansPanel.SetAssetListSource(m_riaContext.SIPDialPlans);
                //LogActivityMessage_External(MessageLevelsEnum.Info, "Dial Plans loaded, total " + lo.TotalEntityCount + " " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + ".");

                if (lo.TotalEntityCount == 0)
                {
                    LogActivityMessage_External(MessageLevelsEnum.Warn, "There were no existing dial plans.");
                }
            }

            m_dialPlansPanelRefreshInProgress = false;
        }

        private void DialPlansPanel_Add()
        {
            m_selectedDialPlan = null;
            m_addControl = new DialPlanAddControl(m_owner, DetailsControlClosed, m_riaContext);
            m_dialPlansPanel.SetDetailsElement(m_addControl);
        }

        private void UpdateDialPlan(SIPDialPlan dialPlan)
        {
            m_riaContext.SubmitChanges(UpdateDialPlanComplete, dialPlan);
        }

        private void UpdateDialPlanComplete(SubmitOperation so)
        {
            SIPDialPlan dialPlan = (SIPDialPlan)so.UserState;

            if (so.HasError)
            {
                if (m_editControl != null)
                {
                    m_editControl.WriteStatusMessage(MessageLevelsEnum.Error, "There was an error performing a Dial Plan update." + so.Error.Message);
                }
                else
                {
                    LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error performing a Dial Plan update." + so.Error.Message);
                }

                so.MarkErrorAsHandled();
            }
            else
            {
                if (m_editControl != null)
                {
                    m_editControl.WriteStatusMessage(MessageLevelsEnum.Info, "Update completed successfully for " + dialPlan.DialPlanName + ".");
                }
            }
        }

        private void DialPlansDataGrid_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (!m_dialPlansPanelRefreshInProgress && m_riaContext.SIPDialPlans.Count() > 0)
                {
                    DataGrid dataGrid = (DataGrid)sender;
                    if (dataGrid.CurrentColumn.Header as string != "Delete")
                    {
                        SIPDialPlan dialPlan = (SIPDialPlan)m_dialPlansDataGrid.SelectedItem;

                        if (m_selectedDialPlan == null || m_selectedDialPlan != dialPlan)
                        {
                            m_selectedDialPlan = dialPlan;

                            if (m_selectedDialPlan.ScriptType == SIPDialPlanScriptTypesEnum.TelisWizard)
                            {
                                if (m_wizardEditControl != null)
                                {
                                    m_wizardEditControl.DisableSelectionChanges();
                                }

                                m_wizardEditControl = new DialPlanWizard(LogActivityMessage_External, m_selectedDialPlan, m_owner, null, UpdateDialPlan, DetailsControlClosed, m_riaContext);
                                m_dialPlansPanel.SetDetailsElement(m_wizardEditControl);
                            }
                            else
                            {
                                m_editControl = new DialPlanUpdateControl(m_selectedDialPlan, m_owner, UpdateDialPlan, DetailsControlClosed);
                                m_dialPlansPanel.SetDetailsElement(m_editControl);
                            }
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "Exception showing DialPlan details. " + excp.Message);
                m_selectedDialPlan = null;
            }
        }

        private void DeleteDialPlan(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SIPDialPlan dialPlan = m_dialPlansDataGrid.SelectedItem as SIPDialPlan;
            MessageBoxResult confirmDelete = MessageBox.Show("Press Ok to delete " + dialPlan.DialPlanName + ".", "Confirm Delete", MessageBoxButton.OKCancel);
            if (confirmDelete == MessageBoxResult.OK)
            {
                LogActivityMessage_External(MessageLevelsEnum.Info, "Sending delete request for Dial Plan " + dialPlan.DialPlanName + ".");
                m_riaContext.SIPDialPlans.Remove(dialPlan);
                m_riaContext.SubmitChanges(DeleteDialPlanComplete, dialPlan);
            }
        }

        private void DeleteDialPlanComplete(SubmitOperation so)
        {
            if (so.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error deleting the dial plan. " + so.Error.Message);
                so.MarkErrorAsHandled();
            }
            else
            {
                m_dialPlansPanel.AssetDeleted();
            }
        }

        private void DetailsControlClosed()
        {
            m_dialPlansPanel.CloseDetailsPane();
            m_selectedDialPlan = null;
        }
    }
}