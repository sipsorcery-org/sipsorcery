using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery
{
    public partial class SIPAccountDetailsControl : UserControl
    {
        private static SolidColorBrush m_infoTextBrush = AssemblyState.InfoTextBrush;
        private static SolidColorBrush m_errorTextBrush = AssemblyState.ErrorTextBrush;
        private static SolidColorBrush m_warnTextBrush = AssemblyState.WarnTextBrush;

        private SIPAccountUpdateDelegate AddSIPAccount_External;
        private SIPAccountUpdateDelegate UpdateSIPAccount_External;
        private ControlClosedDelegate Closed_External;
        private GetDialPlanNamesDelegate GetDialPlanNames_External;
        private GetSIPDomainsDelegate GetSIPDomains_External;

        private DetailsControlModesEnum m_detailsMode;
        private string m_owner;
        private SIPAccount m_sipAccount;

        public SIPAccountDetailsControl()
        {
            InitializeComponent();
        }

        public SIPAccountDetailsControl(
            DetailsControlModesEnum mode,
            SIPAccount sipAccount,
            string owner,
            SIPAccountUpdateDelegate add,
            SIPAccountUpdateDelegate update,
            ControlClosedDelegate closed,
            GetDialPlanNamesDelegate getDialPlanNames,
            GetSIPDomainsDelegate getSIPDomains)
        {
            InitializeComponent();

            m_detailsMode = mode;
            m_owner = owner;
            m_sipAccount = sipAccount;
            AddSIPAccount_External = add;
            UpdateSIPAccount_External = update;
            Closed_External = closed;
            GetDialPlanNames_External = getDialPlanNames;
            GetSIPDomains_External = getSIPDomains;

            if (m_detailsMode == DetailsControlModesEnum.Edit)
            {
                PopulateDataFields(m_sipAccount);
            }
            else
            {
                m_sipAccountUpdateButton.Content = "Add";
                m_sipAccountIdCanvas.Visibility = Visibility.Collapsed;
                m_statusDisabledRadio.Visibility = Visibility.Collapsed;
                m_statusAdminDisabledRadio.Visibility = Visibility.Collapsed;
                UIHelper.SetText(m_sipAccountOwner, m_owner);
                SetDomainNames(null);
                SetDialPlanNames(null);
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

        private void PopulateDataFields(SIPAccount sipAccount)
        {
            m_outDialPlan.Visibility = Visibility.Collapsed;
            m_inDialPlan.Visibility = Visibility.Collapsed;
            m_domainNames.Visibility = Visibility.Collapsed;
            m_sipAccountUsername.Visibility = Visibility.Collapsed;

            if (sipAccount.IsAdminDisabled)
            {
                WriteStatusMessage(MessageLevelsEnum.Warn, "SIP Account has been disabled by administrator. " + sipAccount.AdminDisabledReason);
                m_sipAccountPassword.IsEnabled = false;
                m_keepAlivesCheckBox.IsEnabled = false;
                m_statusAdminDisabledRadio.IsEnabled = true;
                m_statusAdminDisabledRadio.IsChecked = true;
                m_statusAdminDisabledRadio.IsEnabled = false;
                m_statusStandardRadio.IsEnabled = false;
                m_statusIncomingOnlyRadio.IsEnabled = false;
                m_statusDisabledRadio.IsEnabled = false;
                m_outDialPlan.IsEnabled = false;
                m_inDialPlan.IsEnabled = false;
                m_sipAccountUpdateButton.IsEnabled = false;
                m_sipAccountOutDialPlanStatus.Text = (m_sipAccount.OutDialPlanName != null) ? m_sipAccount.OutDialPlanName : "-";
                m_sipAccountInDialPlanStatus.Text = (m_sipAccount.InDialPlanName != null) ? m_sipAccount.InDialPlanName : "-";
                m_sipAccountNetworkId.IsEnabled = false;
                m_sipAccountIPAddressACL.IsEnabled = false;
                m_isSwitchboardEnabledCheckBox.IsEnabled = false;
            }
            else
            {
                if (sipAccount.Owner == m_owner)
                {
                    //ThreadPool.QueueUserWorkItem(new WaitCallback(SetDialPlanNames), null);
                    SetDialPlanNames(null);
                }
                else
                {
                    // Don't have a list of dial plan names for non-owned SIP accounts so just display the dial plan names.
                    m_sipAccountOutDialPlanStatus.Text = (m_sipAccount.OutDialPlanName != null) ? m_sipAccount.OutDialPlanName : "-";
                    m_sipAccountInDialPlanStatus.Text = (m_sipAccount.InDialPlanName != null) ? m_sipAccount.InDialPlanName : "-";
                }

                if (sipAccount.IsDisabled)
                {
                    m_statusDisabledRadio.IsChecked = true;
                }
                else if (sipAccount.IsIncomingOnly)
                {
                    m_statusIncomingOnlyRadio.IsChecked = true;
                }
            }

            m_sipAccountId.Text = sipAccount.Id.ToString();
            m_sipAccountOwner.Text = sipAccount.Owner;
            m_sipAccountUsernameText.Text = sipAccount.SIPUsername;
            m_sipAccountPassword.Text = sipAccount.SIPPassword;
            m_sipAccountDomain.Text = sipAccount.SIPDomain;
            m_keepAlivesCheckBox.IsChecked = sipAccount.SendNATKeepAlives;
            m_sipAccountNetworkId.Text = (sipAccount.NetworkId != null) ? sipAccount.NetworkId : String.Empty;
            m_sipAccountIPAddressACL.Text = (sipAccount.IPAddressACL != null) ? sipAccount.IPAddressACL : String.Empty;
            m_isSwitchboardEnabledCheckBox.IsChecked = sipAccount.IsSwitchboardEnabled;
        }

        private void SetDialPlanNames(object state)
        {
            List<string> dialPlanNames = GetDialPlanNames_External(m_owner);

            int inDialPlanSelectedIndex = -1;
            int outDialPlanSelectedIndex = -1;
            int index = 0;

            // Populate the dialplan combox boxes.
            m_inDialPlan.Items.Add(" ");    // Allows the incoming dialplan setting to be set empty to indicate bindings should be used instead of the dialplan.
            if (dialPlanNames != null)
            {
                foreach (string dialPlanName in dialPlanNames)
                {
                    m_outDialPlan.Items.Add(dialPlanName);
                    m_inDialPlan.Items.Add(dialPlanName);

                    if (m_sipAccount != null)
                    {
                        if (dialPlanName == m_sipAccount.OutDialPlanName)
                        {
                            outDialPlanSelectedIndex = index;
                        }

                        if (dialPlanName == m_sipAccount.InDialPlanName)
                        {
                            inDialPlanSelectedIndex = index + 1;
                        }
                    }

                    index++;
                }

                m_outDialPlan.SelectedIndex = outDialPlanSelectedIndex;
                m_inDialPlan.SelectedIndex = inDialPlanSelectedIndex;
            }

            UIHelper.SetVisibility(m_outDialPlan, Visibility.Visible);
            UIHelper.SetVisibility(m_inDialPlan, Visibility.Visible);
            UIHelper.SetVisibility(m_sipAccountOutDialPlanStatus, Visibility.Collapsed);
            UIHelper.SetVisibility(m_sipAccountInDialPlanStatus, Visibility.Collapsed);
        }

        private void SetDomainNames(object state)
        {
            List<string> domains = GetSIPDomains_External(m_owner);

            if (domains != null && domains.Count > 0)
            {
                foreach (string domain in domains)
                {
                    m_domainNames.Items.Add(domain);
                }
                m_domainNames.SelectedIndex = 0;

                UIHelper.SetVisibility(m_domainNames, Visibility.Visible);
                UIHelper.SetVisibility(m_sipAccountDomain, Visibility.Collapsed);
            }
            else
            {
                UIHelper.SetText(m_sipAccountDomain, "No domains were found.");
            }
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
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
            try
            {
                string username = m_sipAccountUsername.Text.Trim();
                string password = m_sipAccountPassword.Text.Trim();
                string domain = m_domainNames.SelectedItem as string;
                string outDialPlan = m_outDialPlan.SelectedItem as string;
                string inDialPlan = (m_inDialPlan.SelectedIndex != -1) ? m_inDialPlan.SelectedItem as string : null;
                string networkId = m_sipAccountNetworkId.Text.Trim();
                string ipAddressACL = m_sipAccountIPAddressACL.Text.Trim();
                bool sendKeepAlives = m_keepAlivesCheckBox.IsChecked.Value;
                bool isIncomingOnly = m_statusIncomingOnlyRadio.IsChecked.Value;
                bool isSwitchboardEnabled = m_isSwitchboardEnabledCheckBox.IsChecked.Value;

                SIPAccount sipAccount = new SIPAccount(m_owner, domain, username, password, outDialPlan);
                sipAccount.InDialPlanName = inDialPlan;
                sipAccount.SendNATKeepAlives = sendKeepAlives;
                sipAccount.IsIncomingOnly = isIncomingOnly;
                sipAccount.NetworkId = networkId;
                sipAccount.IPAddressACL = ipAddressACL;
                sipAccount.Inserted = DateTime.UtcNow;

                string validationError = SIPAccount.ValidateAndClean(sipAccount);
                if (validationError != null)
                {
                    WriteStatusMessage(MessageLevelsEnum.Warn, validationError);
                }
                else
                {
                    WriteStatusMessage(MessageLevelsEnum.Info, "Adding SIP Account please wait...");
                    AddSIPAccount_External(sipAccount);
                }
            }
            catch (Exception excp)
            {
                WriteStatusMessage(MessageLevelsEnum.Error, "Add SIPAccount Exception. " + excp.Message);
            }
        }

        private void Update()
        {
            try
            {
                m_sipAccount.SIPPassword = m_sipAccountPassword.Text;
                m_sipAccount.OutDialPlanName = (m_outDialPlan.SelectedIndex != -1) ? m_outDialPlan.SelectedItem as string : null;
                m_sipAccount.InDialPlanName = (m_inDialPlan.SelectedIndex != -1) ? m_inDialPlan.SelectedItem as string : null;
                m_sipAccount.IsIncomingOnly = m_statusIncomingOnlyRadio.IsChecked.Value;
                m_sipAccount.SendNATKeepAlives = m_keepAlivesCheckBox.IsChecked.Value;
                m_sipAccount.IsUserDisabled = m_statusDisabledRadio.IsChecked.Value;
                m_sipAccount.NetworkId = m_sipAccountNetworkId.Text.Trim();
                m_sipAccount.IPAddressACL = m_sipAccountIPAddressACL.Text.Trim();
                m_sipAccount.IsSwitchboardEnabled = m_isSwitchboardEnabledCheckBox.IsChecked.Value;

                string validationError = SIPAccount.ValidateAndClean(m_sipAccount);
                if (validationError != null)
                {
                    WriteStatusMessage(MessageLevelsEnum.Warn, validationError);
                }
                else
                {
                    WriteStatusMessage(MessageLevelsEnum.Info, "Attempting to update SIP Account " + m_sipAccount.SIPUsername + "@" + m_sipAccount.SIPDomain + " please wait...");
                    UpdateSIPAccount_External(m_sipAccount);
                }
            }
            catch (Exception excp)
            {
                WriteStatusMessage(MessageLevelsEnum.Error, "Update SIPAccount Exception. " + excp.Message);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (Closed_External != null)
            {
                Closed_External();
            }
        }

        private void IncomingCallsOnlyCheckBox_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            m_sipAccountPassword.Text = String.Empty;
            m_sipAccountPassword.IsEnabled = false;
        }

        private void IncomingCallsOnlyCheckBox_Unchecked(object sender, System.Windows.RoutedEventArgs e)
        {
            m_sipAccountPassword.IsEnabled = true;
        }
    }
}