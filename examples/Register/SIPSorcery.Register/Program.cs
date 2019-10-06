//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example program of how to use the SIPSorcery core library to register a SIP account. 
// 
// History:
// 07 Oct 2019	Aaron Clauson	Created.
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
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIPSorcery PTY LTD. 
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
using System.Net;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorcery.Sys.Net;

namespace SIPSorcery.Register
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("SIPSorcery registration user agent example.");
            Console.WriteLine("Press ctrl-c to exit.");

            // If your default DNS server support SRV records there is no need to set a specific DNS server.
            DNSManager.SetDNSServers(new List<IPEndPoint> { IPEndPoint.Parse("8.8.8.8:53") });

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport(SIPDNSManager.ResolveSIPService, new SIPTransactionEngine());
            int port = FreePort.FindNextAvailableUDPPort(SIPConstants.DEFAULT_SIP_PORT);
            var sipChannel = new SIPUDPChannel(new IPEndPoint(LocalIPConfig.GetDefaultIPv4Address(), port));
            sipTransport.AddSIPChannel(sipChannel);

            // Create a client user agent. In this case the client maintains a registration with a SIP server.
            var regUserAgent = new SIPRegistrationUserAgent(
                sipTransport,
                null,
                sipTransport.GetDefaultSIPEndPoint(),
                SIPURI.ParseSIPURIRelaxed("softphonesample@sipsorcery.com"),
                "softphonesample",
                "password",
                "sipsorcery.com",
                "sipsorcery.com",
                new SIPURI(SIPSchemesEnum.sip, sipTransport.GetDefaultTransportContact(SIPProtocolsEnum.udp)),
                180,
                null,
                null, 
                (ev) => Console.WriteLine(ev?.Message));

            // Event handlers for the different stages of the registration.
            regUserAgent.RegistrationFailed += (uri, err) => Console.WriteLine($"Registration for {uri.ToString()} failed. {err}");
            regUserAgent.RegistrationTemporaryFailure += (uri, msg) => Console.WriteLine($"Registration for {uri.ToString()} in progress. {msg}");
            regUserAgent.RegistrationRemoved += (uri) => Console.WriteLine($"Permanent failure for {uri.ToString()} registration.");
            regUserAgent.RegistrationSuccessful += (uri) => Console.WriteLine($"Registration for {uri.ToString()} succeeded.");

            // Finally start the thread to perform the initial and registration and periodically resend it.
            regUserAgent.Start();
        }
    }
}
