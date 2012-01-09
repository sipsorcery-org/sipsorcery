using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SIPSorcery.Entities;
using SIPSorcery.SIP;
using SIPSorcery.Sys;

namespace SIPSorcery
{
    public partial class SimpleWizardOutRuleControl : UserControl
    {
        public const string ADD_TEXT = "New Rule";
        public const string UPDATE_TEXT = "Update Rule";
        private const string PLEASE_CHOOSE_OPTION = "Please Choose";
        private const decimal DEFAULT_RULE_PRIORITY = 1M;
        private const int DEFAULT_REJECT_RESPONSE_INDEX = 4;
        private const string DEFAULT_DIAL_DESTINATION = "${EXTEN}";

        public event Action<SimpleWizardRule> Update;
        public event Action<SimpleWizardRule> Add;

        public List<SIPProvider> SIPProviders
        {
            set
            {
                value.Insert(0, new SIPProvider() { ProviderName = PLEASE_CHOOSE_OPTION });
                value.Insert(1, new SIPProvider() { ProviderName = "local" });
                m_ruleProvider.DataContext = value;
                m_ruleProvider.SelectedIndex = 0;
            }
        }

        private SimpleWizardRule m_ruleToUpdate;        // If this is set means the control is updating an existing rule as opposed to adding a new one.

        public SimpleWizardOutRuleControl()
        {
            // Required to initialize variables
            InitializeComponent();
            m_rulePriority.Text = DEFAULT_RULE_PRIORITY.ToString();
            DialCommandSB.Begin();
            HideError.Begin();
        }

        /// <summary>
        /// Sets the UI fields based on the supplied rule. If the rule is null the fields are reset ready for a new rule to be added.
        /// </summary>
        public void SetRuleToUpdate(SimpleWizardRule rule)
        {
            if (rule != null)
            {
                m_ruleToUpdate = rule;
                SetStatusMessage(UPDATE_TEXT, false);

                m_rulePatternType.SelectedIndex = m_rulePatternType.Items.IndexOf(m_rulePatternType.Items.Single(x => ((TextBlock)x).Text == rule.PatternType));
                m_rulePattern.Text = rule.Pattern;
                m_ruleCommandType.SelectedIndex = m_ruleCommandType.Items.IndexOf(m_ruleCommandType.Items.Single(x => ((TextBlock)x).Text == rule.Command));
                m_ruleCommandString.Text = rule.CommandParameter1;
                m_ruleDescription.Text = rule.Description;
                m_rulePriority.Text = rule.Priority.ToString();
                m_ruleIsDisabled.IsChecked = rule.IsDisabled;

                SetCommandParameterFieldsForRule(rule);
            }
            else
            {
                m_ruleToUpdate = null;
                SetStatusMessage(ADD_TEXT, false);

                m_ruleIsDisabled.IsChecked = false;
                m_rulePatternType.SelectedIndex = 0;
                m_rulePattern.Text = String.Empty;
                m_ruleCommandString.Text = DEFAULT_DIAL_DESTINATION;
                m_ruleProvider.SelectedIndex = 0;
                m_ruleDescription.Text = String.Empty;
                m_rulePriority.Text = DEFAULT_RULE_PRIORITY.ToString();
                m_ruleAdvancedDialString.Text = String.Empty;
                m_ruleRingDuration.Text = String.Empty;
                m_ruleAnswerDuration.Text = String.Empty;
                m_rejectResponseCode.SelectedIndex = DEFAULT_REJECT_RESPONSE_INDEX;
                m_rejectReason.Text = String.Empty;
                m_ruleCommandType.SelectedIndex = 0;
                HideError.Begin();
            }
        }

        private void Submit(object sender, System.Windows.RoutedEventArgs e)
        {
            decimal priority = DEFAULT_RULE_PRIORITY;
            Decimal.TryParse(m_rulePriority.Text, out priority);

            if (m_ruleToUpdate == null)
            {
                SimpleWizardRule rule = new SimpleWizardRule()
                {
                    ID = Guid.Empty.ToString(),             // Will be set in the manager.
                    Owner = "None",                         // Will be set in the manager.
                    DialPlanID = Guid.Empty.ToString(),     // Will be set in the manager.
                    Direction = SIPCallDirection.Out.ToString(),
                    PatternType = ((TextBlock)m_rulePatternType.SelectedValue).Text,
                    Pattern = m_rulePattern.Text,
                    Command = ((TextBlock)m_ruleCommandType.SelectedValue).Text,
                    Description = m_ruleDescription.Text,
                    Priority = priority,
                    IsDisabled = m_ruleIsDisabled.IsChecked.GetValueOrDefault()
                };

                string commandParameterError = SetRuleCommandParameters(rule);
                if (commandParameterError != null)
                {
                    SetErrorMessage(commandParameterError);
                }
                else if (rule.Pattern.IsNullOrBlank())
                {
                    SetErrorMessage("A pattern must be specified to match the outgoing call.");
                }
                else
                {
                    HideError.Begin();
                    Add(rule);
                }
            }
            else
            {
                m_ruleToUpdate.IsDisabled = m_ruleIsDisabled.IsChecked.GetValueOrDefault();
                m_ruleToUpdate.PatternType = ((TextBlock)m_rulePatternType.SelectedValue).Text;
                m_ruleToUpdate.Pattern = m_rulePattern.Text;
                m_ruleToUpdate.Command = ((TextBlock)m_ruleCommandType.SelectedValue).Text;
                m_ruleToUpdate.Description = m_ruleDescription.Text;
                m_ruleToUpdate.Priority = priority;

                string commandParameterError = SetRuleCommandParameters(m_ruleToUpdate);
                if (commandParameterError != null)
                {
                    SetErrorMessage(commandParameterError);
                }
                else if (m_ruleToUpdate.Pattern.IsNullOrBlank())
                {
                    SetErrorMessage("A pattern must be specified to match the outgoing call.");
                }
                else
                {
                    HideError.Begin();
                    Update(m_ruleToUpdate);
                }
            }
        }

        private void Cancel(object sender, System.Windows.RoutedEventArgs e)
        {
            SetRuleToUpdate(null);
        }

        public void SetStatusMessage(string status, bool disableInput)
        {
            m_rulePattern.IsEnabled = !disableInput;
            m_ruleCommandType.IsEnabled = !disableInput;
            m_ruleCommandString.IsEnabled = !disableInput;
            m_ruleDescription.IsEnabled = !disableInput;
            m_rulePriority.IsEnabled = !disableInput;
            m_ruleSaveButton.IsEnabled = !disableInput;
            m_ruleCancelButton.IsEnabled = !disableInput;

            m_descriptionText.Text = status;
        }

        public void SetErrorMessage(string errorMessage)
        {
            //m_rulePattern.IsEnabled = false;
            //m_ruleCommandType.IsEnabled = false;
            //m_ruleCommandString.IsEnabled = false;
            //m_ruleDescription.IsEnabled = false;
            //m_rulePriority.IsEnabled = false;
            //m_ruleSaveButton.IsEnabled = false;
            //m_ruleCancelButton.IsEnabled = false;

            //m_errorCanvas.Visibility = System.Windows.Visibility.Visible;
            m_errorMessageTextBlock.Text = errorMessage;

            HideError.Stop();
        }

        private void CloseErrorMessage(object sender, System.Windows.RoutedEventArgs e)
        {
            //m_rulePattern.IsEnabled = true;
            //m_ruleCommandType.IsEnabled = true;
            //m_ruleCommandString.IsEnabled = true;
            //m_ruleDescription.IsEnabled = true;
            //m_rulePriority.IsEnabled = true;
            //m_ruleSaveButton.IsEnabled = true;
            //m_ruleCancelButton.IsEnabled = true;

            //m_errorCanvas.Visibility = System.Windows.Visibility.Collapsed;

            //m_descriptionText.Text = (m_ruleToUpdate != null) ? UPDATE_TEXT : ADD_TEXT;

            HideError.Begin();
        }

        private string Validate(SimpleWizardRule rule)
        {
            var validationResults = new List<ValidationResult>();
            var validationContext = new ValidationContext(rule, null, null);
            Validator.TryValidateObject(rule, validationContext, validationResults);
            rule.ValidationErrors.Clear();

            if (validationResults.Count > 0)
            {
                return validationResults[0].ErrorMessage;
            }
            //else
            //{
            //    if (rule.RuleType == SimpleWizardRuleTypes.Regex)
            //    {
            //        try
            //        {
            //            new Regex(rule.Pattern);
            //        }
            //        catch(Exception excp)
            //        {
            //            return "The rule pattern was not recognised as a valid regular expression. " + excp.Message;
            //        }
            //    }
            //}

            return null;
        }

        private void RuleCommandType_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var ruleCommandComboBox = sender as ComboBox;

            if (ruleCommandComboBox != null && m_ruleCommandLabel != null && ruleCommandComboBox.SelectedValue != null)
            {
                var command = (SimpleWizardCommandTypes)Enum.Parse(typeof(SimpleWizardCommandTypes), ((TextBlock)ruleCommandComboBox.SelectedValue).Text, true);

                switch (command)
                {
                    case SimpleWizardCommandTypes.Dial:
                        AdvancedDialCommandSB.Stop();
                        RejectCommandSB.Stop();
                        DialCommandSB.Begin();
                        break;

                    case SimpleWizardCommandTypes.DialAdvanced:
                        DialCommandSB.Stop();
                        RejectCommandSB.Stop();
                        AdvancedDialCommandSB.Begin();
                        break;

                    case SimpleWizardCommandTypes.Reject:
                        DialCommandSB.Stop();
                        AdvancedDialCommandSB.Stop();
                        RejectCommandSB.Begin();
                        break;
                }
            }
        }

        /// <summary>
        /// Sets the command parameter properties on a rule based on the rule's command type. The rule's command type
        /// dictates which input fields will eb used for each command parameter.
        /// </summary>
        private string SetRuleCommandParameters(SimpleWizardRule rule)
        {
            if (rule.CommandType == SimpleWizardCommandTypes.Dial)
            {
                rule.CommandParameter1 = m_ruleCommandString.Text;
                if (m_ruleProvider.SelectedValue == null || ((SIPProvider)m_ruleProvider.SelectedValue).ProviderName == PLEASE_CHOOSE_OPTION)
                {
                    return "No provider was selected for the Dial command.";
                }
                else
                {
                    rule.CommandParameter2 = ((SIPProvider)m_ruleProvider.SelectedValue).ProviderName;
                }
            }
            else if (rule.CommandType == SimpleWizardCommandTypes.DialAdvanced)
            {
                rule.CommandParameter1 = m_ruleAdvancedDialString.Text;
                rule.CommandParameter2 = m_ruleRingDuration.Text;
                rule.CommandParameter3 = m_ruleAnswerDuration.Text;
            }
            else if (rule.CommandType == SimpleWizardCommandTypes.Reject)
            {
                rule.CommandParameter1 = ((TextBlock)m_rejectResponseCode.SelectedValue).Text.Substring(0, 3);
                rule.CommandParameter2 = m_rejectReason.Text;
            }

            return null;
        }

        /// <summary>
        /// Sets the command parameter fields based on the specified rule. The command parameters mean different 
        /// things and apply to different controls dependent on the rule's command type.
        /// </summary>
        private void SetCommandParameterFieldsForRule(SimpleWizardRule rule)
        {
            if (rule.CommandType == SimpleWizardCommandTypes.Dial && m_ruleProvider.Items != null && m_ruleProvider.Items.Count > 0)
            {
                m_ruleCommandString.Text = rule.CommandParameter1;
                if (m_ruleProvider.Items.Any(x => ((SIPProvider)x).ProviderName == rule.CommandParameter2))
                {
                    // The second command parameter holds the provider.
                    m_ruleProvider.SelectedIndex = m_ruleProvider.Items.IndexOf(m_ruleProvider.Items.Single(x => ((SIPProvider)x).ProviderName == rule.CommandParameter2));
                }
            }
            else if (rule.CommandType == SimpleWizardCommandTypes.DialAdvanced)
            {
                m_ruleAdvancedDialString.Text = rule.CommandParameter1;
                m_ruleRingDuration.Text = rule.CommandParameter2;
                m_ruleAnswerDuration.Text = rule.CommandParameter3;
            }
            else if (rule.CommandType == SimpleWizardCommandTypes.Reject)
            {
                m_rejectResponseCode.SelectedIndex = m_rejectResponseCode.Items.IndexOf(m_rejectResponseCode.Items.Single(x => ((TextBlock)x).Text.StartsWith(rule.CommandParameter1)));
                m_rejectReason.Text = rule.CommandParameter2;
            }
        }
    }
}