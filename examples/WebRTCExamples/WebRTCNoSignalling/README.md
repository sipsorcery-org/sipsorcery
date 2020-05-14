### Overview

This example is intended to allow a WebRTC peer connection test between a .Net Core Console application and a WebRTC Browser WITHOUT needing any signalling.

To avoid the need for signalling a number of manual steps are required in order to exchange the information each peers needs for the Session Description offer and answer.

### Instructions

To run the example follow the steps below in order.

 - Start the .Net Core Console application:
   - cd examples\WebRTCNoSignalling
   - dotnet run
- The Console application should display an SDP offer as shown below:
````
examples\WebRTCNoSignalling>dotnet run
WebRTC No Signalling Server Sample Program
Press ctrl-c to exit.
Initialising OpenSSL and libsrtp...
Using WebM Project VP8 Encoder v1.8.1
Starting Peer Connection...
[11:14:30 DBG] CreateRtpSocket start port using OS default ephemeral port range on ::.
[11:14:30 DBG] Successfully bound RTP socket on [::]:52259 (dual mode True).
[11:14:31 DBG] v=0
o=- 35588 0 IN IP4 127.0.0.1
s=-
t=0 0
a=group:BUNDLE 0
m=video 9 RTP/SAVP 100
c=IN IP4 0.0.0.0
a=ice-ufrag:WFEG
a=ice-pwd:NIWGINQKDLYWXDABENRYNGBE
a=fingerprint:sha-256 C6:ED:8C:9D:06:50:77:23:0A:4A:D8:42:68:29:D0:70:2F:BB:C7:72:EC:98:5C:62:07:1B:0C:5D:CB:CE:BE:CD
a=candidate:2306 1 udp 659136 fe80::910e:8bfe:e7e3:7919%52 52259 typ host generation 0
a=candidate:2337 1 udp 659136 172.20.16.1 52259 typ host generation 0
a=candidate:2441 1 udp 659136 fe80::54a9:d238:b2ee:ceb%21 52259 typ host generation 0
a=candidate:2944 1 udp 659136 192.168.11.50 52259 typ host generation 0
a=ice-options:ice2,trickle
a=mid:0
a=rtpmap:100 VP8/90000
a=rtcp-mux
a=rtcp:9 IN IP4 0.0.0.0
a=setup:actpass
a=sendrecv

^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
THE SDP OFFER ABOVE NEEDS TO BE PASTED INTO YOUR BROWSER

Press enter when you the SDP answer is available and then enter the ICE username and password...
````
 - Copy the SDP offer into the `offerSDP` variable in the `webrtc-nosignalling.html` file (the SDP offer starts as `v=0...a=sendrecv`),
 - Open the `webrtc-nosignalling.html` with a WebRTC enabled browser and open the debug console with ctrl-shift-i and click `Start`,
 - The Browser peer connection should print diagnostics messages including the SDP answer,
 - Go back to the Console application and press Enter to provide the ICE username and password from the Browser,
````
Enter the remote peer ICE User (e.g. for 'a=ice-ufrag:tGXy' enter tGXy) =>
````
 - Copy the ICE username from the SDP answer in the Browser. In the example below:
  - The ICE username that needs to be provided to the Console application is: `aQfz`
  - The ICE password that needs to be provided to the Console application is: `G1idYvwZ/eXix4mUj7yqXlUE`
 
````
answer SDP: v=0
o=- 8577810580265776401 2 IN IP4 127.0.0.1
s=-
t=0 0
a=group:BUNDLE 0
a=msid-semantic: WMS
m=video 9 RTP/SAVP 100
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:aQfz
a=ice-pwd:G1idYvwZ/eXix4mUj7yqXlUE
a=ice-options:trickle
a=fingerprint:sha-256 1F:90:20:81:D6:E7:12:8C:21:65:C6:15:1A:1F:D4:5F:68:6A:07:6D:7E:A9:85:2E:00:5D:8E:48:2E:C5:C3:F6
a=setup:active
a=mid:0
a=recvonly
a=rtcp-mux
a=rtpmap:100 VP8/90000
````

- The peer connection between the Console and the Browser should now connect after a few seconds and a test pattern video should be displayed in the Browser.
