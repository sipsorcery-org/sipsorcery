## Usage

This program is intended to allow testing of simple WebRTC connectivity from a command line.

You will need `.Net Core` installed.

To see all the options available use:

`dotnet run -- --help`

There are 3 main ways to do connectivity tests:

 - From a WebRTC enabled browser using over web sockets for signalling.
 - By generating base64 encoded offers and answers and pasting between WebRTC peers.
 - Using [node-dss](https://github.com/bengreenier/node-dss) as a very simple signalling between WebRTC ppers.

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

 ## Node-DSS

 [node-dss](https://github.com/bengreenier/node-dss) is a very rudimentary signalling server that can be used to exchange messages between two peers.

 - Install the dependencies and start `node-dss` with:

 ````
 npm install
 npm start
 ````

- Start the application with (adjust the URL as required): 

`dotnet run -- --nodedss http://127.0.0.1:3000`

- Do the same on a second peer so that you have two instances of `webrtccmdline` running. They can be on different machines provided both can access the `node-dss` application. In the example commands below the peer IDs used are `a` and `b` but they can be any arbitrary string as long as both peers use the same values.

- On the first peer press `enter` to get a prompt and type:

`Command => node so a b`

````
`so`: stands for `send offer`.
`a` : is the peer ID of this peer.
`b` : is the peer ID of the remote peer.
````

 - On the second peer press `enter` to get a prompt and type (NOTE the different order of `b a`):

  `Command => node go b a`

````
`go`: stands for `get offer`.
`b` : is the peer ID of this peer.
`a` : is the peer ID of the remote peer.
````
  - Back on the first peer:

  `Command => node ga a b`

  ````
`ga`: stands for `get answer`
`a` : is the ID of this peer.
`b` : is the ID of the remote peer.
  ````


