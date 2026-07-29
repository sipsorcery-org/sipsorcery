//-----------------------------------------------------------------------------
// Filename: Softphone.xaml.cs
//
// Description: The user interface for the softphone. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//  
// History:
// 11 Mar 2012	Aaron Clauson	Refactored, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.SoftPhone.Signalling;
using SIPSorcery.SoftPhone.UI;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.SoftPhone
{
    public partial class SoftPhone : Window
    {
        private sealed class ClientVideoState
        {
            public required Image Image { get; init; }

            public WriteableBitmap Bitmap { get; set; }
        }

        private const int SIP_CLIENT_COUNT = 2;                             // The number of SIP clients (simultaneous calls) that the UI can handle.
        private const int ZINDEX_TOP = 10;
        private const int REGISTRATION_EXPIRY = 180;

        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.LogFactory.CreateLogger<SoftPhone>();
        private string _logPath = string.Empty;


        private string m_sipUsername = SIPSoftPhoneState.Settings.SIPUsername;
        private string m_sipPassword = SIPSoftPhoneState.Settings.SIPPassword;
        private string m_sipServer = SIPSoftPhoneState.Settings.SIPServer;
        private SoftphoneVideoSourcesEnum m_videoSource = SIPSoftPhoneState.Settings.VideoSource;
        private SIPClient currentRttClient = null;
        private int lastRemoteRttMsgIndex = 0;
        private List<(DateTime Time, string Message)> statusHistory = new();

        private SIPTransportManager _sipTransportManager;
        private List<SIPClient> _sipClients;
        private SoftphoneSTUNClient _stunClient;                    // STUN client to periodically check the public IP address.
        private SIPRegistrationUserAgent _sipRegistrationClient;    // Can be used to register with an external SIP provider if incoming calls are required.

        private ClientVideoState[] _clientVideoStates;

        public SoftPhone()
        {
            InitializeComponent();

            InitLogger();

            //if(!m_useAudioScope)
            //{
            //    _audioScope0Border.Visibility = Visibility.Collapsed;
            //    //OpenGLDraw = "AudioScopeDraw0" OpenGLInitialized = "AudioScopeInitialized0"
            //    AudioScope0.IsEnabled = false;
            //    AudioScope0.Visibility = Visibility.Hidden;
            //}

            _clientVideoStates =
            [
                new ClientVideoState { Image = _client0Video },
                new ClientVideoState { Image = _client1Video }
            ];

            // Do some UI initialization.
            ResetToCallStartState(null);

            _sipTransportManager = new SIPTransportManager();
            _sipTransportManager.IncomingCall += SIPCallIncoming;

            _sipClients = new List<SIPClient>();

            // If a STUN server hostname has been specified start the STUN client to lookup and periodically 
            // update the public IP address of the host machine.
            if (!SIPSoftPhoneState.Settings.STUNServerHostname.IsNullOrBlank())
            {
                _stunClient = new SoftphoneSTUNClient(SIPSoftPhoneState.Settings.STUNServerHostname);
                _stunClient.PublicIPAddressDetected += (ip) =>
                {
                    SIPSoftPhoneState.PublicIPAddress = ip;
                };
                _stunClient.Run();
            }

            DataObject.AddPastingHandler(_rttOutgoingBox, _rttOutgoingBox_OnPaste);
        }

        private void InitLogger()
        {
            if (SIPSoftPhoneState.Settings.EnableLog)
            {
                _logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SIPSorcery", $"{SIPSoftPhoneState.Settings.SIPFromName}.log");

                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .Enrich.FromLogContext()
                    .WriteTo.Debug()
                    .WriteTo.Console()
                    .WriteTo.File(_logPath)
                    .CreateLogger();

                var factory = new Serilog.Extensions.Logging.SerilogLoggerFactory(Log.Logger);
                SIPSorcery.LogFactory.Set(factory);
            }
        }

        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            await Initialize();
            InitializeUi();
        }

        private void InitializeUi()
        {
            _uriEntryDropDown.ItemsSource = SIPSoftPhoneState.Settings.QuickDialEntries;
            _uriEntry2DropDown.ItemsSource = SIPSoftPhoneState.Settings.QuickDialEntries;
        }

        /// <summary>
        /// Initialises the SIP clients and transport.
        /// </summary>
        private async Task Initialize()
        {
            await _sipTransportManager.InitialiseSIP();

            for (int i = 0; i < SIP_CLIENT_COUNT; i++)
            {
                var sipClient = new SIPClient(_sipTransportManager.SIPTransport, SIPSoftPhoneState.Settings.VideoSource);

                sipClient.CallAnswer += SIPCallAnswered;
                sipClient.CallEnded += ResetToCallStartState;
                sipClient.StatusMessage += (client, message) => { SetStatusText(m_signallingStatus, message); };
                sipClient.RemotePutOnHold += RemotePutOnHold;
                sipClient.RemoteTookOffHold += RemoteTookOffHold;
                sipClient.TextReceived += TextReceived;

                _sipClients.Add(sipClient);
            }

            string listeningEndPoints = null;

            foreach (var sipChannel in _sipTransportManager.SIPTransport.GetSIPChannels())
            {
                SIPEndPoint sipChannelEP = sipChannel.ListeningSIPEndPoint.CopyOf();
                sipChannelEP.ChannelID = null;
                listeningEndPoints += (listeningEndPoints == null) ? sipChannelEP.ToString() : $", {sipChannelEP}";
            }

            listeningEndPoint.Content = $"Listening on: {listeningEndPoints}";
            string uri = $"sip:{m_sipUsername}@{m_sipServer}";
            Title += $" - {uri}";


            _sipRegistrationClient = new SIPRegistrationUserAgent(
                _sipTransportManager.SIPTransport,
                m_sipUsername,
                m_sipPassword,
                m_sipServer,
                SIPSoftPhoneState.Settings.RegisterExpiry ?? REGISTRATION_EXPIRY,
                sendUsernameInContactHeader: true,
                registerFailureRetryInterval: SIPSoftPhoneState.Settings.RegisterRetryInSeconds);

            _sipRegistrationClient.RegistrationSuccessful += (uri, response) => UpdateServerRegistrationState("✅", uri.ToParameterlessString(), response);

            Action<SIPURI, SIPResponse, string> registrationFailDelegate = (uri, response, error) => UpdateServerRegistrationState("❌", uri.ToParameterlessString(), response, error);
            _sipRegistrationClient.RegistrationTemporaryFailure += registrationFailDelegate;
            _sipRegistrationClient.RegistrationFailed += registrationFailDelegate;

            _sipRegistrationClient.Start();

            if (!string.IsNullOrEmpty(m_sipUsername) && !string.IsNullOrEmpty(m_sipServer))
            {
                UpdateServerRegistrationState("🔄️", uri, null, null);
            }
            else
            {
                UpdateServerRegistrationState("⚪", uri, null, "no sip registration data");
            }
        }

        private async void UpdateServerRegistrationState(string display, string uri, SIPResponse response, string error = null)
        {
            Dispatcher.DoOnUIThread(() =>
            {
                string state = error == null ? response == null ? "requested ..." : "successful" : $"FAILED: {error}";

                _registrationState.Content = display;
                _registrationState.ToolTip = $"Registration of '{uri}' on server {state}" +
                    $"{(response == null ? string.Empty : $"{Environment.NewLine}{response}")}";
            });

            if (error != null)
            {
                // dirty delay without cancellation, to wait approximately as long as real retry waits
                await Task.Delay(SIPSoftPhoneState.Settings.RegisterRetryInSeconds * 1000);

                string currentDisplay = string.Empty;
                Dispatcher.DoOnUIThread(() => currentDisplay = _registrationState.Content.ToString());

                // as this was a dirty wait above without observing a cancellation token,
                // check if a state change occurred meanwhile, which means this update is obsolete
                if (display == currentDisplay)
                {
                    UpdateServerRegistrationState("🔄️", uri, null, null);
                }
            }
        }

        /// <summary>
        /// Application closing, shutdown the SIP and STUN clients.
        /// </summary>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            foreach (var sipClient in _sipClients)
            {
                sipClient.Shutdown();
            }

            _sipTransportManager.Shutdown();
            _stunClient?.Stop();
        }

        /// <summary>
        /// Reset the UI elements to their initial state at the end of a call.
        /// </summary>
        private void ResetToCallStartState(SIPClient sipClient)
        {
            if (sipClient == null || sipClient == _sipClients[0])
            {
                Dispatcher.DoOnUIThread(() =>
                {
                    m_callButton.Visibility = Visibility.Visible;
                    m_cancelButton.Visibility = Visibility.Collapsed;
                    m_byeButton.Visibility = Visibility.Collapsed;
                    m_answerButton.Visibility = Visibility.Collapsed;
                    m_rejectButton.Visibility = Visibility.Collapsed;
                    m_redirectButton.Visibility = Visibility.Collapsed;
                    m_transferButton.Visibility = Visibility.Collapsed;
                    m_holdButton.Visibility = Visibility.Collapsed;
                    m_offHoldButton.Visibility = Visibility.Collapsed;
                    _client0Video.Visibility = Visibility.Collapsed;
                    SetStatusText(m_signallingStatus, "Ready");
                    _uriEntryDropDown.IsEnabled = true;
                    _protocolSelection.IsEnabled = true;

                    if (_sipClients?.Count > 0 && _sipClients[0] != null)
                    {
                        _sipClients[0].OnRemoteVideo -= OnClientZeroVideoSinkSample;
                        _sipClients[0].OnAudioScopeFrame -= OnClientZeroAudioScopeFrame;
                    }
                });
            }

            if (sipClient == null || sipClient == _sipClients[1])
            {
                Dispatcher.DoOnUIThread(() =>
                {
                    m_call2Button.Visibility = Visibility.Visible;
                    m_cancel2Button.Visibility = Visibility.Collapsed;
                    m_bye2Button.Visibility = Visibility.Collapsed;
                    m_answer2Button.Visibility = Visibility.Collapsed;
                    m_reject2Button.Visibility = Visibility.Collapsed;
                    m_redirect2Button.Visibility = Visibility.Collapsed;
                    m_transfer2Button.Visibility = Visibility.Collapsed;
                    m_hold2Button.Visibility = Visibility.Collapsed;
                    m_offHold2Button.Visibility = Visibility.Collapsed;
                    m_attendedTransferButton.Visibility = Visibility.Collapsed;
                    _client1Video.Visibility = Visibility.Collapsed;
                    SetStatusText(m_signallingStatus, "Ready");
                    _uriEntry2DropDown.IsEnabled = true;
                    _protocolSelection2.IsEnabled = true;
                });
            }

            Dispatcher.DoOnUIThread(() =>
            {
                _videoArea.Visibility = _client0Video.Visibility == Visibility.Visible || _client1Video.Visibility == Visibility.Visible
                    ? Visibility.Visible : Visibility.Collapsed;
            });

            UpdateRttSendingState();

            if (_sipClients?.Count > 1 && _sipClients[1] != null)
            {
                _sipClients[1].OnRemoteVideo -= OnClientOneVideoSinkSample;
                _sipClients[1].OnAudioScopeFrame -= OnClientOneAudioScopeFrame;
            }
        }

        /// <summary>
        /// Checks if there is a client that can accept the call and if so sets up the UI
        /// to present the handling options to the user.
        /// </summary>
        private bool SIPCallIncoming(SIPRequest sipRequest)
        {
            SetStatusText(m_signallingStatus, $"Incoming call from {sipRequest.Header.From.FriendlyDescription()}.");

            if (!_sipClients[0].IsCallActive)
            {
                _sipClients[0].Accept(sipRequest);

                Dispatcher.DoOnUIThread(() =>
                {
                    m_callButton.Visibility = Visibility.Collapsed;
                    m_cancelButton.Visibility = Visibility.Collapsed;
                    m_byeButton.Visibility = Visibility.Collapsed;

                    m_answerButton.Visibility = Visibility.Visible;
                    m_rejectButton.Visibility = Visibility.Visible;
                    m_redirectButton.Visibility = Visibility.Visible;
                });

                return true;
            }
            else if (!_sipClients[1].IsCallActive)
            {
                _sipClients[1].Accept(sipRequest);

                Dispatcher.DoOnUIThread(() =>
                {
                    m_call2Button.Visibility = Visibility.Collapsed;
                    m_cancel2Button.Visibility = Visibility.Collapsed;
                    m_bye2Button.Visibility = Visibility.Collapsed;

                    m_answer2Button.Visibility = Visibility.Visible;
                    m_reject2Button.Visibility = Visibility.Visible;
                    m_redirect2Button.Visibility = Visibility.Visible;
                });

                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Set up the UI to present options for an established SIP call, i.e. hide the cancel 
        /// button and display they hangup button.
        /// </summary>
        private async void SIPCallAnswered(SIPClient client)
        {
            if (client == _sipClients[0])
            {
                if (_sipClients[1].IsCallActive && !_sipClients[1].IsOnHold)
                {
                    await _sipClients[1].PutOnHold();
                }

                Dispatcher.DoOnUIThread(() =>
                {
                    m_answerButton.Visibility = Visibility.Collapsed;
                    m_rejectButton.Visibility = Visibility.Collapsed;
                    m_redirectButton.Visibility = Visibility.Collapsed;
                    m_callButton.Visibility = Visibility.Collapsed;
                    m_cancelButton.Visibility = Visibility.Collapsed;
                    m_byeButton.Visibility = Visibility.Visible;
                    m_transferButton.Visibility = Visibility.Visible;
                    m_holdButton.Visibility = Visibility.Visible;

                    m_call2ActionsGrid.IsEnabled = true;
                    _useAudio2.IsChecked = true;

                    if (_sipClients[0].HasVideo)
                    {
                        _sipClients[0].OnRemoteVideo += OnClientZeroVideoSinkSample;
                        _videoArea.Visibility = Visibility.Visible;
                        _client0Video.Visibility = Visibility.Visible;
                    }
                    else if (m_videoSource is SoftphoneVideoSourcesEnum.AudioScope)
                    {
                        // The remote party answered without video, so the scope of their audio has
                        // nowhere to be sent. Draw it locally instead.
                        _sipClients[0].OnAudioScopeFrame += OnClientZeroAudioScopeFrame;
                        _client0Video.Visibility = Visibility.Visible;
                    }

                    _uriEntryDropDown.Text = GetCallbackUri(client.Dialogue) ?? _uriEntryDropDown.Text;
                    _uriEntryDropDown.IsEnabled = false;
                    _protocolSelection.IsEnabled = false;

                    //if (m_useAudioScope)
                    //{
                    //    _sipClients[0].MediaSession.OnAudioScopeSampleReady += _audioScope0.ProcessSample;
                    //}
                });
            }
            else if (client == _sipClients[1])
            {
                Dispatcher.DoOnUIThread(() =>
                {
                    m_answer2Button.Visibility = Visibility.Collapsed;
                    m_reject2Button.Visibility = Visibility.Collapsed;
                    m_redirect2Button.Visibility = Visibility.Collapsed;
                    m_call2Button.Visibility = Visibility.Collapsed;
                    m_cancel2Button.Visibility = Visibility.Collapsed;
                    m_bye2Button.Visibility = Visibility.Visible;
                    m_transfer2Button.Visibility = Visibility.Visible;
                    m_hold2Button.Visibility = Visibility.Visible;
                    m_attendedTransferButton.Visibility = Visibility.Visible;

                    if (_sipClients[1].HasVideo)
                    {
                        _sipClients[1].OnRemoteVideo += OnClientOneVideoSinkSample;
                        _videoArea.Visibility = Visibility.Visible;
                        _client1Video.Visibility = Visibility.Visible;
                    }
                    else if (m_videoSource is SoftphoneVideoSourcesEnum.AudioScope)
                    {
                        // The remote party answered without video, so the scope of their audio has
                        // nowhere to be sent. Draw it locally instead.
                        _sipClients[1].OnAudioScopeFrame += OnClientOneAudioScopeFrame;
                        _client1Video.Visibility = Visibility.Visible;
                    }

                    _uriEntry2DropDown.Text = GetCallbackUri(client.Dialogue) ?? _uriEntry2DropDown.Text;
                    _uriEntry2DropDown.IsEnabled = false;
                    _protocolSelection2.IsEnabled = false;
                });

                if (_sipClients[0].IsCallActive)
                {
                    if (!_sipClients[0].IsOnHold)
                    {
                        await _sipClients[0].PutOnHold();
                    }

                    Dispatcher.DoOnUIThread(() =>
                    {
                        m_holdButton.Visibility = Visibility.Collapsed;
                        m_offHoldButton.Visibility = Visibility.Visible;
                        m_attendedTransferButton.Visibility = Visibility.Visible;
                    });
                }
            }

            UpdateRttSendingState();
        }

        private string GetCallbackUri(SIPDialogue dialogue)
        {
            string uri = null;

            if (dialogue.Direction == SIPCallDirection.In)
            {
                // user is set for calls from a telephony server, but not for direct calls
                uri = dialogue.RemoteTarget.User ?? dialogue.RemoteTarget.ToString();

                // insert user name for remote targets from telephony server (cause remote target is the server only)
                if (uri.Contains("sip:") && !uri.Contains('@'))
                {
                    uri = uri.Replace("sip:", "sip:unknown@");
                }
            }

            return uri;
        }

        private void OnClientZeroVideoSinkSample(byte[] sample, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat)
            => ShowClientFrame(sample, width, height, stride, pixelFormat, _clientVideoStates[0]);

        private void OnClientZeroAudioScopeFrame(byte[] sample, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat)
            => ShowClientFrame(sample, width, height, stride, pixelFormat, _clientVideoStates[0]);

        private void OnClientOneVideoSinkSample(byte[] sample, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat)
            => ShowClientFrame(sample, width, height, stride, pixelFormat, _clientVideoStates[1]);

        private void OnClientOneAudioScopeFrame(byte[] sample, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat)
            => ShowClientFrame(sample, width, height, stride, pixelFormat, _clientVideoStates[1]);

        /// <summary>
        /// The button to place an outgoing call.
        /// </summary>
        private async void CallButton_Click(object sender, RoutedEventArgs e)
        {
            SIPClient client = (sender == m_callButton) ? _sipClients[0] : _sipClients[1];
            string destination1 = _uriEntryDropDown.Text;
            string destination2 = _uriEntry2DropDown.Text;
            bool useAudio, useVideo, useText;

            if (client == _sipClients[0] && destination1.IsNullOrBlank())
            {
                SetStatusText(m_signallingStatus, "No call destination was specified.");
            }
            else if (client == _sipClients[1] && destination2.IsNullOrBlank())
            {
                SetStatusText(m_signallingStatus, "No call destination was specified.");
            }
            else
            {
                string callDestination = null;
                SIPProtocolsEnum protocol = SIPProtocolsEnum.udp;

                if (client == _sipClients[0])
                {
                    callDestination = destination1;
                    useAudio = _useAudio.IsChecked ?? true;
                    useVideo = _useVideo.IsChecked ?? true;
                    useText = _useText.IsChecked ?? true;
                    protocol = (SIPProtocolsEnum)_protocolSelection.SelectedValue;

                    SetStatusText(m_signallingStatus, $"calling {callDestination}.");

                    m_callButton.Visibility = Visibility.Collapsed;
                    m_cancelButton.Visibility = Visibility.Visible;
                    m_byeButton.Visibility = Visibility.Collapsed;
                }
                else if (client == _sipClients[1])
                {
                    // Put the first call on hold.
                    if (_sipClients[0].IsCallActive)
                    {
                        await _sipClients[0].PutOnHold();
                        m_holdButton.Visibility = Visibility.Collapsed;
                        m_offHoldButton.Visibility = Visibility.Visible;
                    }

                    callDestination = destination2;
                    useAudio = _useAudio2.IsChecked ?? true;
                    useVideo = _useVideo2.IsChecked ?? true;
                    useText = _useText2.IsChecked ?? true;
                    protocol = (SIPProtocolsEnum)_protocolSelection2.SelectedValue;

                    SetStatusText(m_signallingStatus, $"calling {callDestination}.");

                    m_call2Button.Visibility = Visibility.Collapsed;
                    m_cancel2Button.Visibility = Visibility.Visible;
                    m_bye2Button.Visibility = Visibility.Collapsed;
                }
                else
                {
                    return;
                }

                // Start SIP call.
                await client.Call(callDestination, protocol, useAudio, useVideo, useText);
            }
        }

        /// <summary>
        /// The button to cancel an outgoing call.
        /// </summary>
        private void CancelButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var client = (sender == m_cancelButton) ? _sipClients[0] : _sipClients[1];
            client.Cancel();
            ResetToCallStartState(client);
        }

        /// <summary>
        /// The button to hang up an outgoing call.
        /// </summary>
        private void ByeButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var client = (sender == m_byeButton) ? _sipClients[0] : _sipClients[1];
            client.Hangup();

            ResetToCallStartState(client);
        }

        /// <summary>
        /// The button to answer an incoming call.
        /// </summary>
        private async void AnswerButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var client = (sender == m_answerButton) ? _sipClients[0] : _sipClients[1];

            await AnswerCallAsync(client);
            UpdateRttSendingState();
        }

        /// <summary>
        /// Answer an incoming call on the SipClient
        /// </summary>
        /// <param name="client"></param>
        /// <returns></returns>
        private async Task AnswerCallAsync(SIPClient client)
        {
            bool result = await client.Answer();

            if (result)
            {
                SIPCallAnswered(client);
            }
            else
            {
                ResetToCallStartState(client);
            }
        }

        /// <summary>
        /// The button to reject an incoming call.
        /// </summary>
        private void RejectButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var client = (sender == m_rejectButton) ? _sipClients[0] : _sipClients[1];
            client.Reject();
            ResetToCallStartState(client);
        }

        /// <summary>
        /// The button to redirect an incoming call.
        /// </summary>
        private void RedirectButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var client = (sender == m_redirectButton) ? _sipClients[0] : _sipClients[1];

            if (client == _sipClients[0])
            {
                client.Redirect(_uriEntryDropDown.Text);
            }
            else if (client == _sipClients[1])
            {
                client.Redirect(_uriEntry2DropDown.Text);
            }

            ResetToCallStartState(client);
        }

        /// <summary>
        /// The button to send a blind transfer request to the remote call party.
        /// </summary>
        private async void BlindTransferButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var client = (sender == m_transferButton) ? _sipClients[0] : _sipClients[1];
            bool wasAccepted = await client.BlindTransfer(_uriEntryDropDown.Text);

            if (wasAccepted)
            {
                //TODO: We need to the end the call

                ResetToCallStartState(client);
            }
            else
            {
                SetStatusText(m_signallingStatus, "The remote call party did not accept the transfer request.");
            }

            UpdateRttSendingState();
        }

        /// <summary>
        /// The button to initiate an attended transfer request between the two in active calls.
        /// </summary>
        private async void AttendedTransferButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            bool wasAccepted = await _sipClients[1].AttendedTransfer(_sipClients[0].Dialogue);

            if (!wasAccepted)
            {
                SetStatusText(m_signallingStatus, "The remote call party did not accept the transfer request.");
            }

            UpdateRttSendingState();
        }

        /// <summary>
        /// The remote call party put us on hold.
        /// </summary>
        private void RemotePutOnHold(SIPClient sipClient)
        {
            // We can't put them on hold if they've already put us on hold.
            SetStatusText(m_signallingStatus, "Put on hold by remote party.");

            if (sipClient == _sipClients[0])
            {
                Dispatcher.DoOnUIThread(() =>
                {
                    m_holdButton.Visibility = Visibility.Collapsed;
                });
            }
            else if (sipClient == _sipClients[1])
            {
                Dispatcher.DoOnUIThread(() =>
                {
                    m_hold2Button.Visibility = Visibility.Collapsed;
                });
            }
        }

        /// <summary>
        /// The remote call party has taken us off hold.
        /// </summary>
        private void RemoteTookOffHold(SIPClient sipClient)
        {
            SetStatusText(m_signallingStatus, "Taken off hold by remote party.");

            if (sipClient == _sipClients[0])
            {
                Dispatcher.DoOnUIThread(() =>
                {
                    m_holdButton.Visibility = Visibility.Visible;
                });
            }
            else if (sipClient == _sipClients[1])
            {
                Dispatcher.DoOnUIThread(() =>
                {
                    m_hold2Button.Visibility = Visibility.Visible;
                });
            }
        }

        /// <summary>
        /// We are putting the remote call party on hold.
        /// </summary>
        private async void HoldButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            SIPClient client = (sender == m_holdButton) ? _sipClients[0] : _sipClients[1];

            if (client == _sipClients[0])
            {
                m_holdButton.Visibility = Visibility.Collapsed;
                m_offHoldButton.Visibility = Visibility.Visible;
                await client.PutOnHold();
            }
            else if (client == _sipClients[1])
            {
                m_hold2Button.Visibility = Visibility.Collapsed;
                m_offHold2Button.Visibility = Visibility.Visible;
                await client.PutOnHold();
            }

            UpdateRttSendingState();
        }

        /// <summary>
        /// We are taking the remote call party off hold.
        /// </summary>
        private async void OffHoldButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            SIPClient client = (sender == m_offHoldButton) ? _sipClients[0] : _sipClients[1];

            if (client == _sipClients[0])
            {
                m_holdButton.Visibility = Visibility.Visible;
                m_offHoldButton.Visibility = Visibility.Collapsed;
            }
            else if (client == _sipClients[1])
            {
                m_hold2Button.Visibility = Visibility.Visible;
                m_offHold2Button.Visibility = Visibility.Collapsed;
            }

            client.TakeOffHold();
            UpdateRttSendingState();
        }

        private void UpdateRttSendingState()
        {
            bool canSendText = _sipClients?.Any(c => c.IsCallActive && (c.RttEndPoint?.CanSendText ?? false)) ?? false;
            Dispatcher.DoOnUIThread(() =>
            {
                _rttOutgoingBox.IsReadOnly = !canSendText;
                _rttOutgoingBox.Background = _rttOutgoingBox.IsReadOnly ? Brushes.Gainsboro : Brushes.White;
                _rttOutgoingBox.BorderBrush = _rttOutgoingBox.Background;
                _rttOutgoingBoxBorder.BorderBrush = _rttOutgoingBox.Background;
            });
        }

        private void TextReceived(SIPClient client, Signalling.TextEventArgs e)
        {
            if (client == currentRttClient)
            {
                UpdateRttMessageEntry(lastRemoteRttMsgIndex, e.Timestamp, e.Text);
            }
            else
            {
                // skip invalid contents for new remote user message - todo: possibly apply similar check (excluding backspace char!) for current message above
                if (!e.Text.Any(c => Char.IsLetterOrDigit(c)))
                {
                    return;
                }

                // switch current client to create new message
                currentRttClient = client;

                string authorName = string.IsNullOrWhiteSpace(client.Dialogue.RemoteUserField.Name)
                    ? client.Dialogue.RemoteUserField.URI.User
                    : client.Dialogue.RemoteUserField.Name;
                CreateRttMessageEntry(e.Timestamp, e.Text, authorName);
            }
        }

        private void CreateRttMessageEntry(DateTime messageTime, string message, string remoteName = null)
        {
            bool isRemoteMessage = remoteName != null;
            string authorName = remoteName ?? (string.IsNullOrEmpty(m_sipUsername) ? "me" : m_sipUsername);

            Dispatcher.DoOnUIThread(() =>
            {
                var index = _rttConversationList.Items.Add(new ParticipantMessage(messageTime, authorName, message, isRemoteMessage));
                _rttConversationList.ScrollIntoView(_rttConversationList.Items[index]);

                // skip updating rtt message index for outgoing messages, for being able to support further updates to the last remote message
                if (isRemoteMessage)
                {
                    lastRemoteRttMsgIndex = index;
                }
            });
        }

        private void UpdateRttMessageEntry(int messageIndex, DateTime messageTime, string newText)
        {
            Dispatcher.DoOnUIThread(() =>
            {
                if (_rttConversationList.Items[lastRemoteRttMsgIndex] is ParticipantMessage participantMsg)
                {
                    participantMsg.AddText(messageTime, newText);

                    if (!participantMsg.IsOpen)
                    {
                        currentRttClient = null;
                    }
                }
            });
        }

        // this is for user text input
        private void _rttOutgoingBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Text))
            {
                RttSendTextToAllClients(e.Text);
            }
        }

        // this is for control chars
        private void _rttOutgoingBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            string textToSend = null;

            if (e.Key == System.Windows.Input.Key.Back)
            {
                textToSend = "\b";
            }
            else if (e.Key == System.Windows.Input.Key.Enter)
            {
                textToSend = "\r";

                string completeMessage = _rttOutgoingBox.Text.TrimEnd('\r');
                _ = Task.Run(() => CreateRttMessageEntry(DateTime.Now, completeMessage));
                _rttOutgoingBox.Clear();
            }
            else if (e.Key == System.Windows.Input.Key.Space)
            {
                textToSend = " ";
            }

            if (textToSend != null)
            {
                _sipClients.ForEach(client => _ = Task.Run(() => client.RttEndPoint?.SendText(textToSend)));
            }
        }

        // this is for user pasting from clipboard
        private void _rttOutgoingBox_OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            var isText = e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true);
            if (!isText)
            {
                return;
            }

            var text = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string;
            RttSendTextToAllClients(text);
        }

        private void RttSendTextToAllClients(string text)
        {
            _sipClients.ForEach(client => client.RttEndPoint?.SendText(text));
        }

        private void _rttClear_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.DoOnUIThread(() => _rttConversationList.Items.Clear());
            currentRttClient = null;
        }

        private void _useText_Checked(object sender, RoutedEventArgs e)
        {
            if (_rttArea != null)
            {
                _rttArea.Visibility = (_useText?.IsChecked ?? true) || (_useText2?.IsChecked ?? true)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Set the text on one of the status text blocks. Status messages are used to indicate how the call is
        /// progressing or events related to it.
        /// </summary>
        private void SetStatusText(TextBlock textBlock, string text)
        {
            logger.LogDebug(text);
            statusHistory.Add((DateTime.Now, text));

            if (statusHistory.Count > 10)
            {
                statusHistory.RemoveAt(10);
            }
            string statusHistoryString = statusHistory.Aggregate("Previous states:" + Environment.NewLine,
                    (accu, item) => accu += $"{Environment.NewLine} {item.Time:yyyy-MM-dd HH:mm:ss}: {item.Message}");

            if (SIPSoftPhoneState.Settings.EnableLog)
            {
                statusHistoryString += Environment.NewLine + Environment.NewLine + $"Logfile: {_logPath}";
            }

            Dispatcher.DoOnUIThread(() =>
            {
                textBlock.Text = text;
                textBlock.ToolTip = statusHistoryString;
            });
        }

        /// <summary>
        /// Called when the active SIP client has a bitmap representing the remote video stream
        /// ready.
        /// </summary>
        private void ShowClientFrame(byte[] sample, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat, ClientVideoState client)
        {
            if (pixelFormat != VideoPixelFormatsEnum.Bgr)
            {
                logger.LogError("Cannot display decoded video sample, expected pixel format Bgr but got {PixelFormat}.",
                    pixelFormat);

                return;
            }

            if (sample == null || sample.Length < stride * height)
            {
                // Can happen if a source has nothing to render yet, e.g. the audio scope before the
                // first audio frame arrives. WritePixels throws on an undersized buffer.
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (client.Bitmap == null ||
                    client.Bitmap.PixelWidth != width ||
                    client.Bitmap.PixelHeight != height)
                {
                    client.Bitmap = new WriteableBitmap(
                        (int)width,
                        (int)height,
                        96,
                        96,
                        PixelFormats.Bgr24,
                        null);

                    client.Image.Source = client.Bitmap;
                }

                client.Bitmap.WritePixels(
                    new Int32Rect(0, 0, (int)width, (int)height),
                    sample,
                    stride,
                    0);
            }), DispatcherPriority.Normal);
        }

        /// <summary>
        /// When on a call key pad presses will send a DTMF RTP event to the remote
        /// call party.
        /// </summary>
        /// <param name="sender">The button that was pressed.</param>
        /// <param name="e"></param>
        private async void KeyPadButton_Click(object sender, RoutedEventArgs e)
        {
            Button keyButton = sender as Button;
            char keyPressed = (keyButton.Content as string).ToCharArray()[0];

            SIPClient client = GetActiveCall();

            if (client == null)
            {
                SetStatusText(m_signallingStatus, $"Key pressed {keyPressed} but no active SIP client to send DTMF to.");
            }
            else
            {
                SetStatusText(m_signallingStatus, $"Key pressed {keyPressed}.");

                if (keyPressed >= 48 && keyPressed <= 57)
                {
                    await client.SendDTMF((byte)(keyPressed - 48));
                }
                else if (keyPressed == '*')
                {
                    await client.SendDTMF((byte)10);
                }
                else if (keyPressed == '#')
                {
                    await client.SendDTMF((byte)11);
                }
            }
        }

        /// <summary>
        /// Attempts to find the first active call not on hold.
        /// </summary>
        /// <returns>An active SIP call or null if one is not available.</returns>
        private SIPClient GetActiveCall()
        {
            if (_sipClients == null || _sipClients.Count == 0)
            {
                return null;
            }
            else
            {
                for (int i = 0; i < _sipClients.Count; i++)
                {
                    if (_sipClients[i].IsCallActive && !_sipClients[i].IsOnHold)
                    {
                        return _sipClients[i];
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Clicking the video image will bring it to the front.
        /// </summary>
        private void OnClickVideo(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender == _client0Video)
            {
                Panel.SetZIndex(_client0Video, ZINDEX_TOP);
                Panel.SetZIndex(_client1Video, ZINDEX_TOP - 1);
            }
            else
            {
                Panel.SetZIndex(_client0Video, ZINDEX_TOP - 1);
                Panel.SetZIndex(_client1Video, ZINDEX_TOP);
            }
        }

        /// <summary>
        /// Toggles the appearance of the keypad.
        /// </summary>
        private void ToggleKeyPad(object sender, RoutedEventArgs e)
        {
            if (_keypadGrid.Visibility == Visibility.Hidden)
            {
                _keypadGrid.Visibility = Visibility.Visible;
            }
            else
            {
                _keypadGrid.Visibility = Visibility.Hidden;
            }
        }
    }
}
