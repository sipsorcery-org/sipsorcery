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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
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

        private static ILogger logger = SIPSorcery.LogFactory.CreateLogger<SoftPhone>();

        private string m_sipUsername = SIPSoftPhoneState.Settings.SIPUsername;
        private string m_sipPassword = SIPSoftPhoneState.Settings.SIPPassword;
        private string m_sipServer = SIPSoftPhoneState.Settings.SIPServer;
        private bool m_useAudioScope = SIPSoftPhoneState.Settings.UseAudioScope;

        private SIPTransportManager _sipTransportManager;
        private List<SIPClient> _sipClients;
        private SoftphoneSTUNClient _stunClient;                    // STUN client to periodically check the public IP address.
        private SIPRegistrationUserAgent _sipRegistrationClient;    // Can be used to register with an external SIP provider if incoming calls are required.

        private ClientVideoState[] _clientVideoStates;

        public SoftPhone()
        {
            InitializeComponent();

            SIPSorceryMedia.FFmpeg.FFmpegInit.Initialise(SIPSorceryMedia.FFmpeg.FfmpegLogLevelEnum.AV_LOG_VERBOSE, null, logger);

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
        }

        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            await Initialize();
        }

        /// <summary>
        /// Initialises the SIP clients and transport.
        /// </summary>
        private async Task Initialize()
        {
            await _sipTransportManager.InitialiseSIP();

            for (int i = 0; i < SIP_CLIENT_COUNT; i++)
            {
                var sipClient = new SIPClient(_sipTransportManager.SIPTransport);

                sipClient.CallAnswer += SIPCallAnswered;
                sipClient.CallEnded += ResetToCallStartState;
                sipClient.StatusMessage += (client, message) => { SetStatusText(m_signallingStatus, message); };
                sipClient.RemotePutOnHold += RemotePutOnHold;
                sipClient.RemoteTookOffHold += RemoteTookOffHold;

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

            _sipRegistrationClient = new SIPRegistrationUserAgent(
                _sipTransportManager.SIPTransport,
                m_sipUsername,
                m_sipPassword,
                m_sipServer,
                REGISTRATION_EXPIRY,
                sendUsernameInContactHeader: true);

            _sipRegistrationClient.Start();
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
                });

                if (_sipClients?.Count > 1 && _sipClients[1] != null)
                {
                    _sipClients[1].OnRemoteVideo -= OnClientOneVideoSinkSample;
                    _sipClients[1].OnAudioScopeFrame -= OnClientOneAudioScopeFrame;
                }
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

                    if (_sipClients[0].HasVideo)
                    {
                        _sipClients[0].OnRemoteVideo += OnClientZeroVideoSinkSample;
                        _client0Video.Visibility = Visibility.Visible;
                    }
                    else if (m_useAudioScope)
                    {
                        // No remote video to show, so draw the audio scope locally instead. With
                        // video disabled that is a scope of this end's microphone; if video was
                        // enabled but the remote party answered without it, it is a scope of their
                        // audio that had nowhere to be sent.
                        _sipClients[0].OnAudioScopeFrame += OnClientZeroAudioScopeFrame;
                        _client0Video.Visibility = Visibility.Visible;
                    }
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
                        _client1Video.Visibility = Visibility.Visible;
                    }
                    else if (m_useAudioScope)
                    {
                        // No remote video to show, so draw the audio scope locally instead. With
                        // video disabled that is a scope of this end's microphone; if video was
                        // enabled but the remote party answered without it, it is a scope of their
                        // audio that had nowhere to be sent.
                        _sipClients[1].OnAudioScopeFrame += OnClientOneAudioScopeFrame;
                        _client1Video.Visibility = Visibility.Visible;
                    }
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

            if (client == _sipClients[0] && m_uriEntryTextBox.Text.IsNullOrBlank())
            {
                SetStatusText(m_signallingStatus, "No call destination was specified.");
            }
            else if (client == _sipClients[1] && m_uriEntry2TextBox.Text.IsNullOrBlank())
            {
                SetStatusText(m_signallingStatus, "No call destination was specified.");
            }
            else
            {
                string callDestination = null;

                if (client == _sipClients[0])
                {
                    callDestination = m_uriEntryTextBox.Text;

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

                    callDestination = m_uriEntry2TextBox.Text;

                    SetStatusText(m_signallingStatus, $"calling {callDestination}.");

                    m_call2Button.Visibility = Visibility.Collapsed;
                    m_cancel2Button.Visibility = Visibility.Visible;
                    m_bye2Button.Visibility = Visibility.Collapsed;
                }

                // Start SIP call.
                await client.Call(callDestination);
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
                client.Redirect(m_uriEntryTextBox.Text);
            }
            else if (client == _sipClients[1])
            {
                client.Redirect(m_uriEntry2TextBox.Text);
            }

            ResetToCallStartState(client);
        }

        /// <summary>
        /// The button to send a blind transfer request to the remote call party.
        /// </summary>
        private async void BlindTransferButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var client = (sender == m_transferButton) ? _sipClients[0] : _sipClients[1];
            bool wasAccepted = await client.BlindTransfer(m_uriEntryTextBox.Text);

            if (wasAccepted)
            {
                //TODO: We need to the end the call

                ResetToCallStartState(client);
            }
            else
            {
                SetStatusText(m_signallingStatus, "The remote call party did not accept the transfer request.");
            }
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

            await client.TakeOffHold();
        }

        /// <summary>
        /// Set the text on one of the status text blocks. Status messages are used to indicate how the call is
        /// progressing or events related to it.
        /// </summary>
        private void SetStatusText(TextBlock textBlock, string text)
        {
            logger.LogDebug(text);
            Dispatcher.DoOnUIThread(() =>
            {
                textBlock.Text = text;
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
