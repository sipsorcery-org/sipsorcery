## Usage

This program is intended to allow testing of simple WebRTC connectivity from a command line.

You will need `.NET` installed.

To see all the options available use:

`dotnet run -- --help`

There are 3 main ways to do connectivity tests:

 - From a WebRTC enabled browser using over web sockets for signalling.
 - By generating base64 encoded offers and answers and pasting between WebRTC peers.
 - Using the siprocery.cloud echo server https://sipsorcery.cloud/sipsorcery/echo/offer.

## Web Sockets

 - Start the application with:

 `dotnet run -- --ws`

 - If the browser is not on the same machine as the application open a `webrtc.html` in a text editor and adjust the web socket URL's.

 - Open the `webrtc.html` file in a browser and press `Start Send Offer` or `Start Receive Offer`.

 - You should see the log messages in this application and the browser debug console as the WebRTC connection is established.

 ## Base64 Copy Paste

This option was written to be able to test with the [Pion Data Channels Example](https://github.com/pion/webrtc/tree/master/examples/data-channels). To test the steps are:

 - Start the application with:

 `dotnet run -- --offer`

 - A base 64 endoded SDP offer will be shown on the console. Copy it to the Pion Go application using (instructions on how to use the Pion examples are [here](https://github.com/pion/webrtc/tree/master/examples)]):

 `echo <base64 SDP> | go run main.go`

 - The Pion program will generate a base 64 encoded SDP answer. Copy that to the `webrtcmdline` console.

 - The connection log messages should be displayed and a data channel established.

 ## Echo Server

- Start the application with: 

`dotnet run -- --echo https://sipsorcery.cloud/sipsorcery/echo/offer`

There are additional implementations of the WebRTC echo server that can also be tested:

- Janus:

`dotnet run -- --echo https://sipsorcery.cloud/janus/echo/offer`

- aiortc (Python WebRTC library):

`dotnet run -- --echo https://sipsorcery.cloud/aiortc/echo/offer`

## Additional Examples:

Description: Test peer connection establishment to Janus echo server. Use STUN server to include public IPv4 address candidate.
 
`dotnet run -- --echo https://sipsorcery.cloud/janus/echo/offer --stun stun:sipsorcery.com`

Description: Test peer connection and only supply a TURN server (relay) ICE candidate. Has effect of forcing all traffic through a TURN server.

`dotnet run -- --echo https://sipsorcery.cloud/janus/echo/offer --stun turn:sipsorcery.com;aaron;password --relayonly`

Description: Test peer connection establishment and data channel echo.

`dotnet run -- --echo https://sipsorcery.cloud/sipsorcery/echo/offer --noaudio --stun stun:sipsorcery.com`

once connected:

`sdc dcx hello`

Description: Test peer connection establishment with an audio only offer:

`dotnet run -- --echo https://sipsorcery.cloud/sipsorcery/echo/offer --nodatachannel --stun stun:sipsorcery.com`

Description: Test peer connection establishment and DTMF echo.

`dotnet run -- --echo https://sipsorcery.cloud/sipsorcery/echo/offer --nodatachannel --stun stun:sipsorcery.com`

once connected:

`dtmf`