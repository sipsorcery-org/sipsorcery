//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A quick and dirty test console to prototype a SIP-to-XMPP call.
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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorcery.XMPP;

namespace SIPSorcery.XMPP.TestConsole
{
    class Program
    {
        private const string USAGE_STRING = 
@"
usage: siptoxmpp -ip <ipaddress> -port <port> -user <gtalk username> -pass <gtalk password>

IP listen address: The address the application will listen on for SIP requests and that will be used to relay RTP packets.
Port: The port the application will listen on for SIP requests. Must be between 1025 and 65535.
User: The GTalk username that will be used to connect to the GTalk XMPP service.
Pass: The GTalk password that will be used to connect to the GTalk XMPP service.";

        private const string IP_ADDRESS_ARG_KEY = "ip";
        private const string IP_ADDRESS_ARG_SHORT_KEY = "i";
        private const string PORT_ARG_KEY = "port";
        private const string PORT_ARG_SHORT_KEY = "p";
        private const string XMPP_USERNAME_ARG_KEY = "user";
        private const string XMPP_USERNAME_ARG_SHORT_KEY = "u";
        private const string XMPP_PASSWORD_KEY = "pass";
        private const string XMPP_PASSWORD_SHORT_KEY = "s";

        private const string XMPP_SERVER = "talk.google.com";
        private const int XMPP_SERVER_PORT = 5222;
        private const string XMPP_REALM = "google.com";

        private static SIPTransport m_sipTransport;
        private static XMPPClient m_xmppClient;

        private static IPAddress m_ipAddress;
        private static int m_port;
        private static string m_xmppUsername;
        private static string m_xmppPassword;

        private static Dictionary<string, SIPToXMPPCall> m_activeCalls = new Dictionary<string, SIPToXMPPCall>();

        static void Main(string[] args)
        {
            try
            {
                if (args == null || args.Count() == 0)
                {
                    Console.WriteLine(USAGE_STRING);
                }
                else
                {
                    bool validArgs = ParseArgs(args);

                    if (validArgs)
                    {
                        Console.WriteLine("XMPP Test Console:");

                        m_sipTransport = new SIPTransport(SIPDNSManager.ResolveSIPService, new SIPTransactionEngine());
                        SIPUDPChannel udpChannel = new SIPUDPChannel(new IPEndPoint(m_ipAddress, m_port));
                        m_sipTransport.AddSIPChannel(udpChannel);
                        m_sipTransport.SIPTransportRequestReceived += SIPTransportRequestReceived;

                        Console.WriteLine("Waiting for SIP INVITE...");

                        ManualResetEvent mre = new ManualResetEvent(false);
                        mre.WaitOne();
                    }
                }
            }
            catch (Exception excp)
            {
                Console.WriteLine("Exception Main. " + excp.Message);
            }
        }

        private static bool ParseArgs(string[] args)
        {
            bool validArgs = true;

            for (int index = 0; index < args.Length; index++)
            {
                string arg = args[index];

                if (arg.StartsWith("-"))
                {
                    if (String.Compare(arg.Substring(1), IP_ADDRESS_ARG_KEY, true) == 0 || String.Compare(arg.Substring(1), IP_ADDRESS_ARG_SHORT_KEY, true) == 0)
                    {
                        if (args.Length == index + 1 || !IPAddress.TryParse(args[index + 1].Trim(), out m_ipAddress))
                        {
                            Console.WriteLine("The IP address could not be parsed.");
                            validArgs = false;
                            break;
                        }
                        else
                        {
                            index++;
                        }
                    }
                    else if (String.Compare(arg.Substring(1), PORT_ARG_KEY, true) == 0 || String.Compare(arg.Substring(1), PORT_ARG_SHORT_KEY, true) == 0)
                    {
                        if (args.Length == index + 1 || !Int32.TryParse(args[index + 1].Trim(), out m_port))
                        {
                            Console.WriteLine("The port was not recognised as a valid integer.");
                            validArgs = false;
                            break;
                        }
                        else if (m_port <= 1024 || m_port > 65535)
                        {
                            Console.WriteLine("The port was invalid. It must be between 1025 and 65535.");
                            validArgs = false;
                            break;
                        }
                        else
                        {
                            index++;
                        }
                    }
                    else if (String.Compare(arg.Substring(1), XMPP_USERNAME_ARG_KEY, true) == 0 || String.Compare(arg.Substring(1), XMPP_USERNAME_ARG_SHORT_KEY, true) == 0)
                    {
                        if (args.Length == index  + 1|| args[index + 1].Trim().IsNullOrBlank())
                        {
                            Console.WriteLine("The XMPP username was empty.");
                            validArgs = false;
                            break;
                        }
                        else
                        {
                            m_xmppUsername = args[index + 1].Trim();
                            index++;
                        }
                    }
                    else if (String.Compare(arg.Substring(1), XMPP_PASSWORD_KEY, true) == 0 || String.Compare(arg.Substring(1), XMPP_PASSWORD_SHORT_KEY, true) == 0)
                    {
                        if (args.Length == index + 1 || args[index + 1].Trim().IsNullOrBlank())
                        {
                            Console.WriteLine("The XMPP password was empty.");
                            validArgs = false;
                            break;
                        }
                        else
                        {
                             m_xmppPassword = args[index + 1].Trim();
                            index++;
                        }
                    }
                }
            }

            if(m_ipAddress == null)
            {
                Console.WriteLine("The IP address must be specified.");
                validArgs = false;
            }
            else if(m_port == 0)
            {
                Console.WriteLine("The port must be specified.");
                validArgs = false;
            }
            else if(m_xmppUsername.IsNullOrBlank())
            {
                Console.WriteLine("The XMPP username must be specified.");
                validArgs = false;
            }
            else if(m_xmppPassword.IsNullOrBlank())
            {
                Console.WriteLine("The XMPP password must be specified.");
                validArgs = false;
            }

            return validArgs;
        }

        private static void SIPTransportRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            if (sipRequest.Method == SIPMethodsEnum.INVITE)
            {
                Console.WriteLine("INVITE received from  " + localSIPEndPoint.ToString());
                IPEndPoint sipPhoneEndPoint = SDP.GetSDPRTPEndPoint(sipRequest.Body);

                UASInviteTransaction uasTransaction = m_sipTransport.CreateUASTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, null);
                SIPServerUserAgent uas = new SIPServerUserAgent(m_sipTransport, null, null, null, SIPCallDirection.In, null, null, null, uasTransaction);

                SIPResponse tryingResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Trying, null);
                uasTransaction.SendInformationalResponse(tryingResponse);

                if (m_xmppClient == null)
                {
                    m_xmppClient = new XMPPClient(XMPP_SERVER, XMPP_SERVER_PORT, XMPP_REALM, m_xmppUsername, m_xmppPassword);
                    m_xmppClient.Disconnected += XMPPDisconnected;
                    m_xmppClient.IsBound += () => { XMPPPlaceCall(uas); };
                    ThreadPool.QueueUserWorkItem(delegate { m_xmppClient.Connect(); });
                }
                else
                {
                    XMPPPlaceCall(uas);
                }
            }
            else if (sipRequest.Method == SIPMethodsEnum.CANCEL)
            {
                UASInviteTransaction inviteTransaction = (UASInviteTransaction)m_sipTransport.GetTransaction(SIPTransaction.GetRequestTransactionId(sipRequest.Header.Vias.TopViaHeader.Branch, SIPMethodsEnum.INVITE));

                if (inviteTransaction != null)
                {
                    Console.WriteLine("Matching CANCEL request received " + sipRequest.URI.ToString() + ".");
                    SIPCancelTransaction cancelTransaction = m_sipTransport.CreateCancelTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, inviteTransaction);
                    cancelTransaction.GotRequest(localSIPEndPoint, remoteEndPoint, sipRequest);
                }
                else
                {
                    Console.WriteLine("No matching transaction was found for CANCEL to " + sipRequest.URI.ToString() + ".");
                    SIPResponse noCallLegResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                    m_sipTransport.SendResponse(noCallLegResponse);
                }
            }
            else if (sipRequest.Method == SIPMethodsEnum.BYE)
            {
                Console.WriteLine("BYE request received.");

                if (m_activeCalls.ContainsKey(sipRequest.Header.CallId))
                {
                    SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    m_sipTransport.SendResponse(okResponse);
                    m_activeCalls[sipRequest.Header.CallId].TerminateXMPPCall();
                    m_activeCalls.Remove(sipRequest.Header.CallId);
                }
                else
                {
                    SIPResponse doesntExistResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                    m_sipTransport.SendResponse(doesntExistResponse);
                }
            }
        }

        private static void XMPPDisconnected()
        {
            Console.WriteLine("The XMPP client was disconnected.");
            m_xmppClient = null;
        }

        private static void XMPPPlaceCall(SIPServerUserAgent uas)
        {
            if (!uas.IsCancelled)
            {
                XMPPPhoneSession phoneSession = m_xmppClient.GetPhoneSession();
                SIPToXMPPCall call = new SIPToXMPPCall(uas, phoneSession, m_sipTransport, m_ipAddress);
                m_activeCalls.Add(uas.CallRequest.Header.CallId, call);
                call.Call(uas.CallRequest.URI.User);
            }
        }
    }
}
