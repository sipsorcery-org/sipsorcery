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
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorcery.SoftPhone
{
    public partial class SoftPhone : Window
    {
        private const int SIP_CLIENT_COUNT = 2;                             // The number of SIP clients (simultaneous calls) that the UI can handle.
        private const int ZINDEX_TOP = 10;
        private const int REGISTRATION_EXPIRY = 180;

        private static ILogger logger = SIPSorcery.LogFactory.CreateLogger<SoftPhone>();

        private string m_sipUsername = SIPSoftPhoneState.SIPUsername;
        private string m_sipPassword = SIPSoftPhoneState.SIPPassword;
        private string m_sipServer = SIPSoftPhoneState.SIPServer;
        private bool m_useAudioScope = SIPSoftPhoneState.UseAudioScope;

        private SIPTransportManager _sipTransportManager;
        private List<SIPClient> _sipClients;
        private SoftphoneSTUNClient _stunClient;                    // STUN client to periodically check the public IP address.
        private SIPRegistrationUserAgent _sipRegistrationClient;    // Can be used to register with an external SIP provider if incoming calls are required.

#pragma warning disable CS0649
        private WriteableBitmap _client0WriteableBitmap;
        private WriteableBitmap _client1WriteableBitmap;
#pragma warning restore CS0649
        //private AudioScope.AudioScope _audioScope0;
        //private AudioScope.AudioScopeOpenGL _audioScopeGL0;
        //private AudioScope.AudioScope _audioScope1;
        //private AudioScope.AudioScopeOpenGL _audioScopeGL1;
        //private AudioScope.AudioScope _onHoldAudioScope;
        //private AudioScope.AudioScopeOpenGL _onHoldAudioScopeGL;

        public SoftPhone()
        {
            InitializeComponent();

            //if(!m_useAudioScope)
            //{
            //    _audioScope0Border.Visibility = Visibility.Collapsed;
            //    //OpenGLDraw = "AudioScopeDraw0" OpenGLInitialized = "AudioScopeInitialized0"
            //    AudioScope0.IsEnabled = false;
            //    AudioScope0.Visibility = Visibility.Hidden;
            //}

            // Do some UI initialization.
            ResetToCallStartState(null);

            _sipTransportManager = new SIPTransportManager();
            _sipTransportManager.IncomingCall += SIPCallIncoming;

            _sipClients = new List<SIPClient>();

            // If a STUN server hostname has been specified start the STUN client to lookup and periodically 
            // update the public IP address of the host machine.
            if (!SIPSoftPhoneState.STUNServerHostname.IsNullOrBlank())
            {
                _stunClient = new SoftphoneSTUNClient(SIPSoftPhoneState.STUNServerHostname);
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
                REGISTRATION_EXPIRY);

            _sipRegistrationClient.Start();
        }

        /// <summary>
        /// Application closing, shutdown the SIP, Google Voice and STUN clients.
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

                    //if (m_useAudioScope && _sipClients?.Count > 0 && sipClient == _sipClients[0] && sipClient.MediaSession != null)
                    //{
                    //    sipClient.MediaSession.OnAudioScopeSampleReady -= _audioScope0.ProcessSample;
                    //}
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
                    //_sipClients[1].PutOnHold(_onHoldAudioScopeGL);
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

                    if (_sipClients[0].MediaSession.HasVideo)
                    {
                        _sipClients[0].MediaSession.OnVideoSinkSample += (sample, width, height, stride, pixelFormat) => VideoSampleReady(sample, width, height, stride, pixelFormat, _client0WriteableBitmap, _client0Video);
                        _client0Video.Visibility = Visibility.Visible;
                    }

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

                    if (_sipClients[1].MediaSession.HasVideo)
                    {
                        _sipClients[1].MediaSession.OnVideoSinkSample += (sample, width, height, stride, pixelFormat) => VideoSampleReady(sample, width, height, stride, pixelFormat, _client1WriteableBitmap, _client1Video);
                        _client1Video.Visibility = Visibility.Visible;
                    }
                });

                if (_sipClients[0].IsCallActive)
                {
                    if (!_sipClients[0].IsOnHold)
                    {
                        //_sipClients[0].PutOnHold(_onHoldAudioScopeGL);
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
                        //_sipClients[0].PutOnHold(_onHoldAudioScopeGL);
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
                //client.PutOnHold(_onHoldAudioScopeGL);
                await client.PutOnHold();
                //_sipClients[0].MediaSession.OnHoldAudioScopeSampleReady += _onHoldAudioScope.ProcessSample;
            }
            else if (client == _sipClients[1])
            {
                m_hold2Button.Visibility = Visibility.Collapsed;
                m_offHold2Button.Visibility = Visibility.Visible;
                //client.PutOnHold(_onHoldAudioScopeGL);
                await client.PutOnHold();
            }
        }

        /// <summary>
        /// We are taking the remote call party off hold.
        /// </summary>
        private void OffHoldButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            SIPClient client = (sender == m_offHoldButton) ? _sipClients[0] : _sipClients[1];

            if (client == _sipClients[0])
            {
                m_holdButton.Visibility = Visibility.Visible;
                m_offHoldButton.Visibility = Visibility.Collapsed;
                //_sipClients[0].MediaSession.OnHoldAudioScopeSampleReady -= _onHoldAudioScope.ProcessSample;
            }
            else if (client == _sipClients[1])
            {
                m_hold2Button.Visibility = Visibility.Visible;
                m_offHold2Button.Visibility = Visibility.Collapsed;
            }

            client.TakeOffHold();
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
        /// <param name="sample">The bitmap sample in pixel format BGR24.</param>
        /// <param name="width">The bitmap width.</param>
        /// <param name="height">The bitmap height.</param>
        /// <param name="stride">The bitmap stride.</param>
        private void VideoSampleReady(byte[] sample, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat, WriteableBitmap wBmp, System.Windows.Controls.Image dst)
        {
            if (sample != null && sample.Length > 0)
            {
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var bmpPixelFormat = PixelFormats.Bgr24;
                    switch(pixelFormat)
                    {
                        case VideoPixelFormatsEnum.Bgr:
                            bmpPixelFormat = PixelFormats.Bgr24;
                            break;
                        case VideoPixelFormatsEnum.Bgra:
                            bmpPixelFormat = PixelFormats.Bgra32;
                            break;
                        case VideoPixelFormatsEnum.Rgb:
                            bmpPixelFormat = PixelFormats.Rgb24;
                            break;
                        default:
                            bmpPixelFormat = PixelFormats.Bgr24;
                            break;
                    }

                    if (wBmp == null || wBmp.Width != width || wBmp.Height != height)
                    {
                        wBmp = new WriteableBitmap(
                            (int)width,
                            (int)height,
                            96,
                            96,
                            bmpPixelFormat,
                            null);

                        dst.Source = wBmp;
                    }

                    // Reserve the back buffer for updates.
                    wBmp.Lock();

                    Marshal.Copy(sample, 0, wBmp.BackBuffer, sample.Length);

                    // Specify the area of the bitmap that changed.
                    wBmp.AddDirtyRect(new Int32Rect(0, 0, (int)width, (int)height));

                    // Release the back buffer and make it available for display.
                    wBmp.Unlock();
                }), System.Windows.Threading.DispatcherPriority.Normal);
            }
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

        //private void AudioScopeInitialized0(object sender, OpenGLEventArgs args)
        //{
        //    if (m_useAudioScope)
        //    {
        //        _audioScope0 = new AudioScope.AudioScope();
        //        _audioScope0.InitAudio(AudioScope.AudioSourceEnum.External);
        //        _audioScopeGL0 = new AudioScope.AudioScopeOpenGL(_audioScope0);
        //        _audioScopeGL0.Initialise(args.OpenGL);
        //    }
        //}

        //private void AudioScopeInitialized1(object sender, OpenGLEventArgs args)
        //{ }

        //private void AudioScopeDraw0(object sender, OpenGLEventArgs args)
        //{
        //    if (m_useAudioScope)
        //    {
        //        if (_sipClients?.Count > 0 && _sipClients[0].IsCallActive)
        //        {
        //            int width = Convert.ToInt32(this.AudioScope0.Width);
        //            int height = Convert.ToInt32(this.AudioScope0.Height);

        //            OpenGL gl = args.OpenGL;
        //            _audioScopeGL0.Draw(gl, width, height);
        //        }
        //    }
        //}

        //private void AudioScopeDraw1(object sender, OpenGLEventArgs args)
        //{ }

        //private void OnHoldAudioScopeInitialized(object sender, OpenGLEventArgs args)
        //{
        //    _onHoldAudioScope = new AudioScope.AudioScope();
        //    _onHoldAudioScope.InitAudio(AudioScope.AudioSourceEnum.External);
        //    //_onHoldAudioScope.InitAudio("media/Macroform_-_Simplicity.ulaw");
        //    _onHoldAudioScopeGL = new AudioScope.AudioScopeOpenGL(_onHoldAudioScope);
        //    _onHoldAudioScopeGL.Initialise(args.OpenGL);
        //    _onHoldAudioScope.Start();
        //}

        //private void OnHoldAudioScopeDraw(object sender, OpenGLEventArgs args)
        //{
        //    //if (_sipClients?.Count > 0 && _sipClients[0].IsOnHold)
        //    //{
        //    int width = Convert.ToInt32(this._onHoldAudioScopeControl.Width);
        //    int height = Convert.ToInt32(this._onHoldAudioScopeControl.Height);

        //    OpenGL gl = args.OpenGL;
        //    _onHoldAudioScopeGL.Draw(gl, width, height);
        //    //}
        //}
    }
}
