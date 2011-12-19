using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ServiceModel.DomainServices.Client;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SIPSorcery.Entities;
using SIPSorcery.Entities.Services;

namespace SIPSorcery
{
	public partial class DialPlanAddControl : UserControl
	{
        private ControlClosedDelegate ControlClosed_External;

        private Action<SIPDialPlan> m_addAction;
        private string m_owner;
        
		public DialPlanAddControl()
		{
			InitializeComponent();
		}

        public DialPlanAddControl(
            string owner,
            ControlClosedDelegate closed,
            Action<SIPDialPlan> addAction)
        {
            InitializeComponent();

            m_owner = owner;
            ControlClosed_External = closed;
            m_addAction = addAction;
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

        private void AddButtonClicked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (m_dialPlanName.Text.Trim().Length == 0)
            {
                WriteStatusMessage(MessageLevelsEnum.Warn, "The Dial Plan Name cannot be empty.");
            }
            else
            {
                SIPDialPlanScriptTypesEnum scriptType = SIPDialPlanScriptTypesEnum.Ruby;
                if (m_dialPlanTypeTelisWizard.IsChecked.Value)
                {
                    scriptType = SIPDialPlanScriptTypesEnum.TelisWizard;
                }
                else if (m_dialPlanTypeSimpleWizard.IsChecked.Value)
                {
                    scriptType = SIPDialPlanScriptTypesEnum.SimpleWizard;
                }

                SIPDialPlan dialPlan = new SIPDialPlan()
                {
                    ID = Guid.Empty.ToString(),
                    Owner = m_owner,
                    DialPlanName = m_dialPlanName.Text.Trim(),
                    ScriptTypeDescription = scriptType.ToString(),
                    Inserted = DateTimeOffset.UtcNow.ToString("o"),
                    LastUpdate = DateTimeOffset.UtcNow.ToString("o")
                };

                WriteStatusMessage(MessageLevelsEnum.Info, "Adding Dial Plan please wait...");

                AddDialPlan(dialPlan);
            }
        }

        private void AddDialPlan(SIPDialPlan dialPlan)
        {
            m_addAction(dialPlan);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            ControlClosed_External();
        }
	}
}