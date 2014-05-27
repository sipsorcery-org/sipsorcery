using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.DomainServices.Client;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
//using SIPSorcery.SIP;
using SIPSorcery.Entities;
using SIPSorcery.UIControls;
using SIPSorcery.Entities.Services;

namespace SIPSorcery
{   
    public partial class SIPCallManager : UserControl
	{
        private const int SIPCALLS_DISPLAY_COUNT = 25;
        private const int SIPCDRS_DISPLAY_COUNT = 25;

        private ActivityMessageDelegate LogActivityMessage_External;
        private ActivityProgressDelegate ShowActivityProgress_External;

        private SIPEntitiesDomainContext m_riaContext;
        private SIPDialogue m_selectedSIPCall;

        private string m_owner;

        public bool Initialised;
        private bool m_initialLoadComplete;
        private bool m_sipCallLoadInProgress;
        private bool m_sipCallsPanelRefreshInProgress;
        private bool m_sipCDRsPanelRefreshInProgress;

        public SIPCallManager()
        {
            InitializeComponent();
        }

        public SIPCallManager(
            ActivityMessageDelegate logActivityMessage,
            string owner,
            SIPEntitiesDomainContext riaContext)
		{
			InitializeComponent();

            LogActivityMessage_External = logActivityMessage;
            m_owner = owner;
            m_riaContext = riaContext;

            m_sipCallsPanel.SetTitle("Calls");
            m_sipCallsPanel.DisplayCount = SIPCALLS_DISPLAY_COUNT;
            m_sipCallsPanel.MenuEnableAdd(false);
            m_sipCallsPanel.MenuEnableFilter(false);
            m_sipCallsPanel.MenuEnableHelp(false);
            m_sipCallsPanel.GetAssetList = GetSIPCalls;

            m_sipCDRsPanel.SetTitle("CDRs");
            m_sipCDRsPanel.DisplayCount = SIPCDRS_DISPLAY_COUNT;
            m_sipCDRsPanel.MenuEnableAdd(false);
            m_sipCDRsPanel.MenuEnableFilter(false);
            m_sipCDRsPanel.MenuEnableHelp(false);
            m_sipCDRsPanel.GetAssetList = GetSIPCDRs;
		}

        public void Initialise()
        {
            Initialised = true;

            GetSIPCalls(0, SIPCALLS_DISPLAY_COUNT);
            GetSIPCDRs(0, SIPCDRS_DISPLAY_COUNT);
        }

        public void SIPMonitorMachineEventHandler(SIPSorcery.SIP.App.SIPMonitorMachineEvent machineEvent)
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

                m_riaContext.SIPDialogues.Clear();
                var query = m_riaContext.GetSIPDialoguesQuery().OrderBy(x => x.Inserted).Skip(offset).Take(count);
                query.IncludeTotalCount = true;
                m_riaContext.Load<SIPDialogue>(query, LoadBehavior.RefreshCurrent, GetSIPCallsComplete, null);
            }
            else
            {
                LogActivityMessage_External(MessageLevelsEnum.Warn, "A SIP calls refresh is already in progress.");
            }
        }

        private void GetSIPCallsComplete(LoadOperation lo)
        {
            if (lo.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error loading the SIP calls. " + lo.Error.Message);
                lo.MarkErrorAsHandled();
            }
            else
            {
                m_sipCallsPanel.AssetListTotal = lo.TotalEntityCount;
                m_sipCallsPanel.SetAssetListSource(m_riaContext.SIPDialogues);
                //LogActivityMessage_External(MessageLevelsEnum.Info, "SIP calls successfully loaded, total " + lo.TotalEntityCount + " " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + ".");
            }

            m_sipCallsPanelRefreshInProgress = false;
        }

        private void SIPCallsDataGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (m_initialLoadComplete && !m_sipCallLoadInProgress && m_riaContext.SIPDialogues.Count > 0)
                {
                    DataGrid dataGrid = (DataGrid)sender;
                    SIPDialogue sipCall = (SIPDialogue)m_sipCallsDataGrid.SelectedItem;

                    if (m_selectedSIPCall == null || m_selectedSIPCall != sipCall)
                    {
                        m_selectedSIPCall = sipCall;
                        //m_sipCallsPanel.SetDetailsElement(m_editControl);
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

                m_riaContext.CDRs.Clear();
                var query = m_riaContext.GetCDRsQuery().Where(x => x.AnsweredStatus != 401 && x.AnsweredStatus != 407).OrderByDescending(x => x.Created).Skip(offset).Take(count);
                query.IncludeTotalCount = true;
                m_riaContext.Load<CDR>(query, LoadBehavior.RefreshCurrent, GetCDRsComplete, null);
            }
            else
            {
                LogActivityMessage_External(MessageLevelsEnum.Warn, "A SIP CDRs refresh is already in progress.");
            }
        }

        private void GetCDRsComplete(LoadOperation lo)
        {
            if (lo.HasError)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "There was an error loading the SIP CDRs. " + lo.Error.Message);
                lo.MarkErrorAsHandled();
            }
            else
            {
                m_sipCDRsPanel.AssetListTotal = lo.TotalEntityCount;
                m_sipCDRsPanel.SetAssetListSource(m_riaContext.CDRs);
                //LogActivityMessage_External(MessageLevelsEnum.Info, "SIP CDRs successfully loaded, total " + lo.TotalEntityCount + " " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + ".");
            }

            m_sipCDRsPanelRefreshInProgress = false;
        }

        #endregion

        private void DetailsControlClosed()
        {
            m_selectedSIPCall = null;
            m_sipCallsPanel.CloseDetailsPane();
        }
	}
}