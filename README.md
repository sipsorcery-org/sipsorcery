| SIPSorcery    | Examples     | Softphone
| ------------- |:-------------|:---------
[![Build status](https://ci.appveyor.com/api/projects/status/1prvhq7jyw0s5fb1/branch/master?svg=true)](https://ci.appveyor.com/project/sipsorcery/sipsorcery/branch/master) | [![Examples build status](https://ci.appveyor.com/api/projects/status/4myf11mda0p69ysm/branch/master?svg=true)](https://ci.appveyor.com/project/sipsorcery/sipsorcery-mre1o/branch/master) | [![Softphone build status](https://ci.appveyor.com/api/projects/status/xx1bcttkk4gbrd3y/branch/master?svg=true)](https://ci.appveyor.com/project/sipsorcery/sipsorcery-0p6s4/branch/master)
![](https://github.com/sipsorcery/sipsorcery/workflows/sipsorcery-core22/badge.svg) | ![](https://github.com/sipsorcery/sipsorcery/workflows/examples-core30/badge.svg) |

This repository contains the source for a C# .NET library with full support for the Session Initiation Protocol [(SIP)](https://tools.ietf.org/html/rfc3261) including IPv6 support. In addition 
there is partial support for the Real-time Transport Protocol [(RTP)](https://tools.ietf.org/html/rfc3550), Web Real-Time Communication [(WebRTC)](https://en.wikipedia.org/wiki/WebRTC) and a number of related protocols such as RTCP, STUN, SDP and RTSP. Work is ongoing to fully support RTP.

This project does not provide any media (audio and video) handling. There are some limited capabilities in the separate [SIPSorcery.Media](https://github.com/sipsorcery/sipsorcery-media) project but they are Windows specific and not suitable for production. This project can be used for SIP signalling and to send and receive RTP packets but it does not have features to do anything with the payloads in the RTP packets.

## Installation

The library is compliant with .NET Standard 2.0 and .NET Framework 4.5.2. It is available via NuGet.

For .NET Core:

````bash
dotnet add package SIPSorcery
````

With Visual Studio Package Manager Console (or search for [SIPSorcery on NuGet](https://www.nuget.org/packages/SIPSorcery/)):

````ps1
Install-Package SIPSorcery
````

## Documentation

Class reference documentation and articles explaining common usage are available at [https://sipsorcery.github.io/sipsorcery/](https://sipsorcery.github.io/sipsorcery/).

## Getting Started

The [examples folder](https://github.com/sipsorcery/sipsorcery/tree/master/examples) contains full sample code designed to demonstrate some common use cases. The [GetStarted](https://github.com/sipsorcery/sipsorcery/tree/master/examples/GetStarted) example is the best place to start and the main program is shown below.

````csharp
using System;
using System.Net;
using SIPSorcery.SIP;

namespace demo
{
  class Program
  {
    static void Main(string[] args)
    {
      Console.WriteLine("SIPSorcery demo");

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
````

To use the SIP functionality the first step is to initialise the `SIPTransport` class. It takes care of things like retransmitting requests and responses, DNS resolution, selecting the next hop for requests, matching SIP messages to transactions and more.

The `SIPTransport` class can have multiple SIP channels added to it. A SIP channel is roughly the equivalent to the HTTP connection between a Web Browser and Server. It expects all packets received to be either a SIP request or response. The types of SIP channels supported are UDP, TCP and TLS.

The code below shows how to create a `SIPTransport` instance and add a single UDP channel to it.

````csharp
var sipTransport = new SIPTransport();
var sipChannel = new SIPUDPChannel(IPAddress.Loopback, 5060);
sipTransport.AddSIPChannel(sipChannel);
````

To shutdown the `SIPTransport` use:

````csharp
sipTransport.Shutdown();
````

There are two common scenarios when using the `SIPTransport` class:

1. For a server application wire up the `SIPTransport` event handlers, see code below,
2. For client applications the `SIPTranpsort` class can be passed as a constructor parameter. There are a number of client user agents in the `app\SIPUserAgents` folder that
can be used for common client scenarios. See [Next Steps](#next-steps) for a description of the example client applications.

An example of the first approach of wiring up the `SIPTransport` event handlers is shown below. It will respond with a 200 OK response for OPTIONS requests 
and will ignore all other request types.

````csharp
sipTransport.SIPTransportRequestReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
{
  Console.WriteLine($"Request received {localSIPEndPoint.ToString()}<-{remoteEndPoint.ToString()}: {sipRequest.StatusLine}");

  if (sipRequest.Method == SIPMethodsEnum.OPTIONS)
  {
     SIPResponse optionsResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
     sipTransport.SendResponse(optionsResponse);
  }
};
````

## Testing

A convenient tool to test SIP applications is [SIPp](https://github.com/SIPp/sipp). The OPTIONS request handling can be tested from Ubuntu or 
[WSL](https://docs.microsoft.com/en-us/windows/wsl/install-win10) using the steps below.

````bash
$ sudo apt install sip-tester
$ wget https://raw.githubusercontent.com/saghul/sipp-scenarios/master/sipp_uac_options.xml
$ sipp -sf sipp_uac_options.xml -m 3 127.0.0.1
````

If working correctly the message below should appear on the SIPSorcery demo program console:

````bash
SIPSorcery Getting Started Demo
press any key to exit...
Request received udp:127.0.0.1:5060<-udp:127.0.0.1:5061: OPTIONS sip:127.0.0.1:5060 SIP/2.0
Request received udp:127.0.0.1:5060<-udp:127.0.0.1:5061: OPTIONS sip:127.0.0.1:5060 SIP/2.0
Request received udp:127.0.0.1:5060<-udp:127.0.0.1:5061: OPTIONS sip:127.0.0.1:5060 SIP/2.0
````

The SIPp program will also report some test results after a completed test run. In correct operation the `Successful call` row should be greater than 0 and `Failed call` should be 0.

````
-------------------------+---------------------------+--------------------------
  Successful call        |        0                  |        3
  Failed call            |        0                  |        0
-------------------------+---------------------------+--------------------------
````

## Next Steps

Additional example programs are provided to demonstrate how to use the SIPSorcery library in some common scenarios. The example programs are in the `examples` folder.

* [Get Started](https://github.com/sipsorcery/sipsorcery/tree/master/examples/GetStarted): Simplest example. Demonstrates how to initialise a SIP channel and respond to an OPTIONS request.

* [SIP Proxy](https://github.com/sipsorcery/sipsorcery/tree/master/examples/SIPProxy): Expands the `Get Started` example to also handle REGISTER requests. 

* [Registration Client](https://github.com/sipsorcery/sipsorcery/tree/master/examples/UserAgentRegister): Demonstrates how to use the `SIPRegistrationUserAgent` class to register with a SIP Registrar server.

* [SIP Call Client](https://github.com/sipsorcery/sipsorcery/tree/master/examples/UserAgentClient): Demonstrates how to use `SIPClientUserAgent` class to place a call to a SIP server user agent.
 
* [SIP Call Server](https://github.com/sipsorcery/sipsorcery/tree/master/examples/UserAgentServer): Demonstrates how to use the `SIPServerUserAgent` class to receive a call from a SIP client user agent.
 
* [SoftPhone](https://github.com/sipsorcery/sipsorcery/tree/master/examples/Softphone): A very rudimentary SIP softphone implementation.

* [Get Started Web Socket](https://github.com/sipsorcery/sipsorcery/tree/master/examples/GetStartedWebSocket): An example of how to create a web socket listener to send and receive SIP messages. An explanation of the example is available [here](https://sipsorcery.github.io/sipsorcery/articles/websocket-sipchannel.html).

* [STUN Server](https://github.com/sipsorcery/sipsorcery/tree/master/examples/StunServer): An example of how to create a basic STUN ([RFC3849](https://tools.ietf.org/html/rfc3489)) server. An explanation of the example is available [here](https://sipsorcery.github.io/sipsorcery/articles/stunserver.html).

* [Call Transfer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/CallTransfer): An example of how to transfer a call using a REFER request as specified in [RFC3515](https://tools.ietf.org/html/rfc3515). An explanation of the example is available [here](https://sipsorcery.github.io/sipsorcery/articles/calltransfer.html).

* [Send DTMF (as RTP events)](https://github.com/sipsorcery/sipsorcery/tree/master/examples/SendDtmf): An example of how to send DTMF tones using RTP events as specified in [RFC2833](https://tools.ietf.org/html/rfc2833). An explanation of the example is available [here](https://sipsorcery.github.io/sipsorcery/articles/senddtmf.html).

