Readme for SIPSorcery Softphone

Date: 18 Feb 2016
Author: Aaron Clauson
URL: https://github.com/sipsorcery/sipsorcery

The SIPSorcery softphone is a demo application to prototype
using .Net as a suitable runtime environment for a softphone application requiring
deterministic audio sampling and playback, it's not.

Settings that can be adjusted in the softphone.exe.config.

Audio:
------

The bad news is that the softphone is barely usable due to the inability of .Net to
reliably deliver audio samples from the microphone. What tends to happen is that as 
soon as incoming RTP packets start arriving the .Net runtime has periods where it's 
busy doing its garbage collection and other housekeeping and the microphone samples
jump from a 20ms period up to a 100 or 200ms period. The softphone supplied audio
will still be usable at the remote end of the call but it will be jumpy and possibly
have clicks and static.

I've only implemented support for the PCMU (G711 ULAW) codec so if the remote SIP 
device doesn't support it a call will not be possible.
As far as audio devices go the default input and output devices are used and there
is no user interface option facility to change that. Can be done via code.

Settings:
---------

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

Calling:
--------

The softphone supports SIP calls. For authenticated SIP calls check the desired radio button,
enter the destination number in the text box and click the Call button.

It's also possible to place anonymous SIP calls that won't use any preset SIP account information.
To place an anonymous SIP call check the SIP radio button and then enter a SIP address in the 
text box, e.g. music@iptel.org. The softphone will identify that it's a full SIP address and place
a call to it directly. 
