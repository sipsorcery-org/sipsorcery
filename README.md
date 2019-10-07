[![Build status](https://ci.appveyor.com/api/projects/status/github/sipsorcery/sipsorcery?svg=true)](https://ci.appveyor.com/project/sipsorcery/sipsorcery) 

This repository contains the source for a C# .NET library with full support for the Session Initiation Protocol [(SIP)](https://tools.ietf.org/html/rfc3261). In addition 
there is partial support for the Real-time Transport Protocol [(RTP)](https://tools.ietf.org/html/rfc3550), Web Real-Time Communication [(WebRTC)](https://en.wikipedia.org/wiki/WebRTC) and a number of related protocols such as RTCP, STUN, SDP and RTSP. Work is ongoing to fully support RTP and add IPv6 support for SIP.

## Howto

The library is compliant with .NET Standard 2.0 and .NET Framework 4.5.2. It is available via NuGet.

For .NET Core:

````
dotnet add package SIPSorcery
````

With Visual Studio Package Manager Console (or search for SIPSorcery on NuGet):

````
Install-Package SIPSorcery
````

## Example

The example below shows how to create a client user agent to maintain a SIP account registration with a SIP server.
The full source code can be found in the examples folder.

````
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
````
