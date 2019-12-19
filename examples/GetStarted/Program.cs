//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A getting started program to demonstrate how to use the SIPTransport class to respond to
// OPTIONS requests.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Oct 2019	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
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

            sipTransport.SIPTransportRequestReceived += async (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
            {
                Console.WriteLine($"Request received {localSIPEndPoint.ToString()}<-{remoteEndPoint.ToString()}: {sipRequest.StatusLine}");

                if (sipRequest.Method == SIPMethodsEnum.OPTIONS)
                {
                    SIPResponse optionsResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    await sipTransport.SendResponseAsync(optionsResponse);
                }
            };

            Console.Write("press any key to exit...");
            Console.Read();

            sipTransport.Shutdown();
        }
    }
}
