**Usage**

This example allows a test pattern video stream from `ffmpeg` to be transmitted to a WebRTC peer.

You will need `ffmpeg` and `.Net Core` installed.

- Start the test application using:

`dotnet run`

to change the default codec a command line option can be used:

`dotnet run -- <vp8|vp9|h264>`

- The application will write the required `ffmpeg` command line to the console.

````
Start ffmpeg using the command below and then initiate a WebRTC connection from the browser
ffmpeg -re -f lavfi -i testsrc=size=640x480:rate=10 -vcodec h264 -pix_fmt yuv420p -strict experimental -g 1 -f rtp rtp://127.0.0.1:5020 -sdp_file ffmpeg.sdp

Waiting for ffmpeg.sdp to appear...
````

 - Start `ffmpeg` in the same directory as the test application (or adjust the output path for the `ffmpeg.sdp` file) using the command from the console output:

`ffmpeg -re -f lavfi -i testsrc=size=640x480:rate=10 -vcodec h264 -pix_fmt yuv420p -strict experimental -g 1 -f rtp rtp://127.0.0.1:5020 -sdp_file ffmpeg.sdp`

- Open the `webrtc.html` in a browser and click `Start` and the `ffmpeg` test pattern should appear.

