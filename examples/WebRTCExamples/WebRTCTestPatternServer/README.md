**Usage**

This example generates a test pattern video stream and sends it to any WebRTC peers that connect.

**NOTE**: The test pattern is encoded using the separate `SIPSorceryMedia` library which is Windows specific.

You will need `.Net Core` installed.

- Start the test application on Windows using:

`dotnet run`

- Open the `webrtc.html` in a browser and click `Start` and the test pattern should appear.

- If you are feeling brave you can open `3x3.html` in a  browser for 9 separate Peer Connections.

![3x3 screenshot](3x3.png)