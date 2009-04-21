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
using SIPSorcery.Persistence;
using SIPSorcery.SIP.App;
using SIPSorcery.Silverlight.Messaging;
using SIPSorcery.SIPSorceryProvisioningClient;
using SIPSorcery.UIControls;

namespace SIPSorcery
{
    public partial class DialPlanManager : UserControl
	{
        private ActivityMessageDelegate LogActivityMessage_External;
        private ActivityProgressDelegate ShowActivityProgress_External;
        private SIPSorceryPersistor m_persistor;
        private DialPlanDetailsControl m_addControl;
        private DialPlanDetailsControl m_editControl;
        private SIPDialPlan m_selectedDialPlan;
        private int m_dialPlansPanelOffset;
        private int m_dialPlansPanelCount;
        private bool m_dialPlansPanelRefreshInProgress;

        private string m_owner;
        private string m_dialPlansWhere;    // Utilised when filtering is enabled.
        private ObservableCollection<SIPDialPlan> m_dialPlans;

        public bool Initialised { get; private set; }

        public DialPlanManager()
        {
            InitializeComponent();
        }

        public DialPlanManager(
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

            m_dialPlansPanel.SetTitle("Dial Plans");
            m_dialPlansPanel.MenuEnableFilter(false);
            m_dialPlansPanel.MenuEnableDelete(false);
            m_dialPlansPanel.Add += new MenuButtonClickedDelegate(DialPlansPanel_Add);
            m_dialPlansPanel.GetAssetList = GetDialPlans;
		}

        public void Initialise()
        {
            Initialised = true;

            m_persistor.GetDialPlansComplete += new GetDialPlansCompleteDelegate(GetDialPlansComplete);
            m_persistor.GetDialPlansCountComplete += new GetDialPlansCountCompleteDelegate(GetDialPlansCountComplete);
            m_persistor.UpdateDialPlanComplete += new UpdateDialPlanCompleteDelegate(UpdateDialPlanComplete);
            m_persistor.AddDialPlanComplete += new AddDialPlanCompleteDelegate(AddDialPlanComplete);
            m_persistor.DeleteDialPlanComplete += new DeleteDialPlanCompleteDelegate(DeleteDialPlanComplete);

            LogActivityMessage_External(MessageLevelsEnum.Info, "Loading Dial Plans...");
            m_dialPlansPanel.RefreshAsync();
        }

        public void Close()
        {
            m_persistor.GetDialPlansComplete -= new GetDialPlansCompleteDelegate(GetDialPlansComplete);
            m_persistor.GetDialPlansCountComplete -= new GetDialPlansCountCompleteDelegate(GetDialPlansCountComplete);
        }

        /// <summary>
        /// Provides access to the dial plan names for use by other controls such as the SIP Account manager.
        /// </summary>
        /// <param name="owner">The owning account to get the list of dial plan names for.</param>
        /// <returns>A list of dial plan names.</returns>
        public List<string> GetDialPlanNames(string owner)
        {
            List<string> dialPlanNames = new List<string>();

            foreach (SIPDialPlan dialPlan in m_dialPlans)
            {
                if (dialPlan.Owner == owner)
                {
                    dialPlanNames.Add(dialPlan.DialPlanName);
                }
            }

            return dialPlanNames;
        }

        private void GetDialPlans(int offset, int count)
        {
            if (!m_dialPlansPanelRefreshInProgress)
            {
                m_dialPlansPanelRefreshInProgress = true;

                m_dialPlansPanelOffset = offset;
                m_dialPlansPanelCount = count;
                m_persistor.GetDialPlansCountAsync(m_dialPlansWhere);
            }
            else
            {
                LogActivityMessage_External(MessageLevelsEnum.Warn, "A Dial Plans refresh is already in progress.");
            }
        }

        private void GetDialPlansCountComplete(GetDialPlansCountCompletedEventArgs e)
        {
            try
            {
                m_dialPlansPanel.AssetListTotal = e.Result;
                LogActivityMessage_External(MessageLevelsEnum.Info, "Dial Plans count " + e.Result +  " " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + ".");
                m_persistor.GetDialPlansAsync(m_dialPlansWhere, m_dialPlansPanelOffset, m_dialPlansPanelCount);
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error retrieving the Dial Plans count. " + excpMessage);

                m_dialPlansPanelRefreshInProgress = false;
            }
        }

        private void GetDialPlansComplete(GetDialPlansCompletedEventArgs e)
        {
            try
            {
                m_dialPlans = e.Result;

                m_dialPlansPanel.SetAssetListSource(m_dialPlans);
                LogActivityMessage_External(MessageLevelsEnum.Info, "Dial Plans successfully loaded " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + ".");
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error retrieving Dial Plans. " + excpMessage);
            }
            finally
            {
                m_dialPlansPanelRefreshInProgress = false;
            }
        }

        private void DialPlansPanel_Add()
        {
            m_selectedDialPlan = null;
            m_addControl = new DialPlanDetailsControl(DetailsControlModesEnum.Add, m_selectedDialPlan, m_owner, AddDialPlan, null, DetailsControlClosed);
            m_dialPlansPanel.SetDetailsElement(m_addControl);
        }

        private void AddDialPlan(SIPDialPlan dialPlan)
        {
            m_persistor.AddDialPlanAsync(dialPlan);
        }

        private void AddDialPlanComplete(AddDialPlanCompletedEventArgs e)
        {
            try
            {
                SIPDialPlan dialPlan = e.Result;

                if (m_addControl != null)
                {
                    m_addControl.WriteStatusMessage(MessageLevelsEnum.Info, "Dial Plan was successfully created for " + dialPlan.DialPlanName + ".");
                }

                if (m_dialPlans == null)
                {
                    m_dialPlansPanel.RefreshAsync();
                }
                else
                {
                    m_dialPlans.Add(dialPlan);
                    m_dialPlansPanel.AssetAdded();
                }
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                if (m_addControl != null)
                {
                    m_addControl.WriteStatusMessage(MessageLevelsEnum.Error, "Error adding Dial Plan. " + excpMessage);
                }
                else
                {
                    LogActivityMessage_External(MessageLevelsEnum.Error, "Error adding Dial Plan. " + excpMessage);
                }
            }
        }

        private void UpdateDialPlan(SIPDialPlan dialPlan)
        {
            m_persistor.UpdateDialPlanAsync(dialPlan);
        }
       
        private void UpdateDialPlanComplete(UpdateDialPlanCompletedEventArgs e)
        {
            try
            {
                // The Dial Plan returned by the web service call is the account after the update operation was carried out.
                SIPDialPlan dialPlan = e.Result;

                if (m_editControl != null)
                {
                    m_editControl.WriteStatusMessage(MessageLevelsEnum.Info, "Update completed successfully for " + dialPlan.DialPlanName + ".");
                }
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;

                if (m_editControl != null)
                {
                    m_editControl.WriteStatusMessage(MessageLevelsEnum.Error, "There was an error performing a Dial Plan update." + excpMessage);
                }
                else
                {
                    LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error performing a Dial Plan update." + excpMessage);
                }
            }
        }

        private void DialPlansDataGrid_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (!m_dialPlansPanelRefreshInProgress && m_dialPlans != null && m_dialPlans.Count > 0)
                {
                    DataGrid dataGrid = (DataGrid)sender;
                    if (dataGrid.CurrentColumn.Header as string != "Delete")
                    {
                        SIPDialPlan dialPlan = (SIPDialPlan)m_dialPlansDataGrid.SelectedItem;

                        if (m_selectedDialPlan == null || m_selectedDialPlan != dialPlan)
                        {
                            m_selectedDialPlan = dialPlan;
                            m_editControl = new DialPlanDetailsControl(DetailsControlModesEnum.Edit, m_selectedDialPlan, m_owner, null, UpdateDialPlan, DetailsControlClosed);
                            m_dialPlansPanel.SetDetailsElement(m_editControl);
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
                m_persistor.DeleteDialPlanAsync(dialPlan);
            }
        }

        private void DeleteDialPlanComplete(DeleteDialPlanCompletedEventArgs e)
        {
            try
            {
                SIPDialPlan dialPlan = e.Result;

                LogActivityMessage_External(MessageLevelsEnum.Info, "Delete completed successfully for " + dialPlan.DialPlanName + ".");

                for (int index = 0; index < m_dialPlans.Count; index++)
                {
                    if (m_dialPlans[index].Id == dialPlan.Id)
                    {
                        m_dialPlans.RemoveAt(index);
                        break;
                    }
                }

                m_dialPlansPanel.AssetDeleted();
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error deleting the Dial Plan. " + excpMessage);
            }
        }

        private void DetailsControlClosed()
        {
            m_dialPlansPanel.CloseDetailsPane();
            m_selectedDialPlan = null;
        }
	}
}