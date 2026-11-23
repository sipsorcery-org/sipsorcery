# SIPSorcery.AV1

A realtime AV1 video encoder for [SIPSorcery](https://www.nuget.org/packages/SIPSorcery), built on [av1rt](https://github.com/BorgGames/av1rt), a managed port of libaom's realtime encoder.

```csharp
using SIPSorcery.AV1;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

var video = new VideoTestPatternSource(new AV1VideoEncoder());
var pc = new RTCPeerConnection();
pc.addTrack(new MediaStreamTrack(video.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly));
pc.OnVideoFormatsNegotiated += formats => video.SetVideoSourceFormat(formats.First());
video.OnVideoSourceEncodedSample += pc.SendVideo;
```
