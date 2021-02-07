### Overview

This example is intended to demonstrate a WebRTC peer connection test between a .NET Core Console application and the [Janus WebRTC Server](https://github.com/meetecho/janus-gateway) [Echo Test Plugin](https://janus.conf.meetecho.com/docs/echotest.html).

### Instructions

To use this program the Janus REST API must be accessible.

To run the example follow the steps below in order.

 - Edit the `Program.cs` file and set the `JANUS_BASE_URI` constant,
 - Start the .NET Core Console application:
   - dotnet run
- The Console application should start and a Windows Forms window should open,
- The top picture box displays the test pattern from the .NET Core application. If the WebRTC connection to Janus succeeds the bottom picture box will display the echoed test pattern from the Janus server.
- Press enter in the Console application to close the Janus session and exit the application.
