# **SIPSorcery Reference**

This site contains the reference documentation for the SIPSorcery library as well as articles to help with different use cases.

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

## Next Steps

Additional example programs are provided to demonstrate how to use the SIPSorcery library in some common scenarios. The example programs are in the `examples` folder.

* [Get Started](https://github.com/sipsorcery/sipsorcery/tree/master/examples/GetStarted): Simplest example. Demonstrates how to initialise a SIP channel and respond to an OPTIONS request.

* [SIP Proxy](https://github.com/sipsorcery/sipsorcery/tree/master/examples/SIPProxy): Expands the `Get Started` example to also handle REGISTER requests. 

* [Registration Client](https://github.com/sipsorcery/sipsorcery/tree/master/examples/UserAgentRegister): Demonstrates how to use the `SIPRegistrationUserAgent` class to register with a SIP Registrar server.

* [SIP Call Client](https://github.com/sipsorcery/sipsorcery/tree/master/examples/UserAgentClient): Demonstrates how to use `SIPClientUserAgent` class to place a call to a SIP server user agent.
 
* [SIP Call Server](https://github.com/sipsorcery/sipsorcery/tree/master/examples/UserAgentServer): Demonstrates how to use the `SIPServerUserAgent` class to receive a call from a SIP client user agent.
 
* [SoftPhone](https://github.com/sipsorcery/sipsorcery/tree/master/examples/Softphone): A very rudimentary SIP softphone implementation.
