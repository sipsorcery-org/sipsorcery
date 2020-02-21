## How to Register

The [UserAgentRegister](https://github.com/sipsorcery/sipsorcery/tree/master/examples/UserAgentRegister) contains an example of how to register a SIP account with a SIP Registrar.

The key snippet of the code is shown below with an explanation afterwards.

````csharp
using System;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

string USERNAME = "softphonesample";
string PASSWORD = "password";
string DOMAIN = "sipsorcery.com";
int EXPIRY = 120;

var sipTransport = new SIPTransport();
var regUserAgent = new SIPRegistrationUserAgent(sipTransport, USERNAME, PASSWORD, DOMAIN, EXPIRY);

regUserAgent.RegistrationFailed += (uri, err) => Console.WriteLine($"{uri.ToString()}: {err}");
regUserAgent.RegistrationTemporaryFailure += (uri, msg) => Console.WriteLine($"{uri.ToString()}: {msg}");
regUserAgent.RegistrationRemoved += (uri) => Console.WriteLine($"{uri.ToString()} registration failed.");
regUserAgent.RegistrationSuccessful += (uri) => Console.WriteLine($"{uri.ToString()} registration succeeded.");

regUserAgent.Start();
````

### Explanation

The first step is to create a [SIPTransport](xref:SIPSorcery.SIP.SIPTransport) to allocate a transport layer that can be used to send and receive SIP requests and responses. The [SIPTransport](xref:SIPSorcery.SIP.SIPTransport) class supports a number of different protocols and is described in this [article](transport.md)

````csharp
var sipTransport = new SIPTransport();
````

Once the SIP transport is available a [SIPRegistrationUserAgent](xref:SIPSorcery.SIP.App.SIPRegistrationUserAgent) can be created. 

````csharp
var regUserAgent = new SIPRegistrationUserAgent(sipTransport, USERNAME, PASSWORD, DOMAIN, EXPIRY);
````

Various events for the [SIPRegistrationUserAgent](xref:SIPSorcery.SIP.App.SIPRegistrationUserAgent) can be subscribed to in order to track its operation.

````csharp
regUserAgent.RegistrationFailed += (uri, err) => Console.WriteLine($"{uri.ToString()}: {err}");
regUserAgent.RegistrationTemporaryFailure += (uri, msg) => Console.WriteLine($"{uri.ToString()}: {msg}");
regUserAgent.RegistrationRemoved += (uri) => Console.WriteLine($"{uri.ToString()} registration failed.");
regUserAgent.RegistrationSuccessful += (uri) => Console.WriteLine($"{uri.ToString()} registration succeeded.");
````

The last step is to start the registration agent. This will cause the first registration attempt to occur and depending on the outcome will also schedule subsequent retries.

````csharp
regUserAgent.Start();
````



