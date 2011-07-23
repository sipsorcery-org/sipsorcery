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

namespace SIPSorcery
{
	public partial class SimpleWizardUpdateControl : UserControl
	{
        public const string ADD_TEXT = "Add New Rule";
        public const string UPDATE_TEXT = "Update Rule";
        private const int DEFAULT_RULE_PRIORITY = 99;

        public event Action<SimpleWizardDialPlanRule> Update;
        public event Action<SimpleWizardDialPlanRule> Add;

        private SimpleWizardDialPlanRule m_ruleToUpdate;        // If this is set means the control is updating an existing rule as opposed to adding a new one.

		public SimpleWizardUpdateControl()
		{
			// Required to initialize variables
			InitializeComponent();
            m_rulePriority.Text = DEFAULT_RULE_PRIORITY.ToString();
		}

		public void SetRuleToUpdate(SimpleWizardDialPlanRule rule)
        {
            if (rule != null)
            {
                m_ruleToUpdate = rule;
                SetStatusMessage(UPDATE_TEXT, false);

                m_ruleType.SelectedIndex = m_ruleType.Items.IndexOf(m_ruleType.Items.Single(x => ((TextBlock)x).Text == rule.RuleType.ToString()));
                m_rulePattern.Text = rule.Pattern;
                m_ruleDialString.Text = rule.DialString;
                m_ruleDescription.Text = rule.Description;
                m_rulePriority.Text = rule.Priority.ToString();
            }
            else
            {
                m_ruleToUpdate = null;
                SetStatusMessage(ADD_TEXT, false);

                m_ruleType.SelectedIndex = 0;
                m_rulePattern.Text = String.Empty;
                m_ruleDialString.Text = String.Empty;
                m_ruleDescription.Text = String.Empty;
                m_rulePriority.Text = DEFAULT_RULE_PRIORITY.ToString();
            }
        }

		private void Submit(object sender, System.Windows.RoutedEventArgs e)
		{
            int priority = DEFAULT_RULE_PRIORITY;
            Int32.TryParse(m_rulePriority.Text, out priority);

            if (m_ruleToUpdate == null)
            {
                SimpleWizardDialPlanRule rule = new SimpleWizardDialPlanRule()
                {
                    ID = Guid.Empty.ToString(),             // Will be set in the manager.
                    Owner = "None",                         // Will be set in the manager.
                    DialPlanID = Guid.Empty.ToString(),     // Will be set in the manager.
                    Direction = "None",                     // Will be set in the manager.
                    RuleTypeID = Enum.Parse(typeof(SimpleWizardRuleTypes), ((TextBlock)m_ruleType.SelectedValue).Text, true).GetHashCode(),
                    Pattern = m_rulePattern.Text,
                    DialString = m_ruleDialString.Text,
                    Description = m_ruleDescription.Text,
                    Priority = priority
                };

                string validationError = Validate(rule);
                if (validationError != null)
                {
                    SetErrorMessage(validationError);
                }
                else
                {
                    Add(rule);
                }
            }
            else
            {
                m_ruleToUpdate.RuleTypeID = Enum.Parse(typeof(SimpleWizardRuleTypes), ((TextBlock)m_ruleType.SelectedValue).Text, true).GetHashCode();
                m_ruleToUpdate.Pattern = m_rulePattern.Text;
                m_ruleToUpdate.DialString = m_ruleDialString.Text;
                m_ruleToUpdate.Description = m_ruleDescription.Text;
                m_ruleToUpdate.Priority = priority;

                string validationError = Validate(m_ruleToUpdate);
                if (validationError != null)
                {
                    SetErrorMessage(validationError);
                }
                else
                {
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
            m_ruleType.IsEnabled = !disableInput;
            m_rulePattern.IsEnabled = !disableInput;
            m_ruleDialString.IsEnabled = !disableInput;
            m_ruleDescription.IsEnabled = !disableInput;
            m_rulePriority.IsEnabled = !disableInput;
            m_ruleSaveButton.IsEnabled = !disableInput;
            m_ruleCancelButton.IsEnabled = !disableInput;

            m_descriptionText.Text = status;
        }

        public void SetErrorMessage(string errorMessage)
        {
            m_ruleType.IsEnabled = false;
            m_rulePattern.IsEnabled = false;
            m_ruleDialString.IsEnabled = false;
            m_ruleDescription.IsEnabled = false;
            m_rulePriority.IsEnabled = false;
            m_ruleSaveButton.IsEnabled = false;
            m_ruleCancelButton.IsEnabled = false;

            m_errorCanvas.Visibility = System.Windows.Visibility.Visible;
            m_errorMessageTextBlock.Text = errorMessage;
        }

        private void CloseErrroMessage(object sender, System.Windows.RoutedEventArgs e)
        {
            m_ruleType.IsEnabled = true;
            m_rulePattern.IsEnabled = true;
            m_ruleDialString.IsEnabled = true;
            m_ruleDescription.IsEnabled = true;
            m_rulePriority.IsEnabled = true;
            m_ruleSaveButton.IsEnabled = true;
            m_ruleCancelButton.IsEnabled = true;

            m_errorCanvas.Visibility = System.Windows.Visibility.Collapsed;

            m_descriptionText.Text = (m_ruleToUpdate != null) ? UPDATE_TEXT : ADD_TEXT;
        }

        private string Validate(SimpleWizardDialPlanRule rule)
        {
            var validationResults = new List<ValidationResult>();
            var validationContext = new ValidationContext(rule, null, null);
            Validator.TryValidateObject(rule, validationContext, validationResults);
            rule.ValidationErrors.Clear();

            if (validationResults.Count > 0)
            {
                return validationResults[0].ErrorMessage;
            }
            else
            {
                if (rule.RuleType == SimpleWizardRuleTypes.Regex)
                {
                    try
                    {
                        new Regex(rule.Pattern);
                    }
                    catch(Exception excp)
                    {
                        return "The rule pattern was not recognised as a valid regular expression. " + excp.Message;
                    }
                }

                return null;
            }
        }
	}
}