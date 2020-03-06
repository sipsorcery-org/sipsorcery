## Web Socket SIP Channel

The [GetStartedWebSocket](https://github.com/sipsorcery/sipsorcery/tree/master/examples/GetStartedWebSocket) contains an example of how to create a web socket listener to send and
receive SIP messages.

Testing the web socket SIP channel can be done with the javascript [jssip](https://github.com/versatica/jssip-node-websocket) library.

The example below attempts to connect to a web socket server on localhost port 80. If successful it will send a REGISTER request followed by a MESSAGE request. It should work
out of the box with the [GetStartedWebSocket](https://github.com/sipsorcery/sipsorcery/tree/master/examples/GetStartedWebSocket) example,

````
// Reference: https://github.com/versatica/jssip-node-websocket
// npm install jssip
// npm install jssip-node-websocket

const JsSIP = require('jssip');
const NodeWebSocket = require('jssip-node-websocket');
var https = require('https');

//var socket = new NodeWebSocket('ws://localhost');

var socket = new NodeWebSocket('wss://localhost',
{
	origin : 'https://localhost',
	requestOptions :
	{
		 agent: new https.Agent({ rejectUnauthorized: false })
	}
});
var configuration = {sockets  : [ socket ], uri: 'alice@localhost'};

// Register callbacks to desired message events
var eventHandlers = {
  'succeeded': function(e){ console.log("succeeded " + e); },
  'failed':    function(e){ console.log("failed " + e); }
};

var options = {
  'eventHandlers': eventHandlers
};

var ua = new JsSIP.UA(configuration);
ua.on('connected', function(e){ console.log("connected"); });
ua.on('disconnected', function(e){console.log("disconnected"); });
ua.on('registered', function(e){
  console.log("registered"); 
  ua.sendMessage('sip:bob@localhost', "hi", options);
});
ua.start();

````

To run the sample use:

````
node test.js
````

Output should be:

````
connected
registered
succeeded [object Object]
````

