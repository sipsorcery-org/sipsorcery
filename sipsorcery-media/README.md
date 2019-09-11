Building
---------------------

Use `vcpkg` to install the dependencies.

- Clone `vcpkg` from the [github repository](https://github.com/Microsoft/vcpkg) and install as per the instructions in the main README.md.
- Install the required packages:

```
    PS >.\vcpkg install --triplet x64-windows openssl libvpx ffmpeg libsrtp
```

Open `SIPSorcery.Media\SIPSorcery.Media.vcxproj` with Visual Studio.

Deployment
---------------------

The Visual C++ Runtime is required in order to be able to use the SIPSorceryMedia.dll. Install the latest version, x86 or x64 as appropriate, from:

https://support.microsoft.com/en-us/help/2977003/the-latest-supported-visual-c-downloads

The .Net Framewrok Runtime >= 4.7.2 is also required:

https://dotnet.microsoft.com/download/dotnet-framework/net472

Media Foundation
---------------------

To decode mp4 files on Windows Server 2008 R2:

Install the "Desktop Experience" feature: 
https://docs.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2008-R2-and-2008/cc772567(v=ws.11)

Install the h264/aac fix:
https://support.microsoft.com/de-ch/help/2483177/fix-you-cannot-play-back-an-h-264-video-file-or-an-aac-audio-file-on-a