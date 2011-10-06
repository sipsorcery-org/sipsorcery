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

namespace SIPSorcery
{
	public partial class SimpleWizardCRMControl : UserControl
	{
        public const string ADD_TEXT = "Add Account";
        public const string UPDATE_TEXT = "Update Account";

        public event Action<CRMAccount> Update;
        public event Action<CRMAccount> Add;
        public event Action<CRMAccount> Delete;
        public event Action Closed;

        private CRMAccount m_crmAccount;        // If this is set means the control is updating an existing CRM Account as opposed to adding a new one.

		public SimpleWizardCRMControl()
		{
			// Required to initialize variables
			InitializeComponent();
		}

		public void SetCRMAccount(CRMAccount crmAccount)
        {
            if (crmAccount != null)
            {
                m_crmAccount = crmAccount;
                SetStatusMessage(UPDATE_TEXT, false);

                m_crmType.SelectedIndex = m_crmType.Items.IndexOf(m_crmType.Items.Single(x => ((TextBlock)x).Text == crmAccount.CRMAccountType.ToString()));
                m_crmURL.Text = crmAccount.URL;
                m_crmUsername.Text = crmAccount.Username;
                m_crmPassword.Text = crmAccount.Password;

                m_deleteButton.IsEnabled = true;
            }
            else
            {
                m_crmAccount = null;
                SetStatusMessage(ADD_TEXT, false);

                m_crmURL.Text = String.Empty;
                m_crmUsername.Text = String.Empty;
                m_crmPassword.Text = String.Empty;

                m_deleteButton.IsEnabled = false;
            }
        }

		private void Submit(object sender, System.Windows.RoutedEventArgs e)
		{
            if (m_crmAccount == null)
            {
                CRMAccount crmAccount = new CRMAccount()
                {
                    ID = Guid.Empty.ToString(),             // Will be set in the manager.
                    Owner = "None",                         // Will be set in the manager.
                    CRMTypeID = Enum.Parse(typeof(CRMAccountTypes), ((TextBlock)m_crmType.SelectedValue).Text, true).GetHashCode(),
                    URL = m_crmURL.Text,
                    Username = m_crmUsername.Text,
                    Password = m_crmPassword.Text,
                };

                string validationError = Validate(crmAccount);
                if (validationError != null)
                {
                    SetErrorMessage(validationError);
                }
                else
                {
                    Add(crmAccount);
                }
            }
            else
            {
                m_crmAccount.CRMTypeID = Enum.Parse(typeof(CRMAccountTypes), ((TextBlock)m_crmType.SelectedValue).Text, true).GetHashCode();
                m_crmAccount.URL = m_crmURL.Text;
                m_crmAccount.Username = m_crmUsername.Text;
                m_crmAccount.Password = m_crmPassword.Text;

                string validationError = Validate(m_crmAccount);
                if (validationError != null)
                {
                    SetErrorMessage(validationError);
                }
                else
                {
                    Update(m_crmAccount);
                }
            }
		}

		private void Cancel(object sender, System.Windows.RoutedEventArgs e)
		{
            SetCRMAccount(null);
		}

        public void SetStatusMessage(string status, bool disableInput)
        {
            m_crmType.IsEnabled = !disableInput;
            m_crmURL.IsEnabled = !disableInput;
            m_crmUsername.IsEnabled = !disableInput;
            m_crmPassword.IsEnabled = !disableInput;
            m_ruleSaveButton.IsEnabled = !disableInput;
            m_deleteButton.IsEnabled = !disableInput;

            m_descriptionText.Text = status;
        }

        public void SetErrorMessage(string errorMessage)
        {
            m_crmType.IsEnabled = false;
            m_crmURL.IsEnabled = false;
            m_crmUsername.IsEnabled = false;
            m_crmPassword.IsEnabled = false;
            m_ruleSaveButton.IsEnabled = false;
            m_deleteButton.IsEnabled = false;

            m_errorCanvas.Visibility = System.Windows.Visibility.Visible;
            m_errorMessageTextBlock.Text = errorMessage;
        }

        private void CloseErrroMessage(object sender, System.Windows.RoutedEventArgs e)
        {
            m_crmType.IsEnabled = true;
            m_crmURL.IsEnabled = true;
            m_crmUsername.IsEnabled = true;
            m_crmPassword.IsEnabled = true;
            m_ruleSaveButton.IsEnabled = true;
            m_deleteButton.IsEnabled = (m_crmAccount != null);

            m_errorCanvas.Visibility = System.Windows.Visibility.Collapsed;

            m_descriptionText.Text = (m_crmAccount != null) ? UPDATE_TEXT : ADD_TEXT;
        }

        private string Validate(CRMAccount crmAccount)
        {
            var validationResults = new List<ValidationResult>();
            var validationContext = new ValidationContext(crmAccount, null, null);
            Validator.TryValidateObject(crmAccount, validationContext, validationResults);
            crmAccount.ValidationErrors.Clear();

            if (validationResults.Count > 0)
            {
                return validationResults[0].ErrorMessage;
            }
            else
            {
                // Do any other client side validation required.

                return null;
            }
        }

        private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Closed();
        }

        private void Delete_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (m_crmAccount != null)
            {
                Delete(m_crmAccount);
            }
        }
	}
}