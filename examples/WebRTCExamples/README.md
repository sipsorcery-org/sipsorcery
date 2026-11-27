The examples in this folder contain sample code to demonstrate common WebRTC cases including:


 - [WebRTCTestPatternServer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCTestPatternServer): The simplest example. This program serves up a test pattern video stream to a WebRTC peer.
 - [WebRTCServer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCServer): This example extends the test pattern example and can act as a media source for a peer. It has two source options:
   - An mp4 file.
   - Capture devices (webcam and microphone).
 The example includes an html file which runs in a Browser and will connect to a sample program running on the same machine.
- [WebRTCReceiver](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCReceiver): A receive only example. It attempts to connect to a WebRTC peer and display the video stream that it receives.
- [WebRTCClientH264Net](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCClientH264Net): A receive only example that decodes a browser's H.264 video with SIPSorcery.H264, the pure C# port of OpenH264, and displays it. Open its `capture.html` in a browser to send a camera or test pattern stream to it.
- [WebRTCGetStartedCodecs](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCGetStartedCodecs): Sends a test pattern and music to a browser, with the video codec, and the library that encodes it, chosen on the command line: vp8.net, h264.net or av1.net (managed, no native libraries), or FFmpeg for H264, H265, VP8, VP9 or AV1. It's an ASP.NET app that serves its own page, so run it and open http://localhost:8080. [WebRTCGetStarted](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCGetStarted) is the simpler version, with just vp8.net.
