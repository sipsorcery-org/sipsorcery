//-----------------------------------------------------------------------------
// Filename: SIPToXMPPCall.cs
//
// Description: Represents a translation between an incoing SIP call and an outgoing XMPP call.
// 
// History:
// 13 Dec 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), Hobart, Tasmania, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery. 
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
using System.Net.Sockets;
using System.Text;
using System.Threading;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorcery.XMPP;
using log4net;

namespace SIPSorcery.XMPP.TestConsole
{
    public class SIPToXMPPCall
    {
        private const string GOOGLE_VOICE_HOST = "voice.google.com";

        private static ILog logger = AppState.logger;

        private UdpClient m_xmppMediaSocket;     // Socket to send & receive media and STUN with XMPP server.
        private UdpClient m_sipMediaSocket;      // Socket to send & receive media and STUN with SIP user agent.
        private IPEndPoint m_localXMPPEndPoint;
        private IPEndPoint m_xmppServerEndPoint;
        private IPEndPoint m_localSIPEndPoint;
        private IPEndPoint m_sipPhoneRTPEndPoint;
        private string m_localSTUNUFrag;

        private SIPServerUserAgent m_uas;
        private XMPPPhoneSession m_xmppCall;
        private SIPTransport m_sipTransport;
        private IPAddress m_ipAddress;
        private bool m_exit = false;

        public SIPToXMPPCall(SIPServerUserAgent uas, XMPPPhoneSession xmppCall, SIPTransport sipTransport, IPAddress ipAddress)
        {
            m_uas = uas;
            m_xmppCall = xmppCall;
            m_sipTransport = sipTransport;
            m_ipAddress = ipAddress;

            m_uas.CallCancelled += SIPCallCancelled;
            m_xmppCall.Accepted += Answered;
            m_xmppCall.Rejected += CallFailed;
            m_xmppCall.Hungup += Hangup;
        }

        public void Call(string destination)
        {
            CreateMediaSockets(SDP.GetSDPRTPEndPoint(m_uas.CallRequest.Body));
            m_xmppCall.PlaceCall(destination + "@" + GOOGLE_VOICE_HOST, GetSDPForXMPPRequest());
        }

        public void TerminateXMPPCall()
        {
            CloseMediaSockets();
            m_xmppCall.TerminateCall();
        }

        private void SIPCallCancelled(ISIPServerUserAgent uas)
        {
            CloseMediaSockets();
            m_xmppCall.TerminateCall();
        }

        private void CreateMediaSockets(IPEndPoint sipPhoneRTPEndPoint)
        {
            m_sipPhoneRTPEndPoint = sipPhoneRTPEndPoint;

            m_localXMPPEndPoint = new IPEndPoint(m_ipAddress, Crypto.GetRandomInt(10000, 20000));
            m_xmppMediaSocket = new UdpClient(m_localXMPPEndPoint);
            m_localSIPEndPoint = new IPEndPoint(m_ipAddress, Crypto.GetRandomInt(10000, 20000));
            m_sipMediaSocket = new UdpClient(m_localSIPEndPoint);
            ThreadPool.QueueUserWorkItem(delegate { ListenForSIPPhoneMedia(m_sipMediaSocket); });
            ThreadPool.QueueUserWorkItem(delegate { ListenForXMPPServerMedia(m_xmppMediaSocket); });
        }

        private SDP GetSDPForXMPPRequest()
        {
            m_localSTUNUFrag = Crypto.GetRandomString(8);

            SDP sdp = new SDP()
            {
                IcePwd = Crypto.GetRandomString(12),
                IceUfrag = m_localSTUNUFrag,
                Connection = new SDPConnectionInformation(m_localXMPPEndPoint.Address.ToString()),
                Media = new List<SDPMediaAnnouncement>()
                {
                    new SDPMediaAnnouncement(m_localXMPPEndPoint.Port)
                    {
                        MediaFormats = new List<SDPMediaFormat>()
                        {
                            new SDPMediaFormat(0, "PCMU", 8000)
                        }
                    }
                },
            };

            return sdp;
        }

        private SDP GetSDPForSIPResponse()
        {
            SDP sdp = new SDP()
            {
                Address = m_localSIPEndPoint.Address.ToString(),
                Username = "-",
                SessionId = Crypto.GetRandomString(5),
                AnnouncementVersion = Crypto.GetRandomInt(5),
                Timing = "0 0",
                Connection = new SDPConnectionInformation(m_localSIPEndPoint.Address.ToString()),
                Media = new List<SDPMediaAnnouncement>()
                {
                    new SDPMediaAnnouncement(m_localSIPEndPoint.Port)
                    {
                        MediaFormats = new List<SDPMediaFormat>()
                        {
                            new SDPMediaFormat(0, "PCMU", 8000)
                        }
                    }
                },
            };

            return sdp;
        }

        private void ListenForSIPPhoneMedia(UdpClient localSocket)
        {
            try
            {
                logger.Debug("Commencing listen for media from XMPP server on local socket " + m_localXMPPEndPoint + ".");

                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] buffer = localSocket.Receive(ref remoteEndPoint);
                while (buffer != null && buffer.Length > 0 && !m_exit)
                {
                    if (buffer.Length > 100 && m_xmppServerEndPoint != null)
                    {
                        m_xmppMediaSocket.Send(buffer, buffer.Length, m_xmppServerEndPoint);
                    }
                    buffer = m_sipMediaSocket.Receive(ref remoteEndPoint);
                }
            }
            catch (SocketException)
            { }
            catch (Exception excp)
            {
                logger.Error("Exception ListenForSIPPhoneMedia. " + excp.Message);
            }
            finally
            {
                logger.Debug("Shutting down listen for XMPP server media on local socket " + m_localXMPPEndPoint + ".");
            }
        }

        private void ListenForXMPPServerMedia(UdpClient localSocket)
        {
            try
            {
                logger.Debug("Commencing listen for media from XMPP server on local socket " + m_localSIPEndPoint + ".");
                bool stunResponseSent = false;

                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] buffer = localSocket.Receive(ref remoteEndPoint);
                while (buffer != null && buffer.Length > 0 && !m_exit)
                {
                    //if (!stunResponseSent && buffer[0] >> 0xef == 0)
                    if (!stunResponseSent)
                    {
                        //logger.Debug(buffer.Length + " bytes read on media socket from " + remoteEndPoint.ToString() + ", byte[0]=" + buffer[0].ToString() + ".");

                        STUNMessage stunMessage = STUNMessage.ParseSTUNMessage(buffer, buffer.Length);

                        logger.Debug("STUN message received " + stunMessage.Header.MessageType + ".");

                        if (stunMessage.Header.MessageType == STUNMessageTypesEnum.BindingRequest)
                        {
                            logger.Debug("Sending STUN response to " + remoteEndPoint + ".");
                            stunResponseSent = true;
                            STUNMessage stunResponse = new STUNMessage();
                            stunResponse.Header.MessageType = STUNMessageTypesEnum.BindingResponse;
                            stunResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                            stunResponse.AddUsernameAttribute(Encoding.UTF8.GetString(stunMessage.Attributes[0].Value));
                            byte[] stunRespBytes = stunResponse.ToByteBuffer();
                            m_xmppMediaSocket.Send(stunRespBytes, stunRespBytes.Length, remoteEndPoint);
                        }
                        else
                        {
                            //if (stunMessage.Attributes.Count > 0)
                            //{
                            //    foreach (STUNAttribute stunAttribute in stunMessage.Attributes)
                            //    {
                            //        Console.WriteLine(" " + stunAttribute.AttributeType + "=" + stunAttribute.Value + ".");
                            //    }
                            //}
                        }
                    }

                    if (buffer.Length > 100)
                    {
                        m_sipMediaSocket.Send(buffer, buffer.Length, m_sipPhoneRTPEndPoint);
                    }

                    buffer = localSocket.Receive(ref remoteEndPoint);
                }
            }
            catch (SocketException)
            { }
            catch (Exception excp)
            {
                logger.Error("Exception ListenForXMPPServerMedia. " + excp.Message);
            }
            finally
            {
                logger.Debug("Shutting down listen for SIP phone media on local socket " + m_localSIPEndPoint + ".");
            }
        }

        private void Answered(SDP xmppSDP)
        {
            //Console.WriteLine("Yay call answered.");
            //Console.WriteLine(sdp.ToString());
            m_xmppServerEndPoint = SDP.GetSDPRTPEndPoint(xmppSDP.ToString());
            logger.Debug("Sending STUN binding request to " + m_xmppServerEndPoint + ".");
            STUNMessage initMessage = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
            initMessage.AddUsernameAttribute(xmppSDP.IceUfrag + m_localSTUNUFrag);
            byte[] stunMessageBytes = initMessage.ToByteBuffer();
            m_xmppMediaSocket.Send(stunMessageBytes, stunMessageBytes.Length, m_xmppServerEndPoint);

            m_uas.Answer("application/sdp", GetSDPForSIPResponse().ToString(), null, SIPDialogueTransferModesEnum.NotAllowed);
        }

        private void CallFailed()
        {
            logger.Debug("XMPP call failed, sending SIP error response.");
            CloseMediaSockets();
            m_uas.Reject(SIPResponseStatusCodesEnum.InternalServerError, null, null);
        }

        private void Hangup()
        {
            logger.Debug("XMPP call terminated, hanging up SIP call.");
            CloseMediaSockets();
            m_uas.SIPDialogue.Hangup(m_sipTransport, null);
        }

        private void CloseMediaSockets()
        {
            m_exit = true;

            try
            {
                m_xmppMediaSocket.Close();
            }
            catch { }

            try
            {
                m_sipMediaSocket.Close();
            }
            catch { }
        }
    }
}
