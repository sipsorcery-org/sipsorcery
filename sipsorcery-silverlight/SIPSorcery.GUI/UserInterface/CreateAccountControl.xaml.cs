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

namespace SIPSorcery
{
	public partial class CreateAccountControl : UserControl
	{
        private const int MAX_FIELD_LENGTH = 64;
        private const int MIN_USERNAME_LENGTH = 5;
        private const int MAX_USERNAME_LENGTH = 20;
        private const int MIN_PASSWORD_LENGTH = 6;
        private const int MAX_PASSWORD_LENGTH = 20; 
        private const int MAX_WEBSITE_FIELD_LENGTH = 256;

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
                if (m_firstNameTextBox.Text.Trim().Length == 0) {
                    m_statusTextBlock.Text = "A first name must be specified.";
                }
                else if (m_firstNameTextBox.Text.Trim().Length > MAX_FIELD_LENGTH) {
                    m_statusTextBlock.Text = "The first name length must be less than " + MAX_FIELD_LENGTH + ".";
                }
                else if (m_lastNameTextBox.Text.Trim().Length == 0) {
                    m_statusTextBlock.Text = "A last name must be specified.";
                }
                else if (m_lastNameTextBox.Text.Trim().Length > MAX_FIELD_LENGTH) {
                    m_statusTextBlock.Text = "The last name length must be less than " + MAX_FIELD_LENGTH + ".";
                }
                else if (m_emailAddressTextBox.Text.Trim().Length == 0) {
                    m_statusTextBlock.Text = "An email address must be specified.";
                }
                else if (m_emailAddressTextBox.Text.Trim().Length > MAX_FIELD_LENGTH) {
                    m_statusTextBlock.Text = "The email address length must be less than " + MAX_FIELD_LENGTH + ".";
                }
                else if (m_usernameTextBox.Text.Trim().Length == 0) {
                    m_statusTextBlock.Text = "A username must be specified.";
                }
                else if (m_usernameTextBox.Text.Trim().Length > MAX_USERNAME_LENGTH || m_usernameTextBox.Text.Trim().Length < MIN_USERNAME_LENGTH) {
                    m_statusTextBlock.Text = "The username length must be between " + MIN_USERNAME_LENGTH + " and " + MAX_USERNAME_LENGTH + ".";
                }
                else if (m_passwordTextBox.Password.Trim().Length == 0) {
                    m_statusTextBlock.Text = "A password must be specified.";
                }
                else if (m_retypePasswordTextBox.Password.Trim().Length == 0) {
                    m_statusTextBlock.Text = "The retyped password must be specified.";
                }
                else if (m_passwordTextBox.Password.Trim() != m_retypePasswordTextBox.Password.Trim()) {
                    m_statusTextBlock.Text = "The password and retyped password did not match.";
                }
                else if (m_passwordTextBox.Password.Trim().Length > MAX_PASSWORD_LENGTH || m_passwordTextBox.Password.Trim().Length < MIN_PASSWORD_LENGTH) {
                    m_statusTextBlock.Text = "The password length must be between " + MIN_PASSWORD_LENGTH + " and " + MAX_PASSWORD_LENGTH + ".";
                }
                else if (m_securityAnswerTextBox.Text.Trim().Length == 0) {
                    m_statusTextBlock.Text = "The answer to the security question must be specified.";
                }
                else if (m_securityAnswerTextBox.Text.Trim().Length > MAX_FIELD_LENGTH) {
                    m_statusTextBlock.Text = "The security question answer length must be less than " + MAX_FIELD_LENGTH + ".";
                }
                else if (m_cityTextBox.Text.Trim().Length == 0) {
                    m_statusTextBlock.Text = "Your city must be specified. If you don't live in a city please enter the one closest to you.";
                }
                else if (m_cityTextBox.Text.Trim().Length > MAX_FIELD_LENGTH) {
                    m_statusTextBlock.Text = "The city length must be less than " + MAX_FIELD_LENGTH + ".";
                }
                else if (m_webSiteTextBox.Text.Trim().Length > MAX_WEBSITE_FIELD_LENGTH) {
                    m_statusTextBlock.Text = "The web site length must be less than " + MAX_WEBSITE_FIELD_LENGTH + ".";
                }
                else {
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
                        Inserted = DateTime.Now,
                    };

                    m_username = customer.CustomerUsername;
                    m_password = customer.CustomerPassword;

                    CreateCustomer_External(customer);
                }
            }
            catch(Exception excp) {
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