The examples in this folder contain sample code to demonstrate common WebRTC cases including:


 - [WebRTCTestPatternServer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCTestPatternServer): The simplest example. This program serves up a test pattern video stream to a WebRTC peer.
 - [WebRTCServer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCServer): This example extends the test pattern example and can act as a media source for a peer. It has two source options:
   - An mp4 file.
   - Capture devices (webcam and microphone).
 The example includes an html file which runs in a Browser and will connect to a sample program running on the same machine.
- [WebRTCReceiver](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCReceiver): A receive only example. It attempts to connect to a WebRTC peer and display the video stream that it receives.
