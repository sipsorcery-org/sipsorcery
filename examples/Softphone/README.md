## SIPSorcery Softphone Demo Application

Date: 18 Feb 2016
Updated: 30 Nov 2024
Author: Aaron Clauson
URL: https://github.com/sipsorcery-org/sipsorcery/tree/master/examples/Softphone

The SIPSorcery softphone is a demo application for the SIP stack in the SIPSorcery library.
It supports audio calls and can also receive video calls. It is far from being
a production application and is intended to be used as a learning tool.

## Audio

~~The bad news is that the softphone is barely usable due to the inability of .Net to
reliably deliver audio samples from the microphone. What tends to happen is that as 
soon as incoming RTP packets start arriving the .Net runtime has periods where it's 
busy doing its garbage collection and other housekeeping and the microphone samples
jump from a 20ms period up to a 100 or 200ms period. The softphone supplied audio
will still be usable at the remote end of the call but it will be jumpy and possibly
have clicks and static.~~

~~I've only implemented support for the PCMU (G711 ULAW) codec so if the remote SIP 
device doesn't support it a call will not be possible.
As far as audio devices go the default input and output devices are used and there
is no user interface option facility to change that. Can be done via code.~~

Since the above was written the audio performance in .NET has improved a lot and is 
now usable. The softphone now supports the PCMU, PCMA and G722 audio codecs and the 
VP8 video codec.

## Settings

The settings live in `appsettings.json`, which is copied beside the executable on build.

STUNServerHostname - STUN is a protocol used to determine a machine's public IP address.
This STUN server setting needs to be a public STUN server so that the application can
determine its public IP. If it can't then there will almost certainly be audio issues
on some calls.

SIPUsername - Optional, the username for your SIP account.
SIPPassword - Optional, the password for your SIP account.
SIPServer - Optional, the host of your SIP server.
SIPFromName - Optional, the name you would like to appear as the display name on your SIP calls.
UseAudioScope - Optional, set to true to show the audio scope visualisation.
AudioOutDeviceIndex - Optional, the index of the audio output device to use. Defaults to -1
                      which means use the system default device.

The SIPSockets array can be used to configure the SIP transport layer. This is optional and
if it is left empty default values will be used. Each entry takes an Endpoint and an optional
Protocol of udp, tcp or tls. A tls entry also needs a CertificatePath, and optionally a
CertificateKeyPassword, pointing at a pkcs12 (.pfx) file.

````json
"SIPSockets": [
  { "Endpoint": "192.168.11.50:7060", "Protocol": "udp" }
]
````

### Settings file layering

The settings are loaded from the following sources, each one overriding the ones before it:

1. `appsettings.json`, the committed defaults. Required.
2. `appsettings.{Environment}.json`, e.g. `appsettings.Development.json`. Optional and git ignored.
3. Environment variables prefixed with `SOFTPHONE_`.

The environment name comes from the `DOTNET_ENVIRONMENT` environment variable. If that's not
set it defaults to `Development` for a Debug build and `Production` for a Release build, so
an `appsettings.Development.json` file is picked up when debugging without needing to set
anything. Any of these files placed beside the project file are copied next to the executable
on build.

Both override files are git ignored and are intended to hold your own credentials, so neither
should ever be committed. Only `appsettings.json` is in source control.

### Local settings override

The values in `appsettings.json` are the committed defaults. Rather than editing that file
with your own SIP credentials, and risking committing them, create an `appsettings.Development.json`
file beside it. That file is git ignored, is optional and only needs to contain the keys you
want to change:

````json
{
  "Softphone": {
    "SIPUsername": "your-username",
    "SIPPassword": "your-password",
    "SIPServer": "sip.example.com"
  }
}
````

If the file doesn't exist it is ignored and the defaults are used, so the softphone still
runs on a machine that has never been configured.

Settings can also be overridden with environment variables, which is handy for a one off run
or for keeping credentials out of a file altogether. Note the `SOFTPHONE_` prefix and the
double underscore separating the section from the key:

````
SOFTPHONE_Softphone__SIPPassword=your-password
````

## Calling

The softphone supports SIP calls. For authenticated SIP calls the SIP credentials in the previous section
can be used.

## Video Call Testing

A handy way to test the video calling feature of this application is to use the VideoPhoneCmdLine example
see, https://github.com/sipsorcery-org/sipsorcery/blob/master/examples/SIPExamples/VideoPhoneCmdLine/Program.cs.

Command line to use to place a video call to this softphone application:

````
dotnet run --dst=127.0.0.1:5060 --tp
````