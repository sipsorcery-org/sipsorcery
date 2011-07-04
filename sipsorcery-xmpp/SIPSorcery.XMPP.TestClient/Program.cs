//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A quick and dirty test console to prototype a Google XMPP client.
// 
// History:
// 23 Apr 2011	Aaron Clauson	Created.
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
using System.Text;
using System.Threading;
using SIPSorcery.Sys;

namespace SIPSorcery.XMPP.TestClient
{
    class Program
    {
        private const string USAGE_STRING =
@"
usage: xmppclient -u <gtalk username> -p <gtalk password>

User: The GTalk username that will be used to connect to the GTalk XMPP service.
Pass: The GTalk password that will be used to connect to the GTalk XMPP service.";
        
        private const string XMPP_USERNAME_ARG_KEY = "user";
        private const string XMPP_USERNAME_ARG_SHORT_KEY = "u";
        private const string XMPP_PASSWORD_KEY = "pass";
        private const string XMPP_PASSWORD_SHORT_KEY = "p";

        private const string XMPP_SERVER = "talk.google.com";
        private const int XMPP_SERVER_PORT = 5222;
        private const string XMPP_REALM = "google.com";

        private static XMPPClient m_xmppClient;
        private static string m_xmppUsername;
        private static string m_xmppPassword;

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
                        Console.WriteLine("XMPP Test Client:");

                        m_xmppClient = new XMPPClient(XMPP_SERVER, XMPP_SERVER_PORT, XMPP_REALM, m_xmppUsername, m_xmppPassword);
                        m_xmppClient.Disconnected += XMPPDisconnected;
                        m_xmppClient.IsBound += () => { Console.WriteLine("XMPP client is bound."); };
                        ThreadPool.QueueUserWorkItem(delegate { m_xmppClient.Connect(); });

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
                    if (String.Compare(arg.Substring(1), XMPP_USERNAME_ARG_KEY, true) == 0 || String.Compare(arg.Substring(1), XMPP_USERNAME_ARG_SHORT_KEY, true) == 0)
                    {
                        if (args.Length == index + 1 || args[index + 1].Trim().IsNullOrBlank())
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

            if (m_xmppUsername.IsNullOrBlank())
            {
                Console.WriteLine("The XMPP username must be specified.");
                validArgs = false;
            }
            else if (m_xmppPassword.IsNullOrBlank())
            {
                Console.WriteLine("The XMPP password must be specified.");
                validArgs = false;
            }

            return validArgs;
        }

        private static void XMPPDisconnected()
        {
            Console.WriteLine("The XMPP client was disconnected.");
            m_xmppClient = null;
        }
    }
}
