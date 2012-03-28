//-----------------------------------------------------------------------------
// Filename: GingleClient.cs
//
// Description: An XMPP based client for making calls via Google Voice's
// XMPP and nastardised Jingle (Gingle) gateway. 
// 
// History:
// 27 Mar 2012	Aaron Clauson	Refactored.
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
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using SIPSorcery.XMPP;
using log4net;

namespace SIPSorcery.SoftPhone
{
    public class GingleClient : IVoIPClient
    {
        public const string GINGLE_PREFIX = "gingle:";
        private const string XMPP_SERVER = "talk.google.com";
        private const int XMPP_SERVER_PORT = 5222;
        private const string XMPP_REALM = "google.com";
        private const string GOOGLE_VOICE_HOST = "voice.google.com";

        private ILog logger = AppState.logger;

        // Gingle variables.
        private XMPPClient m_xmppClient;
        private bool _isBound;
        private XMPPPhoneSession m_xmppCall;
        private string m_xmppUsername = "";
        private string m_xmppPassword = "";
        private string m_localSTUNUFrag;
        private AudioChannel _audioChannel;

        public event Action CallEnded;
        public event Action<string> StatusMessage;

        public GingleClient()
        {
            m_xmppClient = new XMPPClient(XMPP_SERVER, XMPP_SERVER_PORT, XMPP_REALM, m_xmppUsername, m_xmppPassword);
            m_xmppClient.Disconnected += XMPPDisconnected;
            m_xmppClient.IsBound += () => { _isBound = true; };

            ThreadPool.QueueUserWorkItem(delegate { BindClient(); });
        }

        private void BindClient()
        {
            logger.Debug("Commencing bind on XMPP client.");
            m_xmppClient.Connect();   
        }

        public void Call(string destination)
        {
            if (!_isBound)
            {
                throw new ApplicationException("The Google Voice call could not proceed as the XMPP client is not bound.");
            }
            else
            {
                destination = destination.Replace(GINGLE_PREFIX, "");
                _audioChannel = new AudioChannel();

                // Call to Google Voice over XMPP & Gingle (Google's version of Jingle).
                XMPPPhoneSession phoneSession = m_xmppClient.GetPhoneSession();

                m_xmppCall = m_xmppClient.GetPhoneSession();
                m_xmppCall.Accepted += XMPPAnswered;
                m_xmppCall.Rejected += XMPPCallFailed;
                m_xmppCall.Hungup += Hangup;

                m_localSTUNUFrag = Crypto.GetRandomString(8);
                SDP xmppSDP = _audioChannel.GetSDP(true);
                xmppSDP.IcePwd = Crypto.GetRandomString(12);
                xmppSDP.IceUfrag = m_localSTUNUFrag;

                m_xmppCall.PlaceCall(destination + "@" + GOOGLE_VOICE_HOST, xmppSDP);
            }
        }

        private void XMPPAnswered(SDP xmppSDP)
        {
            StatusMessage("Google Voice call answered.");

            IPEndPoint remoteSDPEndPoint = SDP.GetSDPRTPEndPoint(xmppSDP.ToString());
            _audioChannel.SetRemoteRTPEndPoint(remoteSDPEndPoint);
            
            STUNMessage initMessage = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
            initMessage.AddUsernameAttribute(xmppSDP.IceUfrag + m_localSTUNUFrag);
            byte[] stunMessageBytes = initMessage.ToByteBuffer();
            _audioChannel.SendRTPRaw(stunMessageBytes, stunMessageBytes.Length);
        }

        private void XMPPDisconnected()
        {
            StatusMessage("The Google Voice XMPP client connection was unexpectedly disconnected.");
            m_xmppClient = null;
            CallFinished();
        }

        private void XMPPCallFailed()
        {
            StatusMessage("Google Voice call failed.");
            CallFinished();
        }

        public void Cancel()
        {
            m_xmppCall.TerminateCall();
            StatusMessage("Google Voice call cancelled.");
            CallFinished();
        }

        public void Answer()
        {
            throw new NotSupportedException("Incoming calls are currently not supported with the Gingle client.");
        }

        public void Redirect(string destination)
        {
            throw new NotSupportedException("Incoming calls are currently not supported with the Gingle client.");
        }

        public void Reject()
        {
            throw new NotSupportedException("Incoming calls are currently not supported with the Gingle client.");
        }

        public void Hangup()
        {
            m_xmppCall.TerminateCall();
            StatusMessage("Google Voice call terminated.");
            CallFinished();
        }

        private void CallFinished()
        {
            if (_audioChannel != null)
            {
                _audioChannel.Close();
            }

            CallEnded();
        }

        public void Shutdown()
        {
            if (_audioChannel != null)
            {
                _audioChannel.Close();
            }

            if (m_xmppCall != null)
            {
                m_xmppCall.TerminateCall();
            }
        }
    }
}
