using System;
using System.Windows;
using System.Windows.Browser;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SIPSorcery.CRM;
using SIPSorcery.Sys;

namespace SIPSorcery
{
	public partial class CreateAccountControl : UserControl
	{
        public CreateCustomerDelegate CreateCustomer_External;
        public LoginDelegate Login_External;

        // Used when a new customer account is created to automatically sign the user on.
        private string m_username;
        private string m_password;

		public CreateAccountControl()
		{
			InitializeComponent();
		}

        private void CreateCustomerButton_Click(object sender, System.Windows.RoutedEventArgs e) {
            try {
                m_statusTextBlock.Text = String.Empty;

                Customer customer = new Customer() {
                    Id = Guid.NewGuid().ToString(),
                    FirstName = m_firstNameTextBox.Text.Trim(),
                    LastName = m_lastNameTextBox.Text.Trim(),
                    EmailAddress = m_emailAddressTextBox.Text.Trim(),
                    CustomerUsername = m_usernameTextBox.Text.Trim(),
                    CustomerPassword = m_passwordTextBox.Password.Trim(),
                    SecurityQuestion = ((TextBlock)m_securityQuestionListBox.SelectedItem).Text,
                    SecurityAnswer = m_securityAnswerTextBox.Text.Trim(),
                    City = m_cityTextBox.Text.Trim(),
                    Country = ((TextBlock)m_countryListBox.SelectedItem).Text,
                    WebSite = m_webSiteTextBox.Text.Trim(),
                    InsertedUTC = DateTime.Now.ToUniversalTime()
                };

                string validationError = Customer.ValidateAndClean(customer);
                if (validationError != null) {
                    m_statusTextBlock.Text = validationError;
                }
                else {

                    if (m_retypePasswordTextBox.Password.IsNullOrBlank()) {
                        m_statusTextBlock.Text = "The retyped password must be specified.";
                    }
                    else if (m_retypePasswordTextBox.Password.Trim() != customer.CustomerPassword.Trim()) {
                        m_statusTextBlock.Text = "The password and retyped password did not match.";
                    }
                    else {
                        CreateCustomer_External(customer);

                        m_username = customer.CustomerUsername;
                        m_password = customer.CustomerPassword;
                    }
                }
            }
            catch (Exception excp) {
                m_statusTextBlock.Text = "Exception creating customer. " + excp.Message;
            }
        }

        public void CustomerCreated(System.ComponentModel.AsyncCompletedEventArgs e) {
            if (e.Error == null) {
                m_statusTextBlock.Text = "Customer successfully created.";
                Clear();
                Login_External(m_username, m_password);
            }
            else {
                m_statusTextBlock.Text = e.Error.Message;
            }
        }

        public void Clear() {
            UIHelper.SetText(m_statusTextBlock, String.Empty);
            UIHelper.SetText(m_firstNameTextBox, String.Empty);
            UIHelper.SetText(m_lastNameTextBox, String.Empty);
            UIHelper.SetText(m_emailAddressTextBox, String.Empty);
            UIHelper.SetText(m_usernameTextBox, String.Empty);
            UIHelper.SetText(m_passwordTextBox, String.Empty);
            UIHelper.SetText(m_retypePasswordTextBox, String.Empty);
            UIHelper.SetText(m_securityAnswerTextBox, String.Empty);
            UIHelper.SetText(m_cityTextBox, String.Empty);
            UIHelper.SetComboBoxSelectedId(m_securityQuestionListBox, 0);
            UIHelper.SetComboBoxSelectedId(m_countryListBox, 14);
        }
	}
}