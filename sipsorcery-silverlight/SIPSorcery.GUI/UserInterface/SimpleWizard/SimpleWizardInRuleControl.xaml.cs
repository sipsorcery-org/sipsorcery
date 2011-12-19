using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.ServiceModel.DomainServices.Client;
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
	public partial class SimpleWizardInRuleControl : UserControl
	{
        public const string ADD_TEXT = "New Rule";
        public const string UPDATE_TEXT = "Update Rule";
        private const string PLEASE_CHOOSE_OPTION = "Please Choose";
        private const decimal DEFAULT_RULE_PRIORITY = 1M;
        private const int DEFAULT_REJECT_RESPONSE_INDEX = 4;
        private const string DEFAULT_DIAL_DESTINATION = "${EXTEN}";

        public event Action<SimpleWizardRule> Update;
        public event Action<SimpleWizardRule> Add;

        private SimpleWizardRule m_ruleToUpdate;        // If this is set means the control is updating an existing rule as opposed to adding a new one.

        private bool m_initialised = false;

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

		public SimpleWizardInRuleControl()
		{
			// Required to initialize variables
			InitializeComponent();
            m_rulePriority.Text = DEFAULT_RULE_PRIORITY.ToString();
            DialCommandSB.Begin();
            HideErrorSB.Begin();
            SpecificTimeStoryboard.Begin();
            m_initialised = true;
		}

        public void SetToSIPAccounts(EntitySet<SIPAccount> toAccounts)
        {
            m_ruleToAccount.Items.Clear();
            m_ruleToAccount.Items.Add(PLEASE_CHOOSE_OPTION);

            if (toAccounts != null && toAccounts.Count > 0)
            {
                foreach (SIPAccount toAccount in toAccounts)
                {
                    m_ruleToAccount.Items.Add(toAccount.SIPUsername + "@" + toAccount.SIPDomain);
                }
            }

            //m_ruleToAccount.SelectedIndex = 0;
        }

		public void SetRuleToUpdate(SimpleWizardRule rule)
        {
            if (rule != null)
            {
                m_ruleToUpdate = rule;
                SetStatusMessage(UPDATE_TEXT, false);

                if (rule.ToSIPAccount != null && m_ruleToAccount.Items.Any(x => x.ToString() == rule.ToSIPAccount))
                {
                    m_ruleToSIPAccount.IsChecked = true;
                    m_ruleToChoiceAny.IsChecked = false;
                    m_ruleToAccount.SelectedIndex = m_ruleToAccount.Items.IndexOf(m_ruleToAccount.Items.Single(x => x.ToString() == rule.ToSIPAccount));
                }
                else
                {
                    m_ruleToSIPAccount.IsChecked = false;
                    m_ruleToChoiceAny.IsChecked = true;
                }

                //m_ruleType.SelectedIndex = m_ruleType.Items.IndexOf(m_ruleType.Items.Single(x => ((TextBlock)x).Text == rule.RuleType.ToString()));
                m_rulePattern.Text = rule.Pattern;
                m_ruleCommandType.SelectedIndex = m_ruleCommandType.Items.IndexOf(m_ruleCommandType.Items.Single(x => ((TextBlock)x).Text == rule.Command));
                m_ruleDescription.Text = rule.Description;
                m_rulePriority.Text = rule.Priority.ToString();

                if (rule.TimePattern != null)
                {
                    m_ruleWhenSpecificTimes.IsChecked = true;
                    var matchedDays = rule.MatchedDays();
                    m_monCheckbox.IsChecked = matchedDays.Contains(DayOfWeek.Monday);
                    m_tueCheckbox.IsChecked = matchedDays.Contains(DayOfWeek.Tuesday);
                    m_wedCheckbox.IsChecked = matchedDays.Contains(DayOfWeek.Wednesday);
                    m_thuCheckbox.IsChecked = matchedDays.Contains(DayOfWeek.Thursday);
                    m_friCheckbox.IsChecked = matchedDays.Contains(DayOfWeek.Friday);
                    m_satCheckbox.IsChecked = matchedDays.Contains(DayOfWeek.Saturday);
                    m_sunCheckbox.IsChecked = matchedDays.Contains(DayOfWeek.Sunday);
                    m_startTimeHour.Text = rule.GetStartHour().ToString();
                    m_startTimeMin.Text = rule.GetStartMinute().ToString();
                    m_endTimeHour.Text = rule.GetEndHour().ToString();
                    m_endTimeMin.Text = rule.GetEndMinute().ToString();
                }
                else
                {
                    m_ruleWhenAnytime.IsChecked = true;
                }

                SetCommandParameterFieldsForRule(rule);
            }
            else
            {
                m_ruleToUpdate = null;
                SetStatusMessage(ADD_TEXT, false);

                m_ruleToSIPAccount.IsChecked = false;
                m_ruleToChoiceAny.IsChecked = true;
                m_ruleToAccount.SelectedIndex = -1;
                m_rulePattern.Text = String.Empty;
                m_ruleCommandType.SelectedIndex = 0;
                m_ruleCommandString.Text = DEFAULT_DIAL_DESTINATION;
                m_ruleProvider.SelectedIndex = 0;
                m_ruleDescription.Text = String.Empty;
                m_rulePriority.Text = DEFAULT_RULE_PRIORITY.ToString();
                m_ruleAdvancedDialString.Text = String.Empty;
                m_ruleRingDuration.Text = String.Empty;
                m_ruleAnswerDuration.Text = String.Empty;
                m_rejectResponseCode.SelectedIndex = DEFAULT_REJECT_RESPONSE_INDEX;
                m_rejectReason.Text = String.Empty;
                m_highriseURL.Text = String.Empty;
                m_highriseToken.Text = String.Empty;

                m_ruleCommandString.Text = "${EXTEN}";

                m_ruleWhenAnytime.IsChecked = true;
                m_ruleWhenSpecificTimes.IsChecked = false;
                m_monCheckbox.IsChecked = true;
                m_tueCheckbox.IsChecked = true;
                m_wedCheckbox.IsChecked = true;
                m_thuCheckbox.IsChecked = true;
                m_friCheckbox.IsChecked = true;
                m_satCheckbox.IsChecked = true;
                m_sunCheckbox.IsChecked = true;
                m_startTimeHour.Text = "00";
                m_startTimeMin.Text = "00";
                m_endTimeHour.Text = "23";
                m_endTimeMin.Text = "59";

                HideErrorSB.Begin();
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
                    Direction = SIPCallDirection.In.ToString(), 
                    ToSIPAccount = (m_ruleToSIPAccount.IsChecked.GetValueOrDefault()) ? m_ruleToAccount.SelectedValue as string : null,
                    //RuleTypeID = Enum.Parse(typeof(SimpleWizardRuleTypes), ((TextBlock)m_ruleType.SelectedValue).Text, true).GetHashCode(),
                    Pattern = m_rulePattern.Text,
                    Command = ((TextBlock)m_ruleCommandType.SelectedValue).Text,
                    Description = m_ruleDescription.Text,
                    Priority = priority
                };

                string commandParameterError = SetRuleCommandParameters(rule);
                if (commandParameterError != null)
                {
                    SetErrorMessage(commandParameterError);
                }
                else
                {
                    string timePatternError = GetTimePattern(rule);
                    if (timePatternError != null)
                    {
                        SetErrorMessage(timePatternError);
                    }
                    else
                    {
                        string validationError = Validate(rule);
                        if (validationError != null)
                        {
                            SetErrorMessage(validationError);
                        }
                        else
                        {
                            HideErrorSB.Begin();
                            Add(rule);
                        }
                    }
                }
            }
            else
            {
                m_ruleToUpdate.ToSIPAccount = (m_ruleToSIPAccount.IsChecked.GetValueOrDefault()) ? m_ruleToAccount.SelectedValue as string : null;
                //m_ruleToUpdate.RuleTypeID = Enum.Parse(typeof(SimpleWizardRuleTypes), ((TextBlock)m_ruleType.SelectedValue).Text, true).GetHashCode();
                m_ruleToUpdate.Pattern = m_rulePattern.Text;
                m_ruleToUpdate.Command = ((TextBlock)m_ruleCommandType.SelectedValue).Text;
                m_ruleToUpdate.Description = m_ruleDescription.Text;
                m_ruleToUpdate.Priority = priority;

                string commandParameterError = SetRuleCommandParameters(m_ruleToUpdate);
                if (commandParameterError != null)
                {
                    SetErrorMessage(commandParameterError);
                }
                else
                {
                    string timePatternError = GetTimePattern(m_ruleToUpdate);
                    if (timePatternError != null)
                    {
                        SetErrorMessage(timePatternError);
                    }
                    else
                    {
                        string validationError = Validate(m_ruleToUpdate);
                        if (validationError != null)
                        {
                            SetErrorMessage(validationError);
                        }
                        else
                        {
                            HideErrorSB.Begin();
                            Update(m_ruleToUpdate);
                        }
                    }
                }
            }
		}

		private void Cancel(object sender, System.Windows.RoutedEventArgs e)
		{
            SetRuleToUpdate(null);
		}

        public void SetStatusMessage(string status, bool disableInput)
        {
            //m_ruleToAccount.IsEnabled = !disableInput;
            //m_ruleType.IsEnabled = !disableInput;
            m_rulePattern.IsEnabled = !disableInput;
            //m_ruleCommandString.IsEnabled = !disableInput;
            m_ruleDescription.IsEnabled = !disableInput;
            m_rulePriority.IsEnabled = !disableInput;
            m_ruleSaveButton.IsEnabled = !disableInput;
            m_ruleCancelButton.IsEnabled = !disableInput;

            m_descriptionText.Text = status;
        }

        public void SetErrorMessage(string errorMessage)
        {
            //m_ruleToAccount.IsEnabled = false;
            ////m_ruleType.IsEnabled = false;
            //m_rulePattern.IsEnabled = false;
            ////m_ruleCommandString.IsEnabled = false;
            //m_ruleDescription.IsEnabled = false;
            //m_rulePriority.IsEnabled = false;
            //m_ruleSaveButton.IsEnabled = false;
            //m_ruleCancelButton.IsEnabled = false;

            //m_errorCanvas.Visibility = System.Windows.Visibility.Visible;
            m_errorMessageTextBlock.Text = errorMessage;

            HideErrorSB.Stop();
        }

        private void CloseErrroMessage(object sender, System.Windows.RoutedEventArgs e)
        {
            ////m_ruleToAccount.IsEnabled = true;
            ////m_ruleType.IsEnabled = true;
            //m_rulePattern.IsEnabled = true;
            ////m_ruleCommandString.IsEnabled = true;
            //m_ruleDescription.IsEnabled = true;
            //m_rulePriority.IsEnabled = true;
            //m_ruleSaveButton.IsEnabled = true;
            //m_ruleCancelButton.IsEnabled = true;

            //m_errorCanvas.Visibility = System.Windows.Visibility.Collapsed;

            //m_descriptionText.Text = (m_ruleToUpdate != null) ? UPDATE_TEXT : ADD_TEXT;

            HideErrorSB.Begin();
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
            //   
            //}

            return null;
        }

        private void RuleCommandTypeSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
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
                        HighriseLookupSB.Stop();
                        DialCommandSB.Begin();
                        break;

                    case SimpleWizardCommandTypes.DialAdvanced:
                        DialCommandSB.Stop();
                        RejectCommandSB.Stop();
                        HighriseLookupSB.Stop();
                        AdvancedDialCommandSB.Begin();
                        break;

                    case SimpleWizardCommandTypes.Reject:
                        DialCommandSB.Stop();
                        AdvancedDialCommandSB.Stop();
                        HighriseLookupSB.Stop();
                        RejectCommandSB.Begin();
                        break;

                    case SimpleWizardCommandTypes.HighriseLookup:
                        DialCommandSB.Stop();
                        AdvancedDialCommandSB.Stop();
                        RejectCommandSB.Stop();
                        HighriseLookupSB.Begin();
                        break;
                }
            }
        }

        /// <summary>
        /// Sets the value of the rules properties based on the UI controls representing command parameters. The rule's command type
        /// dictates which input fields will be used for each command parameter.
        /// </summary>
        /// <returns>If there is an error parsing the rule parameters an error message otherwise null.</returns>
        private string SetRuleCommandParameters(SimpleWizardRule rule)
        {
            if (rule.CommandType == SimpleWizardCommandTypes.Dial)
            {
                rule.CommandParameter1 = m_ruleCommandString.Text ?? "${EXTEN}";
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
            else if (rule.CommandType == SimpleWizardCommandTypes.HighriseLookup)
            {
                rule.CommandParameter1 = m_highriseURL.Text;
                rule.CommandParameter2 = m_highriseToken.Text;
                rule.CommandParameter3 = m_recordHighriseNote.IsChecked.ToString();
            }

            return null;
        }

        /// <summary>
        /// Sets the content of the UI controls representing command parameter fields based on the specified rule. 
        /// The command parameters mean different things and apply to different controls dependent on the rule's command type.
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
            else if (rule.CommandType == SimpleWizardCommandTypes.HighriseLookup)
            {
                m_highriseURL.Text = rule.CommandParameter1;
                m_highriseToken.Text = rule.CommandParameter2;
                m_recordHighriseNote.IsChecked = (rule.CommandParameter3 != null) ? Convert.ToBoolean(rule.CommandParameter3) : false;
            }
        }

        /// <summary>
        /// Extracts the time pattern from the UI controls that represent a rule that's being applied for a specific time.
        /// </summary>
        /// <returns>A string describing the time pattern.</returns>
        private string GetTimePattern(SimpleWizardRule rule)
        {
            if (!m_ruleWhenSpecificTimes.IsChecked.GetValueOrDefault())
            {
                rule.TimePattern = null;
                return null;
            }
            else
            {
                string timePattern = null;
                timePattern += (m_monCheckbox.IsChecked.GetValueOrDefault()) ? "M" : null;
                timePattern += (m_tueCheckbox.IsChecked.GetValueOrDefault()) ? "Tu" : null;
                timePattern += (m_wedCheckbox.IsChecked.GetValueOrDefault()) ? "W" : null;
                timePattern += (m_thuCheckbox.IsChecked.GetValueOrDefault()) ? "Th" : null;
                timePattern += (m_friCheckbox.IsChecked.GetValueOrDefault()) ? "F" : null;
                timePattern += (m_satCheckbox.IsChecked.GetValueOrDefault()) ? "Sa" : null;
                timePattern += (m_sunCheckbox.IsChecked.GetValueOrDefault()) ? "Su" : null;

                if (timePattern == null)
                {
                    return "At least one day must be checked for a rule using a specific time.";
                }
                else
                {
                    int startTimeHour = 0;
                    int startTimeMin = 0;
                    int endTimeHour = 0;
                    int endTimeMin = 0;

                    if (!Int32.TryParse(m_startTimeHour.Text, out startTimeHour))
                    {
                        return "The start time hour was invalid. Please make sure it contains only numbers.";
                    }
                    if (!Int32.TryParse(m_startTimeMin.Text, out startTimeMin))
                    {
                        return "The start time minute was invalid. Please make sure it contains only numbers.";
                    }
                    if (!Int32.TryParse(m_endTimeHour.Text, out endTimeHour))
                    {
                        return "The end time hour was invalid. Please make sure it contains only numbers.";
                    }
                    if (!Int32.TryParse(m_endTimeMin.Text, out endTimeMin))
                    {
                        return "The end time minute was invalid. Please make sure it contains only numbers.";
                    }

                    if (startTimeHour < 0 || startTimeHour > 23)
                    {
                        return "The start time hour was invalid. Please make sure it is between 0 and 23.";
                    }
                    else if (startTimeMin < 0 || startTimeMin > 59)
                    {
                        return "The start time minute was invalid. Please make sure it is between 0 and 59.";
                    }
                    if (endTimeHour < 0 || endTimeHour > 23)
                    {
                        return "The end time hour was invalid. Please make sure it is between 0 and 23.";
                    }
                    else if (endTimeMin < 0 || endTimeMin > 59)
                    {
                        return "The end time minute was invalid. Please make sure it is between 0 and 59.";
                    }
                    else if ((startTimeHour * 60 + startTimeMin) >= (endTimeHour * 60 + endTimeMin))
                    {
                        return "The start time must be less than the end time.";
                    }

                    rule.TimePattern = timePattern + ";" + startTimeHour.ToString("D2") + ":" + startTimeMin.ToString("D2") + "-" + endTimeHour.ToString("D2") + ":" + endTimeMin.ToString("D2");

                    return null;
                }
            }
        }

        private void ToSIPAccount_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (m_initialised)
            {
                m_ruleToAccount.IsEnabled = true;
                m_ruleToAccount.SelectedIndex = 0;
            }
        }

        private void AnyIncomingCallChecked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (m_initialised)
            {
                m_ruleToAccount.IsEnabled = false;
                m_ruleToAccount.SelectedIndex = -1;
            }
        }

        private void AnyTimeChecked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (m_initialised)
            {
                SpecificTimeStoryboard.Begin();
            }
        }

        private void SpecificTimeChecked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (m_initialised)
            {
                SpecificTimeStoryboard.Stop();
            }
        }
	}
}