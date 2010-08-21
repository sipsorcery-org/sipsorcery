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
    public partial class NewAccountInviteControl : UserControl
    {
        public Action<string> CheckInviteCode;
        
        public NewAccountInviteControl()
        {
            InitializeComponent();
        }

        private void CheckInviteCodeButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            string inviteCode = m_inviteCodeTextBox.Text;

            if (!inviteCode.IsNullOrBlank())
            {
                m_inviteCodeErrorTextBlock.Text = "Checking invite code, please wait...";
                CheckInviteCode(m_inviteCodeTextBox.Text);
            }
            else
            {
                m_inviteCodeErrorTextBlock.Text = "Please enter an invite code.";
            }
        }

        public void InviteCodeInvalid(string reason)
        {
            if (reason.IsNullOrBlank())
            {
                m_inviteCodeErrorTextBlock.Text = "Sorry the invite code was invalid. If you believe your invite code should be valid please email admin@sipsorcery.com.";
            }
            else
            {
                m_inviteCodeErrorTextBlock.Text = reason;
            }
        }
    }
}