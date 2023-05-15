**Table of content**

[TOC]

---

####Description
In this example shown how to restream rtsp to the web. This example almos the same as [FFmpegToWebRTC](https://github.com/sipsorcery-org/sipsorcery/tree/master/examples/WebRTCExamples/FfmpegToWebRTC) except in this example we use both audio and video, with needful explanation how to demux rtsp to two RTP and after mux them to RTCPeerConnection. Test project you may running webrtc_tester.html file

---
####Sequence of doings:
1. Setup demuxer configuration
2. Run ffmpeg demuxer
3. Create and run ffmpeg listener
4. Create and run WebSocket signaling server

#####1. Setup demuxer configuration
In *DemuxerConfig* class declare settings neccessary for ffmpeg process  This class  is excess but let me explain the idea. This configuration using to declare ffmpeg arguments, and ffmpeg command template that will be run by [Process](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process?view=net-8.0) class.
In my case I used this command: 
>ffmpeg -use_wallclock_as_timestamps 1 -i {0} -map 0:v -c:v {1} -ssrc {2} -f rtp rtp://{3}:{4} -map 0:a -c:a {5} -ssrc {6} -f rtp rtp://{3}:{7}  -sdp_file {8} -y


DemuxerConfig has few command template, wich can be selected by initiating **outputStream** field.

#####2. Run ffmpeg demuxer
Now we run FFmpegListener, that run  [Process](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process?view=net-8.0) inside itself.
In ourcase (audio + video) streaming we got two RTP streams, we have to listen.

Be aware of that ffmpeg create global sdp file. (what [ffmpeg documentation](https://ffmpeg.org/ffmpeg.html) says ). Its mean that your audio and video tracks from rtsp stream description will write down at one file, what bring some juggling with SDP class definition i explain in [further](#####3. Create and run ffmpeg listener) step.
#####3. Create and run ffmpeg listener
Now the time to catch frames from RTPs created by ffmpeg. Inside *FFmpegListener* class we can find two RTPSession, one for audio another for video.  Important moment in WebRTC its track declaring. As I mentioned above ffmpeg create sdp file for both audio and video, and **crucial** understanding for me was that RTPSession should setted with only one track SDP description. So, the first we need to read global sdp file into SIPSorcery.Net.SDP , read announcments from it, and delete video track from SDP in case we creating SDP for audio description. This SDP used for SetRemoteDescription() method.

>var sdpAudio = SDP.ParseSDPDescription(File.ReadAllText(_dc.sdpPath));
            var videoAnn = sdpAudio.Media.Find(x => x.Media == SDPMediaTypesEnum.video);
            var audioAnn = sdpAudio.Media.Find(x => x.Media == SDPMediaTypesEnum.audio);
            sdpAudio.Media.Remove(videoAnn);

`_audioRTP.SetRemoteDescription(SIPSorcery.SIP.App.SdpType.answer, sdpAudio);`

**Result of this step:** we have two RTPSession (audio+video), each of which is defined by its own SDP.
#####4.  Create and run WebSocket signaling server
**Mark** that in this example i do not use STUN and TURN servers. I've tested solution in home LAN.
The role of the *WebSocketSignalingServer* class is create RTCPeerConnection and add tracks. Server get ssrc and format of track from *FFmpegListner* class. 
####Stucks you may faced

- OnRtpPacketReceived does not rises: 
In case I putted audio and video track in the RTPSession, the OnRtpPacketReceived invoked only for video, and skip audio track. This trouble force me to write this project with explanation of how to use. (my be i've wrong with library usage, but any way this is solution works)
 Solution: use one RTPSession for one track.
	
- Hight CPU usage:
For sure code may be optimized.
