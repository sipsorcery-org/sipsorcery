//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A getting started program to demonstrate how to use the SIPTransport class to respond to
// OPTIONS requests.
// 
// History:
// 13 Oct 2019	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2019 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Dublin, Ireland (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
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

//-----------------------------------------------------------------------------
// Notes:
//
// A convenient tool to test SIP applications is [SIPp] (https://github.com/SIPp/sipp). The OPTIONS request handling can 
// be tested from Ubuntu or [WSL] (https://docs.microsoft.com/en-us/windows/wsl/install-win10) using the steps below.
//
// $ sudo apt install sip-tester
// $ wget https://raw.githubusercontent.com/saghul/sipp-scenarios/master/sipp_uac_options.xml
// $ sipp -sf sipp_uac_options.xml -max_socket 1 -r 1 -p 5062 -rp 1000 -trace_err 127.0.0.1
//
//-----------------------------------------------------------------------------

using System;
using System.Net;
using SIPSorcery.SIP;

namespace demo
{
    class Program
    {
        static void Main()
        {
            Console.WriteLine("SIPSorcery Getting Started Demo");

            var sipTransport = new SIPTransport();
            var sipChannel = new SIPUDPChannel(IPAddress.Loopback, 5060);
            sipTransport.AddSIPChannel(sipChannel);

            sipTransport.SIPTransportRequestReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
            {
                Console.WriteLine($"Request received {localSIPEndPoint.ToString()}<-{remoteEndPoint.ToString()}: {sipRequest.StatusLine}");

                if (sipRequest.Method == SIPMethodsEnum.OPTIONS)
                {
                    SIPResponse optionsResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    sipTransport.SendResponse(optionsResponse);
                }
            };

            Console.Write("press any key to exit...");
            Console.Read();

            sipTransport.Shutdown();
        }
    }
}
