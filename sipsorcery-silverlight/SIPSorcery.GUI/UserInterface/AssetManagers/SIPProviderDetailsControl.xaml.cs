using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace SIPSorcery
{
	public partial class SIPProviderDetailsControl : UserControl
	{
        private static char m_customHeadersSeparator = SIPProvider.CUSTOM_HEADERS_SEPARATOR;
        private static int m_defaultRegisterExpiry = SIPProvider.REGISTER_DEFAULT_EXPIRY;
        private static int m_minimumRegisterExpiry = SIPProvider.REGISTER_MINIMUM_EXPIRY;
        private static int m_maximumRegisterExpiry = SIPProvider.REGISTER_MAXIMUM_EXPIRY;

        private static Color m_infoTextColour = AssemblyState.InfoTextColour;
        private static Color m_warnTextColour = AssemblyState.WarnTextColour;
        private static Color m_errorTextColour = AssemblyState.ErrorTextColour;

        private SIPProviderUpdateDelegate SIPProviderAdd_External;
        private SIPProviderUpdateDelegate SIPProviderUpdate_External;
        private ControlClosedDelegate ControlClosed_External;

        private DetailsControlModesEnum m_detailsMode;
        private SIPProvider m_sipProvider;
        private string m_owner;

		public SIPProviderDetailsControl()
		{
			InitializeComponent();
		}

        public SIPProviderDetailsControl(
            DetailsControlModesEnum mode, 
            SIPProvider sipProvider, 
            string owner, 
            SIPProviderUpdateDelegate sipProviderAdd,
            SIPProviderUpdateDelegate sipProviderUpdate, 
            ControlClosedDelegate closed)
        {
            InitializeComponent();

            m_advancedProviderSettings.Visibility = Visibility.Collapsed;
            m_detailsMode = mode;
            m_sipProvider = sipProvider;
            m_owner = owner;
            SIPProviderAdd_External = sipProviderAdd;
            SIPProviderUpdate_External = sipProviderUpdate;
            ControlClosed_External = closed;

            if (mode == DetailsControlModesEnum.Edit)
            {
                m_applyButton.Content = "Update";
                PopulateDataFields(m_sipProvider);
            }
            else
            {
                m_providerIdCanvas.Visibility = Visibility.Collapsed;
                m_applyButton.Content = "Add";
            }
        }

        public void WriteStatusMessage(MessageLevelsEnum status, string message)
        {
            if (status == MessageLevelsEnum.Error)
            {
                UIHelper.SetColouredText(m_statusTextBlock, message, m_errorTextColour);
            }
            else if (status == MessageLevelsEnum.Warn)
            {
                UIHelper.SetColouredText(m_statusTextBlock, message, m_warnTextColour);
            }
            else
            {
                UIHelper.SetColouredText(m_statusTextBlock, message, m_infoTextColour);
            }
        }

        private void PopulateDataFields(SIPProvider sipProvider)
        {
            m_providerId.Text = sipProvider.Id;
            m_providerName.Text = sipProvider.ProviderName;
            m_providerUsername.Text = sipProvider.ProviderUsername;
            m_providerPassword.Text = sipProvider.ProviderPassword;
            m_providerServer.Text = sipProvider.ProviderServer.ToString();
            m_providerRegister.IsChecked = sipProvider.RegisterEnabled;
            m_providerRegisterContact.Text = (sipProvider.RegisterContact != null) ? sipProvider.RegisterContact.ToString() : String.Empty;
            m_providerOutboundProxy.Text = (sipProvider.ProviderOutboundProxy != null) ? sipProvider.ProviderOutboundProxy : String.Empty;
            m_providerAuthUsername.Text = (sipProvider.ProviderAuthUsername != null) ? sipProvider.ProviderAuthUsername : String.Empty;
            m_providerFromHeader.Text = (sipProvider.ProviderFrom != null) ? sipProvider.ProviderFrom : String.Empty;
            m_providerRegisterExpiry.Text = sipProvider.RegisterExpiry.ToString();
            m_providerRegisterRealm.Text = (sipProvider.RegisterRealm != null) ? sipProvider.RegisterRealm : String.Empty;
            m_providerRegisterServer.Text = (sipProvider.RegisterServer != null) ? sipProvider.RegisterServer.ToString() : String.Empty;

            m_providerRegisterContact.IsEnabled = m_providerRegister.IsChecked.Value;
            m_providerRegisterExpiry.IsEnabled = m_providerRegister.IsChecked.Value;
            m_providerRegisterRealm.IsEnabled = m_providerRegister.IsChecked.Value;
            m_providerRegisterServer.IsEnabled = m_providerRegister.IsChecked.Value;

            if (sipProvider.CustomHeaders != null && sipProvider.CustomHeaders.Trim().Length > 0)
            {
                string[] customHeaders = sipProvider.CustomHeaders.Split(m_customHeadersSeparator);

                if (customHeaders != null && customHeaders.Length > 0)
                {
                    foreach (string customHeader in customHeaders)
                    {
                        if (customHeader != null && customHeader.Trim().Length > 0)
                        {
                            m_providerCustomHeaders.Items.Add(customHeader.Trim());
                        }
                    }
                }
            }
        }

        private void ApplyButtonClicked(object sender, System.Windows.RoutedEventArgs e)
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
                bool valid = false;
                string validationError = "Unknown validation error.";

                string providerName = m_providerName.Text.Trim();
                string providerUsername = m_providerUsername.Text.Trim();
                string providerPassword = m_providerPassword.Text.Trim();
                SIPURI providerServer = (m_providerServer.Text != null && m_providerServer.Text.Trim().Length > 0) ? SIPURI.ParseSIPURIRelaxed(m_providerServer.Text) : null;
                bool registerEnabled = m_providerRegister.IsChecked.Value;
                SIPURI registerContact = (m_providerRegisterContact.Text != null && m_providerRegisterContact.Text.Trim().Length >0) ? SIPURI.ParseSIPURI(m_providerRegisterContact.Text) : null;
                string outboundProxy = m_providerOutboundProxy.Text;
                string authUsername = m_providerAuthUsername.Text;
                string providerFrom = m_providerFromHeader.Text;
                string registerRealm = m_providerRegisterRealm.Text;
                SIPURI registerServer = (m_providerRegisterServer.Text != null && m_providerRegisterServer.Text.Trim().Length > 0) ? SIPURI.ParseSIPURIRelaxed(m_providerRegisterServer.Text) : null;

                int registerExpiry = m_defaultRegisterExpiry;
                Int32.TryParse(m_providerRegisterExpiry.Text, out registerExpiry);

                string customHeaders = null;
                if (m_providerCustomHeaders.Items.Count > 0)
                {
                    foreach (string customHeader in m_providerCustomHeaders.Items)
                    {
                        customHeaders += (m_sipProvider.CustomHeaders != null && m_sipProvider.CustomHeaders.Trim().Length > 0) ? m_customHeadersSeparator.ToString() : null;
                        customHeaders += customHeader;
                    }
                }

                if (providerName == null || providerName.Trim().Length == 0)
                {
                    valid = false;
                    validationError = "A value for Provider Name must be specified.";
                }
                else if (providerServer == null)
                {
                    valid = false;
                    validationError = "A value for Server must be specified.";
                }
                else if (m_providerRegisterServer.Text != null && m_providerRegisterServer.Text.Trim().Length > 0 && registerServer == null)
                {
                    valid = false;
                    validationError = "The Register Server could not be understood.";
                }
                else if (registerEnabled && registerContact == null)
                {
                    validationError = "A valid contact must be supplied to enable a provider registration.";
                }
                else
                {
                    valid = true;
                }

                if (valid)
                {
                    SIPProvider sipProvider = new SIPProvider(m_owner, providerName, providerUsername, providerPassword, providerServer, outboundProxy, providerFrom, customHeaders,
                        registerContact, registerExpiry, registerServer, authUsername, registerRealm, registerEnabled, true);
                    sipProvider.Inserted = DateTime.Now;
                    sipProvider.LastUpdate = DateTime.Now;

                    WriteStatusMessage(MessageLevelsEnum.Info, "Adding SIP Provider please wait...");
                    SIPProviderAdd_External(sipProvider);
                }
                else
                {
                    WriteStatusMessage(MessageLevelsEnum.Warn, validationError);
                }
            }
            catch (Exception excp)
            {
                WriteStatusMessage(MessageLevelsEnum.Error, "Add Exception. " + excp.Message);
            }
        }
        
        private void Update()
        {
            try
            {
                bool valid = false;
                string validationError = "Unknown validation error.";

                m_sipProvider.ProviderName = m_providerName.Text;
                m_sipProvider.ProviderUsername = m_providerUsername.Text;
                m_sipProvider.ProviderPassword = m_providerPassword.Text;
                m_sipProvider.ProviderServer = (m_providerServer.Text != null && m_providerServer.Text.Trim().Length > 0) ? SIPURI.ParseSIPURIRelaxed(m_providerServer.Text).ToString() : null;
                m_sipProvider.RegisterEnabled = m_providerRegister.IsChecked.Value;
                m_sipProvider.RegisterContact = (m_providerRegisterContact.Text != null && m_providerRegisterContact.Text.Trim().Length > 0) ? SIPURI.ParseSIPURI(m_providerRegisterContact.Text).ToString() : null;
                m_sipProvider.ProviderOutboundProxy = m_providerOutboundProxy.Text;
                m_sipProvider.ProviderAuthUsername = m_providerAuthUsername.Text;
                m_sipProvider.ProviderFrom = m_providerFromHeader.Text;
                m_sipProvider.RegisterRealm = m_providerRegisterRealm.Text;
                m_sipProvider.RegisterServer = (m_providerRegisterServer.Text != null && m_providerRegisterServer.Text.Trim().Length > 0) ? SIPURI.ParseSIPURIRelaxed(m_providerRegisterServer.Text).ToString() : null;

                int registerExpiry = m_defaultRegisterExpiry;
                if (Int32.TryParse(m_providerRegisterExpiry.Text, out registerExpiry))
                {
                    m_sipProvider.RegisterExpiry = registerExpiry;
                }

                m_sipProvider.CustomHeaders = null;
                if (m_providerCustomHeaders.Items.Count > 0)
                {
                    foreach (string customHeader in m_providerCustomHeaders.Items)
                    {
                        m_sipProvider.CustomHeaders += (m_sipProvider.CustomHeaders != null && m_sipProvider.CustomHeaders.Trim().Length > 0) ? m_customHeadersSeparator.ToString() : null;
                        m_sipProvider.CustomHeaders += customHeader;
                    }
                }

                if (m_sipProvider.ProviderName == null || m_sipProvider.ProviderName.Trim().Length == 0)
                {
                    validationError = "A value for Provider Name must be specified.";
                }
                else if (m_sipProvider.ProviderServer == null)
                {
                    validationError = "A value for Server must be specified.";
                }
                else if (m_sipProvider.RegisterExpiry < m_minimumRegisterExpiry || m_sipProvider.RegisterExpiry > m_maximumRegisterExpiry)
                {
                    validationError = "The registration expiry must be between " + m_minimumRegisterExpiry + " and " + m_maximumRegisterExpiry + " seconds.";
                }
                else if (m_sipProvider.RegisterEnabled && m_sipProvider.RegisterContact == null)
                {
                    validationError = "A valid contact must be supplied to enable a provider registration.";
                }
                else
                {
                    valid = true;
                }

                if (valid)
                {
                    if (m_sipProvider.RegisterEnabled && m_sipProvider.RegisterAdminEnabled) {
                        m_sipProvider.RegisterDisabledReason = null;
                    }

                    WriteStatusMessage(MessageLevelsEnum.Info, "Updating SIP Provider please wait...");
                    SIPProviderUpdate_External(m_sipProvider);
                }
                else
                {
                    WriteStatusMessage(MessageLevelsEnum.Warn, validationError);
                }
            }
            catch (Exception excp)
            {
                WriteStatusMessage(MessageLevelsEnum.Error, "Update Exception. " + excp.Message);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            ControlClosed_External();
        }

        private void AdvancedSettings_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (m_advancedProviderSettings.Visibility != Visibility.Visible)
            {
                m_advancedSettingsMenu.Text = "hide advanced settings";
                m_advancedProviderSettings.Visibility = Visibility.Visible;
            }
            else
            {
                m_advancedSettingsMenu.Text = "show advanced settings";
                m_advancedProviderSettings.Visibility = Visibility.Collapsed;
            }
        }

        private void AddCustomHeader_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            m_providerCustomHeaders.Items.Add(m_providerCustom.Text);
            m_providerCustom.Text = String.Empty;
        }

        private void CustomHeaders_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (m_providerCustomHeaders.SelectedItem != null)
            {
                m_providerCustom.Text = m_providerCustomHeaders.SelectedItem as string;
                m_providerCustomHeaders.Items.Remove(m_providerCustomHeaders.SelectedItem);
            }
        }

        private void ProviderRegister_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            m_providerRegisterContact.IsEnabled = true;
            m_providerRegisterExpiry.IsEnabled = true;
            m_providerRegisterRealm.IsEnabled = true;
            m_providerRegisterServer.IsEnabled = true;

            if (m_providerRegisterExpiry.Text.Trim().Length == 0)
            {
                m_providerRegisterExpiry.Text = m_defaultRegisterExpiry.ToString();
            }
        }

        private void ProviderRegister_Unchecked(object sender, System.Windows.RoutedEventArgs e)
        {
            m_providerRegisterContact.IsEnabled = false;
            m_providerRegisterExpiry.IsEnabled = false;
            m_providerRegisterRealm.IsEnabled = false;
            m_providerRegisterServer.IsEnabled = false;
        }
	}
}