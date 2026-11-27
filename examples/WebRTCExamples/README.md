The examples in this folder contain sample code to demonstrate common WebRTC cases including:


 - [WebRTCTestPatternServer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCTestPatternServer): The simplest example. This program serves up a test pattern video stream to a WebRTC peer.
 - [WebRTCServer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCServer): This example extends the test pattern example and can act as a media source for a peer. It has two source options:
   - An mp4 file.
   - Capture devices (webcam and microphone).
 The example includes an html file which runs in a Browser and will connect to a sample program running on the same machine.
- [WebRTCReceiver](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCReceiver): A receive only example. It attempts to connect to a WebRTC peer and display the video stream that it receives.
- [WebRTCClientH264Net](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCClientH264Net): A receive only example that decodes a browser's H.264 video with SIPSorcery.H264, the pure C# port of OpenH264, and displays it. Open its `capture.html` in a browser to send a camera or test pattern stream to it.
- [WebRTCGetStartedH264Net](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCGetStartedH264Net): Sends a test pattern to a browser as H.264, encoded by SIPSorcery.H264, the pure C# port of OpenH264, so no native libraries are needed. Open its `getstarted.html` in a browser and press Start.
