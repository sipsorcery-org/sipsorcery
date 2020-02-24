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
using System.Net;
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
using SIPSorceryMedia;

namespace SIPSorcery.SoftPhone
{
    public partial class SoftPhone : Window
    {
        private const string VIDEO_LOOPBACK_CALL_DESTINATION = "loop";    // If this destination is called a video loopback call will be attempted.
        private const int SIP_CLIENT_COUNT = 2;                             // The number of SIP clients (simultaneous calls) that the UI can handle.

        // Currently only supporting these mode(s) from local web cams. Extra work to convert other formats to bitmaps that can be displayed by WPF.
        private static readonly List<VideoSubTypesEnum> _supportedVideoModes = new List<VideoSubTypesEnum>() { VideoSubTypesEnum.RGB24 };
        
        private static ILogger logger = Log.Logger;

        private string m_sipUsername = SIPSoftPhoneState.SIPUsername;
        private string m_sipPassword = SIPSoftPhoneState.SIPPassword;
        private string m_sipServer = SIPSoftPhoneState.SIPServer;

        private SIPTransportManager _sipTransportManager;
        private List<SIPClient> _sipClients;
        private SoftphoneSTUNClient _stunClient;                    // STUN client to periodically check the public IP address.
        private SIPRegistrationUserAgent _sipRegistrationClient;    // Can be used to register with an external SIP provider if incoming calls are required.

        private MediaManager _mediaManager;                         // The media (audio and video) manager.
        private WriteableBitmap _localWriteableBitmap;
        private Int32Rect _localBitmapFullRectangle;
        private WriteableBitmap _remoteWriteableBitmap;
        private Int32Rect _remoteBitmapFullRectangle;

        private VideoMode _localVideoMode;

        public SoftPhone()
        {
            InitializeComponent();

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
            _mediaManager = new MediaManager(Dispatcher);
            logger.LogDebug("Media Manager Initialized.");
            _mediaManager.OnLocalVideoSampleReady += LocalVideoSampleReady;
            _mediaManager.OnRemoteVideoSampleReady += RemoteVideoSampleReady;
            _mediaManager.OnLocalVideoError += LocalVideoError;

            await Initialize();

            if (_localVideoDevices.Items.Count == 0)
            {
                await Task.Run(LoadVideoDevices);
            }
        }

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
            null,
            new SIPURI(m_sipUsername, m_sipServer, null, SIPSchemesEnum.sip, SIPProtocolsEnum.udp),
            m_sipUsername,
            m_sipPassword,
            null,
            m_sipServer,
            new SIPURI(m_sipUsername, IPAddress.Any.ToString(), null),
            180,
            (message) => { logger.LogDebug(message.ToString()); });

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

            _mediaManager.Close();
            _sipTransportManager.Shutdown();

            if (_stunClient != null)
            {
                _stunClient.Stop();
            }
        }

        /// <summary>
        /// Retrieves a list of the available video devices, their resolutions and pixel formats.
        /// </summary>
        private void LoadVideoDevices()
        {
            var videoDevices = _mediaManager.GetVideoDevices();
            var videoDeviceKeys = new List<KeyValuePair<string, VideoMode>>();

            if (videoDevices != null && videoDevices.Count > 0)
            {
                for (int index = 0; index < videoDevices.Count; index++)
                {
                    if (_supportedVideoModes.Contains(videoDevices[index].VideoSubType))
                    {
                        var videoSubType = videoDevices[index].VideoSubType; // MFVideoSubTypes.FindVideoSubTypeForGuid(videoDevices[index].VideoSubType);
                        string videoModeName = String.Format("{0} {1} x {2} {3}", videoDevices[index].DeviceFriendlyName, videoDevices[index].Width, videoDevices[index].Height, videoSubType.ToString());

                        videoDeviceKeys.Add(new KeyValuePair<string, VideoMode>(videoModeName, videoDevices[index]));
                        //_localVideoDevices.Items.Add();
                    }
                }
            }

            Dispatcher.DoOnUIThread(delegate
            {
                _localVideoDevices.ItemsSource = videoDeviceKeys;
                _localVideoDevices.IsEnabled = true;
            });
        }

        private void VideoDeviceChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems != null && e.AddedItems.Count > 0)
            {
                var selection = (KeyValuePair<string, VideoMode>)e.AddedItems[0];
                System.Diagnostics.Debug.WriteLine(selection.Key);
                _localVideoMode = selection.Value;
                _startLocalVideoButton.IsEnabled = true;
            }
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
                    SetStatusText(m_signallingStatus, "Ready");
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
        private void SIPCallAnswered(SIPClient client)
        {
            _mediaManager.SetActive(client.MediaSession);
            _mediaManager.StartAudio();

            if (client == _sipClients[0])
            {
                Dispatcher.DoOnUIThread(() =>
                {
                    m_callButton.Visibility = Visibility.Collapsed;
                    m_cancelButton.Visibility = Visibility.Collapsed;
                    m_byeButton.Visibility = Visibility.Visible;
                    m_transferButton.Visibility = Visibility.Visible;
                    m_holdButton.Visibility = Visibility.Visible;

                    m_call2ActionsGrid.IsEnabled = true;
                });
            }
            else if (client == _sipClients[1])
            {
                Dispatcher.DoOnUIThread(() =>
                {
                    m_call2Button.Visibility = Visibility.Collapsed;
                    m_cancel2Button.Visibility = Visibility.Collapsed;
                    m_bye2Button.Visibility = Visibility.Visible;
                    m_transfer2Button.Visibility = Visibility.Visible;
                    m_hold2Button.Visibility = Visibility.Visible;
                    m_attendedTransferButton.Visibility = Visibility.Visible;
                });
            }
        }

        /// <summary>
        /// The button to place an outgoing call.
        /// </summary>
        private void CallButton_Click(object sender, RoutedEventArgs e)
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
            else if (m_uriEntryTextBox.Text == VIDEO_LOOPBACK_CALL_DESTINATION)
            {
                if (_localVideoMode == null)
                {
                    LocalVideoError("Please start the local video and try again.");
                }
                else
                {
                    SetStatusText(m_signallingStatus, "Running video loopback test...");

                    m_callButton.Visibility = Visibility.Collapsed;
                    m_cancelButton.Visibility = Visibility.Collapsed;
                    m_byeButton.Visibility = Visibility.Visible;

                    _mediaManager.RunLoopbackTest();
                }
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
                        _sipClients[0].PutOnHold();
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
                Task.Run(() => client.Call(callDestination));
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
            await client.Answer();

            _mediaManager.SetActive(client.MediaSession);
            _mediaManager.StartAudio();

            if (client == _sipClients[0])
            {
                m_answerButton.Visibility = Visibility.Collapsed; 
                m_rejectButton.Visibility = Visibility.Collapsed;
                m_redirectButton.Visibility = Visibility.Collapsed;
                m_byeButton.Visibility = Visibility.Visible;
                m_transferButton.Visibility = Visibility.Visible;
                m_holdButton.Visibility = Visibility.Visible;

                m_call2ActionsGrid.IsEnabled = true;
            }
            else if (client == _sipClients[1])
            {
                // Put the first call on hold.
                if (_sipClients[0].IsCallActive)
                {
                    _mediaManager.SetOnHold(_sipClients[0].MediaSession);
                    _sipClients[0].PutOnHold();
                    m_holdButton.Visibility = Visibility.Collapsed;
                    m_offHoldButton.Visibility = Visibility.Visible;
                }

                m_answer2Button.Visibility = Visibility.Collapsed;
                m_reject2Button.Visibility = Visibility.Collapsed;
                m_redirect2Button.Visibility = Visibility.Collapsed;
                m_bye2Button.Visibility = Visibility.Visible;
                m_transfer2Button.Visibility = Visibility.Visible;
                m_hold2Button.Visibility = Visibility.Visible;
                m_attendedTransferButton.Visibility = Visibility.Visible;
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
        private void HoldButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            SIPClient client = (sender == m_holdButton) ? _sipClients[0] : _sipClients[1];

            if (client == _sipClients[0])
            {
                m_holdButton.Visibility = Visibility.Collapsed;
                m_offHoldButton.Visibility = Visibility.Visible;
            }
            else if (client == _sipClients[1])
            {
                m_hold2Button.Visibility = Visibility.Collapsed;
                m_offHold2Button.Visibility = Visibility.Visible;
            }

            _mediaManager.SetOnHold(client.MediaSession);
            client.PutOnHold();
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
            }
            else if (client == _sipClients[1])
            {
                m_hold2Button.Visibility = Visibility.Visible;
                m_offHold2Button.Visibility = Visibility.Collapsed;
            }

            _mediaManager.SetActive(client.MediaSession);
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

        private void LocalVideoSampleReady(byte[] sample, int width, int height)
        {
            if (sample != null && sample.Length > 0)
            {
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_localWriteableBitmap == null || _localWriteableBitmap.Width != width || _localWriteableBitmap.Height != height)
                    {
                        _localWriteableBitmap = new WriteableBitmap(
                            width,
                            height,
                            96,
                            96,
                            PixelFormats.Rgb24, //PixelFormats.Bgr32,
                            null);

                        _localVideo.Source = _localWriteableBitmap;
                        _localBitmapFullRectangle = new Int32Rect(0, 0, Convert.ToInt32(_localWriteableBitmap.Width), Convert.ToInt32(_localWriteableBitmap.Height));
                    }

                    // Reserve the back buffer for updates.
                    _localWriteableBitmap.Lock();

                    Marshal.Copy(sample, 0, _localWriteableBitmap.BackBuffer, sample.Length);

                    // Specify the area of the bitmap that changed.
                    _localWriteableBitmap.AddDirtyRect(_localBitmapFullRectangle);

                    // Release the back buffer and make it available for display.
                    _localWriteableBitmap.Unlock();

                }), System.Windows.Threading.DispatcherPriority.Normal);
            }
        }

        private void RemoteVideoSampleReady(byte[] sample, int width, int height)
        {
            if (sample != null && sample.Length > 0)
            {
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_remoteWriteableBitmap == null || _remoteWriteableBitmap.Width != width || _remoteWriteableBitmap.Height != height)
                    {
                        _remoteWriteableBitmap = new WriteableBitmap(
                            width,
                            height,
                            96,
                            96,
                            PixelFormats.Bgr24,
                            null);

                        _remoteVideo.Source = _remoteWriteableBitmap;
                        _remoteBitmapFullRectangle = new Int32Rect(0, 0, Convert.ToInt32(_remoteWriteableBitmap.Width), Convert.ToInt32(_remoteWriteableBitmap.Height));
                    }

                    // Reserve the back buffer for updates.
                    _remoteWriteableBitmap.Lock();

                    Marshal.Copy(sample, 0, _remoteWriteableBitmap.BackBuffer, sample.Length);

                    // Specify the area of the bitmap that changed.
                    _remoteWriteableBitmap.AddDirtyRect(_remoteBitmapFullRectangle);

                    // Release the back buffer and make it available for display.
                    _remoteWriteableBitmap.Unlock();
                }), System.Windows.Threading.DispatcherPriority.Normal);
            }
        }

        private void LocalVideoError(string error)
        {
            Dispatcher.DoOnUIThread(() =>
            {
                if (error.NotNullOrBlank())
                {
                    SetStatusText(m_signallingStatus, error);
                    _startLocalVideoButton.IsEnabled = true;
                    _stopLocalVideoButton.IsEnabled = false;
                    _localVideoDevices.IsEnabled = true;
                }
            });
        }

        private void StartLocalVideo(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_localVideoMode == null)
            {
                LocalVideoError("Please select a video device and format.");
            }
            else
            {
                _startLocalVideoButton.IsEnabled = false;
                _stopLocalVideoButton.IsEnabled = true;
                _localVideoDevices.IsEnabled = false;
                _keypadGrid.Visibility = Visibility.Hidden;
                _locaVIdeoBorder.Visibility = Visibility.Visible;

                _mediaManager.StartVideo(_localVideoMode);
            }
        }

        private void StopLocalVideo(object sender, System.Windows.RoutedEventArgs e)
        {
            _mediaManager.StopVideo();
            _startLocalVideoButton.IsEnabled = true;
            _stopLocalVideoButton.IsEnabled = false;
            _localVideoDevices.IsEnabled = true;
            _locaVIdeoBorder.Visibility = Visibility.Hidden;
            _keypadGrid.Visibility = Visibility.Visible;
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
            if(_sipClients == null || _sipClients.Count == 0)
            {
                return null;
            }
            else
            {
                for (int i=0; i < _sipClients.Count; i++)
                {
                    if(_sipClients[i].IsCallActive && !_sipClients[i].IsOnHold)
                    {
                        return _sipClients[i];
                    }
                }

                return null;
            }
        }
    }
}
