using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SIPSorcery
{
	public partial class LoginControl : UserControl
	{
        public LoginDelegate Login_External;

        public event ClickedDelegate CreateNewAccountClicked;
        public event ClickedDelegate ForgottenPasswordClicked;

		public LoginControl()
		{
			InitializeComponent();
		}

        public LoginControl(LoginDelegate loginDelegate)
        {
            InitializeComponent();
            Login_External = loginDelegate;
        }

        private void LoginButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Login();
        }

        private void Login()
        {
            string username = m_usernameTextBox.Text;
            string password = m_passwordTextBox.Password;

            if (username == null || username.Trim().Length == 0)
            {
                WriteLoginMessage("Username was empty.");
            }
            else if (password == null || password.Trim().Length == 0)
            {
                WriteLoginMessage("Password was empty.");
            }
            else
            {
                WriteLoginMessage("Attempting login...");
                Login_External(username, password);
            }
        }

        public void Clear()
        {
            UIHelper.SetText(m_usernameTextBox, String.Empty);
            UIHelper.SetText(m_passwordTextBox, String.Empty);
            UIHelper.SetText(m_errorTextBox, String.Empty);
        }

        public void WriteLoginMessage(string message)
        {
            if (message == null || message == String.Empty || message.Trim().Length == 0)
            {
                UIHelper.SetVisibility(m_errorTextBox, Visibility.Collapsed);
            }
            else
            {
                UIHelper.SetVisibility(m_errorTextBox, Visibility.Visible);
                UIHelper.SetText(m_errorTextBox, message);
            }
        }

        private void LoginTextBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Login();
            }
        }

        private void CreateNewAccount_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            if (CreateNewAccountClicked != null) {
                CreateNewAccountClicked();
            }
        }

        private void ForgottenPassword_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            if (ForgottenPasswordClicked != null) {
                ForgottenPasswordClicked();
            }
        }
	}
}