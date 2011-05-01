using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SIPSorcery.Entities;

namespace SIPSorcery
{
	public partial class DialPlanUpdateControl : UserControl
	{
        private DialPlanUpdateDelegate DialPlanUpdate_External;
        private ControlClosedDelegate ControlClosed_External;

        private string m_owner;
        private SIPDialPlan m_dialPlan;
        
		public DialPlanUpdateControl()
		{
			InitializeComponent();
		}

        public DialPlanUpdateControl(
            SIPDialPlan dialPlan, 
            string owner,
            DialPlanUpdateDelegate dialPlanUpdate, 
            ControlClosedDelegate closed)
        {
            InitializeComponent();

            m_owner = owner;
            m_dialPlan = dialPlan;

            DialPlanUpdate_External = dialPlanUpdate;
            ControlClosed_External = closed;

            PopulateDataFields(m_dialPlan);
        }

        public void WriteStatusMessage(MessageLevelsEnum status, string message)
        {
            if (status == MessageLevelsEnum.Error)
            {
                UIHelper.SetColouredText(m_statusTextBlock, message, MessageLevelsEnum.Error);
            }
            else if (status == MessageLevelsEnum.Warn)
            {
                UIHelper.SetColouredText(m_statusTextBlock, message, MessageLevelsEnum.Warn);
            }
            else
            {
                UIHelper.SetColouredText(m_statusTextBlock, message, MessageLevelsEnum.Info);
            }
        }

        private void PopulateDataFields(SIPDialPlan dialPlan)
        {
            m_dialPlanId.Text = dialPlan.ID;
            m_dialPlanName.Text = dialPlan.DialPlanName;
            m_dialPlanTraceEmailAddress.Text = (dialPlan.TraceEmailAddress != null) ? dialPlan.TraceEmailAddress : String.Empty;
            m_dialPlanText.Text = (dialPlan.DialPlanScript != null) ? dialPlan.DialPlanScript : String.Empty;
            m_dialPlanScriptType.Text = dialPlan.ScriptTypeDescription;
            m_dialPlanAcceptNonInvite.IsChecked = dialPlan.AcceptNonInvite;
        }

        private void UpdateButtonClicked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (m_dialPlanName.Text.Trim().Length == 0)
            {
                WriteStatusMessage(MessageLevelsEnum.Warn, "The Dial Plan Name cannot be empty.");
            }
            else
            {
                WriteStatusMessage(MessageLevelsEnum.Info, "Updating Dial Plan please wait...");

                string dialPlanText = (m_dialPlanText.Text != null) ? Regex.Replace(m_dialPlanText.Text.Trim(), "\r([^\n])", "\r\n${1}") : null;
                m_dialPlan.DialPlanScript = dialPlanText;
                m_dialPlan.TraceEmailAddress = m_dialPlanTraceEmailAddress.Text;
                m_dialPlan.DialPlanName = m_dialPlanName.Text;
                m_dialPlan.AcceptNonInvite = m_dialPlanAcceptNonInvite.IsChecked.Value;
 
                DialPlanUpdate_External(m_dialPlan);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            ControlClosed_External();
        }
	}
}