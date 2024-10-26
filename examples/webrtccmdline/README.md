## Usage

This program is intended to allow testing of simple WebRTC connectivity from a command line.

You will need `.NET` installed.

To see all the options available use:

`dotnet run -- --help`

There are 2 main ways to do connectivity tests:

 - From a WebRTC enabled browser using web sockets for signalling.
 - By generating base64 encoded offers and answers and pasting between WebRTC peers.

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

## Echo Test

  - Server
   
  `docker run -it --rm --init -p 8080:8080 ghcr.io/sipsorcery/aiortc-webrtc-echo`
  
  - Client
  
  `dotnet run -- --echo http://localhost:8080/offer`