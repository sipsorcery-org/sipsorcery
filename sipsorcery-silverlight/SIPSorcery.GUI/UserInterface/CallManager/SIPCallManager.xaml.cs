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
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Persistence;
using SIPSorcery.Silverlight.Messaging;
using SIPSorcery.SIPSorceryProvisioningClient;
using SIPSorcery.UIControls;

namespace SIPSorcery
{   
    public partial class SIPCallManager : UserControl
	{
        private const int SIPCALLS_DISPLAY_COUNT = 25;
        private const int SIPCDRS_DISPLAY_COUNT = 25;

        private ActivityMessageDelegate LogActivityMessage_External;
        private ActivityProgressDelegate ShowActivityProgress_External;
        private SIPSorceryPersistor m_persistor;

        private ObservableCollection<SIPDialogueAsset> m_sipCalls;
        private SIPDialogueAsset m_selectedSIPCall;
        private int m_sipCallsPanelOffset;
        private int m_sipCallsPanelCount;
        private int m_sipCDRsPanelOffset;
        private int m_sipCDRsPanelCount;

        private string m_owner;
        private string m_sipCallsWhere;     // Used when filtering is enabled.
        private string m_sipCDRsWhere = "AnsweredStatus != 401 and AnsweredStatus != 407";      // Used when filtering is enabled.

        public bool Initialised;
        private bool m_initialLoadComplete;
        private bool m_sipCallsLoaded;
        private bool m_sipCDRsLoaded;
        private bool m_sipCallLoadInProgress;
        private bool m_sipCallsPanelRefreshInProgress;
        private bool m_sipCDRsPanelRefreshInProgress;

        public SIPCallManager()
        {
            InitializeComponent();
        }

        public SIPCallManager(
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

            m_sipCallsPanel.SetTitle("Calls");
            m_sipCallsPanel.DisplayCount = SIPCALLS_DISPLAY_COUNT;
            m_sipCallsPanel.MenuEnableAdd(false);
            m_sipCallsPanel.MenuEnableFilter(false);
            m_sipCallsPanel.MenuEnableDelete(false);
            m_sipCallsPanel.GetAssetList = GetSIPCalls;

            m_sipCDRsPanel.SetTitle("CDRs");
            m_sipCDRsPanel.DisplayCount = SIPCDRS_DISPLAY_COUNT;
            m_sipCDRsPanel.MenuEnableAdd(false);
            m_sipCDRsPanel.MenuEnableFilter(false);
            m_sipCDRsPanel.MenuEnableDelete(false);
            m_sipCDRsPanel.GetAssetList = GetSIPCDRs;
		}

        public void Initialise()
        {
            Initialised = true;

            m_persistor.GetCallsComplete += new GetCallsCompleteDelegate(GetCallsComplete);
            m_persistor.GetCallsCountComplete += new GetCallsCountCompleteDelegate(GetCallsCountComplete);
            m_persistor.GetCDRsCountComplete += new GetCDRsCountCompleteDelegate(GetCDRsCountComplete);
            m_persistor.GetCDRsComplete += new GetCDRsCompleteDelegate(GetCDRsComplete);

           Load();
        }

        private void Load()
        {
            try
            {
                if (!m_sipCallsLoaded)
                {
                    ShowActivityProgress_External(50);
                    LogActivityMessage_External(MessageLevelsEnum.Info, "Loading in progress calls...");
                    m_sipCallsPanel.RefreshAsync();
                }
                else if (!m_sipCDRsLoaded)
                {
                    ShowActivityProgress_External(75);
                    LogActivityMessage_External(MessageLevelsEnum.Info, "Loading CDRs...");
                    m_sipCDRsPanel.RefreshAsync();
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
            m_persistor.GetCallsComplete -= new GetCallsCompleteDelegate(GetCallsComplete);
            m_persistor.GetCallsCountComplete -= new GetCallsCountCompleteDelegate(GetCallsCountComplete);
            m_persistor.GetCDRsCountComplete -= new GetCDRsCountCompleteDelegate(GetCDRsCountComplete);
            m_persistor.GetCDRsComplete -= new GetCDRsCompleteDelegate(GetCDRsComplete);
        }

        public void SIPMonitorMachineEventHandler(SIPMonitorMachineEvent machineEvent)
        {
            // Update the calls display.
            if (m_initialLoadComplete && !m_sipCallsPanelRefreshInProgress)
            {
                m_sipCallsPanel.RefreshAsync();
            }
        }

        #region SIP Calls functions.

        private void GetSIPCalls(int offset, int count)
        {
            if (!m_sipCallsPanelRefreshInProgress)
            {
                m_sipCallsPanelRefreshInProgress = true;
                m_sipCallLoadInProgress = true;

                m_sipCallsPanelOffset = offset;
                m_sipCallsPanelCount = count;
                m_persistor.GetCallsCountAsync(m_sipCallsWhere);
            }
            else
            {
                LogActivityMessage_External(MessageLevelsEnum.Warn, "A SIP calls refresh is already in progress.");
            }
        }

        private void GetCallsCountComplete(GetCallsCountCompletedEventArgs e)
        {
            try
            {
                m_sipCallsPanel.AssetListTotal = e.Result;
                m_persistor.GetCallsAsync(m_sipCallsWhere, m_sipCallsPanelOffset, m_sipCallsPanelCount);
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error retrieving the number of in progress calls. " + excpMessage);

                m_sipCallLoadInProgress = false;
                m_sipCallsPanelRefreshInProgress = false;
            }
        }

        private void GetCallsComplete(GetCallsCompletedEventArgs e)
        {
            m_sipCallsLoaded = true;

            try
            {
                m_sipCalls = e.Result;
                m_sipCallsPanel.SetAssetListSource(m_sipCalls);

                if (!m_initialLoadComplete)
                {
                    Load();
                }

                LogActivityMessage_External(MessageLevelsEnum.Info, "In progress calls successfully loaded " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + ".");
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error retrieving the in progress calls. " + excpMessage);
            }
            finally
            {
                m_sipCallLoadInProgress = false;
                m_sipCallsPanelRefreshInProgress = false;
            }
        }

        private void SIPCallsDataGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (m_initialLoadComplete && !m_sipCallLoadInProgress && m_sipCalls.Count > 0)
                {
                    DataGrid dataGrid = (DataGrid)sender;
                    SIPDialogueAsset sipCall = (SIPDialogueAsset)m_sipCallsDataGrid.SelectedItem;

                    if (m_selectedSIPCall == null || m_selectedSIPCall != sipCall)
                    {
                        m_selectedSIPCall = sipCall;
                        //m_sipCallsPanel.SetDetailsElement(editControl);
                    }
                }
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                LogActivityMessage_External(MessageLevelsEnum.Error, "Exception showing Call details. " + excpMessage);
                m_selectedSIPCall = null;
            }
        }

        #endregion

        #region SIP CDR Functions.

        private void GetSIPCDRs(int offset, int count)
        {
            if (!m_sipCDRsPanelRefreshInProgress)
            {
                m_sipCDRsPanelRefreshInProgress = true;

                m_sipCDRsPanelOffset = offset;
                m_sipCDRsPanelCount = count;
                m_persistor.GetCDRsCountAsync(m_sipCDRsWhere);
            }
            else
            {
                LogActivityMessage_External(MessageLevelsEnum.Warn, "A SIP CDRs refresh is already in progress.");
            }
        }

        private void GetCDRsCountComplete(GetCDRsCountCompletedEventArgs e)
        {
            try
            {
                m_sipCDRsPanel.AssetListTotal = e.Result;
                LogActivityMessage_External(MessageLevelsEnum.Info, e.Result + " CDRs found.");
                m_persistor.GetCDRsAsync(m_sipCDRsWhere, m_sipCDRsPanelOffset, m_sipCDRsPanelCount);
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error retrieving the number of SIP CDRs. " + excpMessage);

                m_sipCDRsPanelRefreshInProgress = false;
            }
        }

        private void GetCDRsComplete(GetCDRsCompletedEventArgs e)
        {
            m_sipCDRsLoaded = true;

            try
            {
                m_sipCDRsPanel.SetAssetListSource(e.Result);

                if (!m_initialLoadComplete)
                {
                    Load();
                }

                LogActivityMessage_External(MessageLevelsEnum.Info, "CDRs successfully loaded.");
            }
            catch (Exception excp)
            {
                string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error retrieving SIP CDRs. " + excpMessage);
            }
            finally
            {
                m_sipCDRsPanelRefreshInProgress = false;
            }
        }

        #endregion

        private void DetailsControlClosed()
        {
            m_selectedSIPCall = null;
            m_sipCallsPanel.CloseDetailsPane();
        }
	}
}