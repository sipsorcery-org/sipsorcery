//-----------------------------------------------------------------------------
// Filename: Softphone.xaml.cs
//
// Description: The user interface for the softphone. 
// 
// History:
// 11 Mar 2012	Aaron Clauson	Refactored.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2012 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIPSorcery Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SIPSorceryMedia;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SoftPhone
{
    public partial class SoftPhone : Window
    {
        private const string VIDEO_LOOPBACK_CALL_DESTINATION = "loop";     // If this destination is called a video loopback call will be attempted.

        // Currently only supporting this mode(s) from local web cams. Extra work to convert other formats to bitmaps that can be displayed by WPF.
        private static readonly List<MFVideoSubTypesEnum> _supportedVideoModes = new List<MFVideoSubTypesEnum>() { MFVideoSubTypesEnum.MFVideoFormat_RGB24 };

        private ILog logger = AppState.logger;

        private string m_sipUsername = SIPSoftPhoneState.SIPUsername;
        private string m_sipPassword = SIPSoftPhoneState.SIPPassword;
        private string m_sipServer = SIPSoftPhoneState.SIPServer;

        private SIPClient _sipClient;                               // SIP calls.
        //private GingleClient _gingleClient;                         // Google Voice calls.
        private IVoIPClient _activeClient;                          // The active client, either SIP or GV.
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

            // Do some UI initialisation.
            m_uasGrid.Visibility = Visibility.Collapsed;
            m_cancelButton.Visibility = Visibility.Collapsed;
            m_byeButton.Visibility = Visibility.Collapsed;

            // Set up the SIP client. It can receive calls and initiate outgoing calls.
            _sipClient = new SIPClient();
            _sipClient.IncomingCall += SIPCallIncoming;
            _sipClient.CallAnswer += SIPCallAnswered;
            _sipClient.CallEnded += ResetToCallStartState;
            _sipClient.StatusMessage += (message) => { SetStatusText(m_signallingStatus, message); };

            // Set up the Gingle client.
            //_gingleClient = new GingleClient();
            //_gingleClient.CallEnded += ResetToCallStartState;
            //_gingleClient.StatusMessage += (message) => { SetStatusText(m_signallingStatus, message); };

            // Lookup and periodically check the public IP address of the host machine.
            _stunClient = new SoftphoneSTUNClient();

            // Comment this out if you don't want the app to register with your SIP server.
            //_sipRegistrationClient = new SIPRegistrationUserAgent(
            //    _sipClient.SIPClientTransport,
            //    null,
            //    null,
            //    new SIPURI(m_sipUsername, m_sipServer, null, SIPSchemesEnum.sip, SIPProtocolsEnum.udp),
            //    m_sipUsername,
            //    m_sipPassword,
            //    null,
            //    m_sipServer,
            //    new SIPURI(m_sipUsername, _sipClient.SIPClientTransport.GetDefaultSIPEndPoint().GetIPEndPoint().ToString(), null),
            //    180,
            //    null,
            //    null,
            //    (message) => { logger.Debug(message); });
            //_sipRegistrationClient.Start();
        }

        private void OnWindowLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                _mediaManager = new MediaManager();
                logger.Debug("Media Manager Initialised.");
                _mediaManager.OnLocalVideoSampleReady += LocalVideoSampleReady;
                _mediaManager.OnRemoteVideoSampleReady += RemoteVideoSampleReady;
                _mediaManager.OnLocalVideoError += LocalVideoError;

                if (_localVideoDevices.Items.Count == 0)
                {
                    LoadVideoDevices();
                }
            });
        }

        /// <summary>
        /// Application closing, shutdown the SIP, Google Voice and STUN clients.
        /// </summary>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _mediaManager.Close();
            _sipClient.Shutdown();
            _stunClient.Stop();

            //if (_gingleClient != null)
            //{
            //    _gingleClient.Shutdown();
            //}
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
                    if (_supportedVideoModes.Contains(MFVideoSubTypes.FindVideoSubTypeForGuid(videoDevices[index].VideoSubType)))
                    {
                        var videoSubType = MFVideoSubTypes.FindVideoSubTypeForGuid(videoDevices[index].VideoSubType);
                        string videoModeName = String.Format("{0} {1} x {2} {3}", videoDevices[index].DeviceFriendlyName, videoDevices[index].Width, videoDevices[index].Height, videoSubType.GetSubTypeDescription());

                        videoDeviceKeys.Add(new KeyValuePair<string, VideoMode>(videoModeName, videoDevices[index]));
                        //_localVideoDevices.Items.Add();
                    }
                }
            }

            UIHelper.DoOnUIThread(this, delegate
            {
                _localVideoDevices.ItemsSource = videoDeviceKeys;
                _localVideoDevices.IsEnabled = true;
            });
        }

        private void VideoDeviceChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
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
        private void ResetToCallStartState()
        {
            UIHelper.DoOnUIThread(this, delegate
            {
                m_callButton.Visibility = Visibility.Visible;
                m_cancelButton.Visibility = Visibility.Collapsed;
                m_byeButton.Visibility = Visibility.Collapsed;
                m_answerButton.Visibility = Visibility.Visible;
                m_rejectButton.Visibility = Visibility.Visible;
                m_redirectButton.Visibility = Visibility.Visible;
                m_hangupButton.Visibility = Visibility.Visible;
                m_uacGrid.Visibility = Visibility.Visible;
                m_uasGrid.Visibility = Visibility.Collapsed;
            });

            _activeClient = null;
        }

        /// <summary>
        /// Set up the UI to present the options for an incoming SIP call.
        /// </summary>
        private void SIPCallIncoming()
        {
            _activeClient = _sipClient;

            UIHelper.DoOnUIThread(this, delegate
            {
                m_uacGrid.Visibility = Visibility.Collapsed;
                m_uasGrid.Visibility = Visibility.Visible;
            });
        }

        /// <summary>
        /// Set up the UI to present options for an establisehd SIP call, i.e. hide the cancel 
        /// button and display they hangup button.
        /// </summary>
        private void SIPCallAnswered()
        {
            UIHelper.DoOnUIThread(this, delegate
            {
                m_callButton.Visibility = Visibility.Collapsed;
                m_cancelButton.Visibility = Visibility.Collapsed;
                m_byeButton.Visibility = Visibility.Visible;
            });
        }

        /// <summary>
        /// The button to place an outgoing call.
        /// </summary>
        private void CallButton_Click(object sender, RoutedEventArgs e)
        {
            if (m_uriEntryTextBox.Text.IsNullOrBlank())
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
                SetStatusText(m_signallingStatus, "calling " + m_uriEntryTextBox.Text + ".");

                m_callButton.Visibility = Visibility.Collapsed;
                m_cancelButton.Visibility = Visibility.Visible;
                m_byeButton.Visibility = Visibility.Collapsed;

                string destination = m_uriEntryTextBox.Text;

                // Use Google Voice or the SIP client to place the call depending on the destination prefix.
                //if (m_googleVoiceRadioButton.IsChecked.GetValueOrDefault())
                //{
                //    // Google Voice call.
                //    _activeClient = _gingleClient;
                //    ThreadPool.QueueUserWorkItem(delegate { _gingleClient.Call(_mediaManager, destination); });
                //}
                //else
                //{
                // SIP call.
                _activeClient = _sipClient;
                ThreadPool.QueueUserWorkItem(delegate { _sipClient.Call(_mediaManager, destination); });
                //}
            }
        }

        /// <summary>
        /// The button to cancel an outgoing call.
        /// </summary>
        private void CancelButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _activeClient.Cancel();
            ResetToCallStartState();
        }

        /// <summary>
        /// The button to hang up an outgoing call.
        /// </summary>
        private void ByeButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_activeClient != null)
            {
                _activeClient.Hangup();
            }
            else
            {
                // If no active client then it must be a loopback test that's ending.
                _mediaManager.EndCall();
                SetStatusText(m_signallingStatus, "Ready");
            }

            ResetToCallStartState();
        }

        /// <summary>
        /// The button to answer an incoming call.
        /// </summary>
        private void AnswerButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _activeClient.Answer(_mediaManager);
            m_answerButton.Visibility = Visibility.Collapsed;
            m_rejectButton.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// The button to reject an incoming call.
        /// </summary>
        private void RejectButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _activeClient.Reject();
            ResetToCallStartState();
        }

        /// <summary>
        /// The button to redirect an incoming call.
        /// </summary>
        private void RedirectButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _activeClient.Redirect(m_redirectURIEntryTextBox.Text);
            ResetToCallStartState();
        }

        /// <summary>
        /// The button to hang up an incoming call.
        /// </summary>
        private void HangupButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _activeClient.Hangup();
            ResetToCallStartState();
        }

        /// <summary>
        /// Set the text on one of the status text blocks. Status messages are used to indicate how the call is
        /// progressing or events related to it.
        /// </summary>
        private void SetStatusText(TextBlock textBlock, string text)
        {
            logger.Debug(text);
            UIHelper.DoOnUIThread(this, delegate { textBlock.Text = text; });
        }

        private void LocalAudioSampleReady(byte[] sample)
        {

        }

        private void LocalVideoSampleReady(byte[] sample, int width, int height)
        {
            if (sample != null && sample.Length > 0)
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
                                PixelFormats.Bgr24, //PixelFormats.Bgr32,
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
            UIHelper.DoOnUIThread(this, delegate
            {
                if (error.NotNullOrBlank())
                {
                    _localVideoStatus.Text = error;
                    _localVideoStatusBorder.Visibility = System.Windows.Visibility.Visible;
                    _startLocalVideoButton.IsEnabled = true;
                    _stopLocalVideoButton.IsEnabled = false;
                    _localVideoDevices.IsEnabled = true;
                }
                else
                {
                    _localVideoStatus.Text = null;
                    _localVideoStatusBorder.Visibility = System.Windows.Visibility.Collapsed;
                }
            });
        }

        /// <summary>
        /// Event handler for clicking on the local video error status text box.
        /// </summary>
        private void HideLocalVideoError(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _localVideoStatusBorder.Visibility = System.Windows.Visibility.Collapsed;
            _localVideoStatus.Text = null;
        }

        /// <summary>
        /// Event handler for clicking on the remote video error status text box.
        /// </summary>
        private void HideRemoteVideoError(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _remoteVideoStatusBorder.Visibility = System.Windows.Visibility.Collapsed;
            _remoteVideoStatus.Text = null;
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

                _mediaManager.StartLocalVideo(_localVideoMode);
            }
        }

        private void StopLocalVideo(object sender, System.Windows.RoutedEventArgs e)
        {
            _mediaManager.StopLocalVideo();
            _startLocalVideoButton.IsEnabled = true;
            _stopLocalVideoButton.IsEnabled = false;
            _localVideoDevices.IsEnabled = true;
        }
    }
}
