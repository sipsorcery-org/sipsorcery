**Usage**

This example allows the stream from a remote WebRTC peer to be displayed in ffplay.

You will need `ffmpeg` and `.Net Core` installed.

- Start the test application using:

`dotnet run`

-  Open the `webrtc.html` in a browser and click the `Start` button.

 - The test application will write an `ffplay.sdp` file and then display the command to start `ffplay`.

 ````
 [21:20:33 DBG] RTPChannel for 127.0.0.1:63192 started.
v=0
o=- 1474529382 0 IN IP4 127.0.0.1
s=-
c=IN IP4 127.0.0.1
t=0 0
m=audio 5016 RTP/AVP 111
a=rtpmap:111 opus/48000/2
a=fmtp:111 minptime=10;useinbandfec=1
a=sendrecv
m=video 5018 RTP/AVP 98
a=rtpmap:98 VP8/90000
a=sendrecv

Start ffplay using the command below:
ffplay -probesize 32 -protocol_whitelist "file,rtp,udp" -i ffplay.sdp
To request the remote peer to send a video key frame press 'k'
````

 - In the same directory as the test application (or adjust the path to the `ffplay.sdp` file) start ffplay:

 `ffplay -probesize 32 -protocol_whitelist "file,rtp,udp" -i ffplay.sdp`

 - `ffplay` may display some warning messages while it waits for a key frame. In the test application console press `k` a few times to get the remote peer to generate a key frame.

- Once `ffplay` gets a keyframe a window should pop-up with the video and audio streams from the remote peer (check the task bar if it doesn't appear as sometimes it stays minimised).