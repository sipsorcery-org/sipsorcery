## Transport Layer

The [SIPTransport](xref:SIPSorcery.SIP.SIPTransport) class is the most important class in the SIPSorcery library.

### SIP Channels

This transport layer is responsible for managing the [SIPChannels](xref:SIPSorcery.SIP.SIPChannel) that handle the sending and receiving of all SIP requests and responses. The types of SIP channels supported are:

 - [SIPUDPChannel](xref:SIPSorcery.SIP.SIPUDPChannel) the default channel for transmitting and receiving SIP messages over UDP.
 - [SIPTCPChannel](xref:SIPSorcery.SIP.SIPTCPChannel) transmits and receives SIP messages over TCP.
 - [SIPTLSChannel](xref:SIPSorcery.SIP.SIPTCPChannel) based on the TCP channel but in this case upgraded to support a secure TLS connection.
 - [SIPWebSocketChannel](xref:SIPSorcery.SIP.SIPWebSocketChannel) accepts client web socket connections for SIP communications. Supports secure (wss) connections. This channel is accept only, it cannot be used to establish outgoing connections.
 
To use the SIPSorcery library the first step is always to create an instance of the [SIPTransport](xref:SIPSorcery.SIP.SIPTransport) class  and add one or more SIP channels to it.

The example below shows how to initialise a new [SIPTransport](xref:SIPSorcery.SIP.SIPTransport) class and add IPv4 and IPv6 channels for UDP, TCP and TLS. It's not necessary to always add all channels. In a lot of cases a single IPv4 UDP channel will be sufficient.

````csharp
int SIP_LISTEN_PORT = 5060;
int SIPS_LISTEN_PORT = 5061;
int SIP_WEBSOCKET_LISTEN_PORT = 80;
int SIP_SECURE_WEBSOCKET_LISTEN_PORT = 443;

// Set up a default SIP transport.
var sipTransport = new SIPTransport();

// IPv4 channels.
sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_LISTEN_PORT)));
sipTransport.AddSIPChannel(new SIPTCPChannel(new IPEndPoint(IPAddress.Any, SIP_LISTEN_PORT)));
sipTransport.AddSIPChannel(new SIPTLSChannel(new X509Certificate2("localhost.pfx"), new IPEndPoint(IPAddress.Any, SIPS_LISTEN_PORT)));
sipTransport.AddSIPChannel(new SIPWebSocketChannel(IPAddress.Any, SIP_WEBSOCKET_LISTEN_PORT));
sipTransport.AddSIPChannel(new SIPWebSocketChannel(IPAddress.Any, SIP_SECURE_WEBSOCKET_LISTEN_PORT, localhostCertificate));

// IPv6 channels.
sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.IPv6Any, SIP_LISTEN_PORT)));
sipTransport.AddSIPChannel(new SIPTCPChannel(new IPEndPoint(IPAddress.IPv6Any, SIP_LISTEN_PORT)));
sipTransport.AddSIPChannel(new SIPTLSChannel(new X509Certificate2("localhost.pfx"), new IPEndPoint(IPAddress.IPv6Any, SIPS_LISTEN_PORT)));
sipTransport.AddSIPChannel(new SIPWebSocketChannel(IPAddress.IPv6Any, SIP_WEBSOCKET_LISTEN_PORT));
sipTransport.AddSIPChannel(new SIPWebSocketChannel(IPAddress.IPv6Any, SIP_SECURE_WEBSOCKET_LISTEN_PORT, localhostCertificate));
````

### Receiving

Once the [SIPTransport](xref:SIPSorcery.SIP.SIPTransport) class has been initialised it will automatically start receiving. For an application to get access to received messages it needs to add an event handler for the `SIPTransportRequestReceived` and `SIPTransportResponseReceived` events.

````csharp
sipTransport.SIPTransportRequestReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
{
    Console.WriteLine($"Request received {localSIPEndPoint}<-{remoteEndPoint}: {sipRequest.StatusLine}");
}

sipTransport.SIPTransportResponseReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse) =>
{
    Console.WriteLine($"Response received {localSIPEndPoint}<-{remoteEndPoint}: {sipResponse.ShortDescription}");
};
````

### Sending

To send SIP requests and responses there are a number of synchronous and asynchronous `Send` methods. Note the synchronous `Send` methods are wrappers around the async versions with a `Wait` call attached.

For SIP requests the send methods are shown below:

````charp
public async Task<SocketError> SendRequestAsync(SIPRequest sipRequest)

public async Task<SocketError> SendRequestAsync(SIPEndPoint dstEndPoint, SIPRequest sipRequest)
````

For SIP requests the send methods are shown below:

````csharp
public async Task<SocketError> SendResponseAsync(SIPResponse sipResponse)

public async Task<SocketError> SendResponseAsync(SIPEndPoint dstEndPoint, SIPResponse sipResponse)
````



### Setting Send From Address in Headers

A challenge when sending SIP requests and responses is the inclusion of IP address information in the headers. It can be the case that the SIP channel a request or response needs to be sent on won't be known until the respective `Send` method is called. The SIPSorcery library provides a convenient way to deal with this situation. By setting the headers with an address of `IPAddress.Any` (or `0.0.0.0`) or `IPAddress.IPv6Any` (or `[::0]`) the transport layer will recognise and replace them with the socket that was selected to send the message.

The specific SIP headers that the transport layer checks are:

 - `Via` header (only the top one).
 - `Contact` header.
 - `From` header.

The example below shows how to set the headers on a request so that the transport layer will automatically take care of seting the correct socket addresses.
 
````csharp
var sipRequest = sipTransport.GetRequest(
   method,
   uri,
   new SIPToHeader(
     null, 
     new SIPURI(uri.User, uri.Host, null, uri.Scheme, SIPProtocolsEnum.udp), 
     null),
SIPFromHeader.GetDefaultSIPFromHeader(uri.Scheme));
   
// Set the Contact header to a default value that lets the transport layer know to update it
// when the sending socket is selected.
sipRequest.Header.Contact = new List<SIPContactHeader>() { SIPContactHeader.GetDefaultSIPContactHeader() };
````

### Hints for Sending Channel

While the transport layer can generally take care of selecting the best channel to send a request or response there are times where it's desireable to provide a suggestion on which specific channel to use. For example if a request is received on a TCP or TLS channel it's generally desireable to send the response back on the same channel. The tranpsort layer has mechanisms to do this but if it needs to be overriden there are two poperties on a request and response that can be used to give the transport layer guidance.

 - [SIPRequest.SendFromHintChannelID](xref:SIPSorcery.SIP.SIPMessageBase.SendFromHintChannelID) and [SIPResponse.SendFromHintChannelID](xref:SIPSorcery.SIP.SIPMessageBase.SendFromHintChannelID): when the SIP transport layer has mutliple channels  this can be used as a mechanism to request that a specific channel be used to send on.
 - [SIPRequest.SendFromHintConnectionID](xref:SIPSorcery.SIP.SIPMessageBase.SendFromHintConnectionID) and [SIPResponse.SendFromHintConnectionID](xref:SIPSorcery.SIP.SIPMessageBase.SendFromHintConnectionID): for connection oriented channels, such as TCP and TLS, it's normally crucial that a message is sent back on the same socket connection that the original request was received on. This property can inform the sending channel which connection is desired.
 
The reason these poperties are called hints is that the transport layer may have to overrule them. For example if a hinted channel is UDP but the SIP request being sent is a TCP end point then the hint will have to be ignored. Likewise if a connection hint is for a socket connection that has been closed then the sending channel will ignore it and attempt to establish a new connection. The hints will always be given priority and only if there is a protocol mismatch, closed connection etc. will they be overruled.

The ID's to use for the channel ID and connection ID come from the [LocalSIPEndPoint.ChannelID](xref:SIPSorcery.SIP.SIPEndPoint.ChannelID) and [LocalSIPEndPoint.ConnectionID](xref:SIPSorcery.SIP.SIPEndPoint.ConnectionID) properties on a received SIP request or response.

