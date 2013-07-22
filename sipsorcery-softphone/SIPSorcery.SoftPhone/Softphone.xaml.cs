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
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SoftPhone
{
    public partial class SoftPhone : Window
    {
        private ILog logger = AppState.logger;
        private ILog _videoLogger = AppState.GetLogger("videodevice");

        private SIPClient _sipClient;               // SIP calls.
        private GingleClient _gingleClient;         // Google Voice calls.
        private IVoIPClient _activeClient;          // The active client, either SIP or GV.
        private SoftphoneSTUNClient _stunClient;    // STUN client to periodically check the public IP address.

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
            _gingleClient = new GingleClient();
            _gingleClient.CallEnded += ResetToCallStartState;
            _gingleClient.StatusMessage += (message) => { SetStatusText(m_signallingStatus, message); };

            // Lookup and periodically check the public IP address of the host machine.
            _stunClient = new SoftphoneSTUNClient();

            //videoElement.NewVideoSample += new EventHandler<WPFMediaKit.DirectShow.MediaPlayers.VideoSampleArgs>(videoElement_NewVideoSample);
        }

        //void videoElement_NewVideoSample(object sender, WPFMediaKit.DirectShow.MediaPlayers.VideoSampleArgs e)
        //{
        //    _videoLogger.Debug("New video frame sample received.");
        //}

        /// <summary>
        /// Application closing, shutdown the SIP, Google Voice and STUN clients.
        /// </summary>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _sipClient.Shutdown();
            _stunClient.Stop();

            if (_gingleClient != null)
            {
                _gingleClient.Shutdown();
            } 
        }

        /// <summary>
        /// Reset the UI elements to their initial state at the end of a call.
        /// </summary>
        private void ResetToCallStartState()
        {
            SetVisibility(m_callButton, Visibility.Visible);
            SetVisibility(m_cancelButton, Visibility.Collapsed);
            SetVisibility(m_byeButton, Visibility.Collapsed);
            SetVisibility(m_answerButton, Visibility.Visible);
            SetVisibility(m_rejectButton, Visibility.Visible);
            SetVisibility(m_redirectButton, Visibility.Visible);
            SetVisibility(m_hangupButton, Visibility.Visible);
            SetVisibility(m_uacGrid, Visibility.Visible);
            SetVisibility(m_uasGrid, Visibility.Collapsed);
        }

        /// <summary>
        /// Set up the UI to present the options for an incoming SIP call.
        /// </summary>
        private void SIPCallIncoming()
        {
            SetVisibility(m_uacGrid, Visibility.Collapsed);
            SetVisibility(m_uasGrid, Visibility.Visible);
        }

        /// <summary>
        /// Set up the UI to present options for an establisehd SIP call, i.e. hide the cancel 
        /// button and display they hangup button.
        /// </summary>
        private void SIPCallAnswered()
        {
            SetVisibility(m_callButton, Visibility.Collapsed);
            SetVisibility(m_cancelButton, Visibility.Collapsed);
            SetVisibility(m_byeButton, Visibility.Visible);
        }

        /// <summary>
        /// The button to place an outgoing call.
        /// </summary>
        private void CallButton_Click(object sender, RoutedEventArgs e)
        {
            SetStatusText(m_signallingStatus, "calling " + m_uriEntryTextBox.Text + ".");

            m_callButton.Visibility = Visibility.Collapsed;
            m_cancelButton.Visibility = Visibility.Visible;
            m_byeButton.Visibility = Visibility.Collapsed;

            string destination = m_uriEntryTextBox.Text;

            // Use Google Voice or the SIP client to place the call depending on which radio button has been checked.
            if (m_googleVoiceRadioButton.IsChecked.GetValueOrDefault())
            {
                // Google Voice call.
                _activeClient = _gingleClient;
                ThreadPool.QueueUserWorkItem(delegate { _gingleClient.Call(destination); });
            }
            else
            {
                // SIP call.
                _activeClient = _sipClient;
                ThreadPool.QueueUserWorkItem(delegate { _sipClient.Call(destination); });
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
            _activeClient.Hangup();
            ResetToCallStartState();
        }

        /// <summary>
        /// The button to answer an incoming call.
        /// </summary>
        private void AnswerButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _activeClient.Answer();
            SetVisibility(m_answerButton, Visibility.Collapsed);
            SetVisibility(m_rejectButton, Visibility.Collapsed);
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

            if (Thread.CurrentThread != Dispatcher.Thread)
            {
                Dispatcher.Invoke(new Action<TextBlock, string>(SetStatusText), textBlock, text);
                return;
            }

            textBlock.Text = text;
        }

        /// <summary>
        /// Set the visibility on a UI element.
        /// </summary>
        private void SetVisibility(UIElement element, Visibility visibility)
        {
            if (Thread.CurrentThread != Dispatcher.Thread)
            {
                Dispatcher.Invoke(new Action<UIElement, Visibility>(SetVisibility), element, visibility);
                return;
            }

            element.Visibility = visibility;
        }
    }
}
