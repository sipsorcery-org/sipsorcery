using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SIPSorcery.SIP.App;

namespace SIPSorcery
{
	public partial class DialPlanDetailsControl : UserControl
	{
        private DialPlanUpdateDelegate DialPlanAdd_External;
        private DialPlanUpdateDelegate DialPlanUpdate_External;
        private ControlClosedDelegate ControlClosed_External;

        private DetailsControlModesEnum m_detailsMode;
        private string m_owner;
        private SIPDialPlan m_dialPlan;
        
		public DialPlanDetailsControl()
		{
			InitializeComponent();
		}

        public DialPlanDetailsControl(
            DetailsControlModesEnum mode, 
            SIPDialPlan dialPlan, 
            string owner,
            DialPlanUpdateDelegate dialPlanAdd, 
            DialPlanUpdateDelegate dialPlanUpdate, 
            ControlClosedDelegate closed)
        {
            InitializeComponent();

            m_detailsMode = mode;
            m_owner = owner;
            m_dialPlan = dialPlan;

            DialPlanAdd_External = dialPlanAdd;
            DialPlanUpdate_External = dialPlanUpdate;
            ControlClosed_External = closed;

            if (mode == DetailsControlModesEnum.Edit)
            {
                m_applyButton.Content = "Update";
                PopulateDataFields(m_dialPlan);
            }
            else
            {
                m_dialPlanIdCanvas.Visibility = Visibility.Collapsed;
                m_applyButton.Content = "Add";
            }
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
            m_dialPlanId.Text = dialPlan.Id.ToString();
            m_dialPlanName.Text = dialPlan.DialPlanName;
            m_dialPlanTraceEmailAddress.Text = (dialPlan.TraceEmailAddress != null) ? dialPlan.TraceEmailAddress : String.Empty;
            m_dialPlanText.Text = dialPlan.DialPlanScript;

            if(dialPlan.ScriptType == SIPDialPlanScriptTypesEnum.Asterisk)
            {
                m_dialPlanTypeExtension.IsChecked = true;
            }
        }

        private void UpdateButtonClicked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (m_detailsMode == DetailsControlModesEnum.Edit)
            {
                Update();
            }
            else
            {
                Add();
            }
        }

        private void Add()
        {
            if (m_dialPlanName.Text.Trim().Length == 0)
            {
                WriteStatusMessage(MessageLevelsEnum.Warn, "The Dial Plan Name cannot be empty.");
            }
            else
            {
                SIPDialPlanScriptTypesEnum scriptType = (m_dialPlanTypeRuby.IsChecked.Value) ? SIPDialPlanScriptTypesEnum.Ruby : SIPDialPlanScriptTypesEnum.Asterisk;

                string dialPlanText = (m_dialPlanText.Text != null) ? Regex.Replace(m_dialPlanText.Text.Trim(), "\r([^\n])", "\r\n${1}") : null;
                SIPDialPlan dialPlan = new SIPDialPlan(m_owner, m_dialPlanName.Text.Trim(), m_dialPlanTraceEmailAddress.Text.Trim(), dialPlanText, scriptType);

                WriteStatusMessage(MessageLevelsEnum.Info, "Adding Dial Plan please wait...");

                DialPlanAdd_External(dialPlan);
            }
        }

        private void Update()
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
                m_dialPlan.ScriptTypeDescription = (m_dialPlanTypeExtension.IsChecked.Value) ? SIPDialPlanScriptTypesEnum.Asterisk.ToString() : SIPDialPlanScriptTypesEnum.Ruby.ToString();

                DialPlanUpdate_External(m_dialPlan);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            ControlClosed_External();
        }
	}
}