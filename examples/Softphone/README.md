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
VP8 video codec..

## Settings

STUNServerHostname - STUN is a protocol used to determine a machine's public IP address.
This STUN server setting needs to be a public STUN server so that the application can
determine its public IP. If it can't then there will almost certainly be audio issues
on some calls.

SIPUsername - Optional, the username for your SIP account.
SIPPassword - Optional, the password for your SIP account.
SIPServer - Optional, the host of your SIP server.
SIPFromName - Optional, the name you would like to appear as the display name on your SIP calls.
DnsServer - Optional, a custom DNS server to use. Relevant if default DNS server does not resolve 
            SIP SRV records 

The sipsockets node can be used to configure the SIP transport layer. This is optional 
and if the node is left commented then default values will be used.

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