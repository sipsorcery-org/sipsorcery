//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A getting started program to demonstrate how to use the SIPSorcery
// library to place a call.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Oct 2019	Aaron Clauson	Created, Dublin, Ireland.
// 31 Dec 2019  Aaron Clauson   Changed from an OPTIONS example to a call example.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using NAudio;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Net;

namespace demo
{
    class Program
    {
        static async Task Main()
        {
            Console.WriteLine("SIPSorcery Getting Started Demo");

            var sipTransport = new SIPTransport();
            var userAgent = new SIPUserAgent(sipTransport, null);
            var rtpSession = new RTPMediaSession((int)SDPMediaFormatsEnum.PCMU, AddressFamily.InterNetwork);
            await userAgent.Call("time@sipsorcery.com", null, null, rtpSession);

          

            Console.Write("press any key to exit...");
            Console.Read();

            sipTransport.Shutdown();
        }
    }
}
