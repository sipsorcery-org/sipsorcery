# VP8.Net

This project is an attempt to port the [WebM](https://www.webmproject.org/) VP8 video codec to C#.

The motivation for the project is to provide a .NET video codec that does not require any native libraries for use in the sipsorcery real-time communications library.

**As of Mar 2021:**

 - VP8 decoder works but is very slow. A [demo program](https://github.com/sipsorcery-org/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCClientVP8Net) is available.
 - VP8 encoder is not yet ported.
