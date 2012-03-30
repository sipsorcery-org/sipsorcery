Readme for SIPSorcery Softphone

Date: 30 mar 2012
Author: Aaron Clauson
URL: http://sipsorcery.codeplex.com/

The SIPSorcery softphone is a demo (note the "demo") application to prototype
using .Net as a suitable runtime environment for a softphone application requiring
deterministic audio sampling and playback, it's not. And also to prototype placing
calls via Google Voice's XMPP gateway, this works well.

As far as using the softphone goes there are settings that can be entered in the
sipsorcery-softphone.exe.config (sorry I was too lazy to do a menu screen for the
settings so you'll have to use a text editor).

Audio:
------

The bad news is that the softphone is barely usable due to the inability of .Net to
reliably deliver audio samples from the microphone. What tends to happen is that as 
soon as incoming RTP packets start arriving the .Net runtime has periods where it's 
busy doing its garbage collection and other housekeeping and the microphone samples
jump from a 20ms period up to a 100 or 200ms period. The softphone supplied audio
will still be usable at the remote end of the call but it will be jumpy and possibly
have clicks and static.

I've only implemented support for the PCMU (G711 UALW) codec so if the remote SIP 
device doesn't support it a call will not be possible (Google Voice supports PCMU).
As far as audio devices go the default input and output devices are used and there
is no facility to change that (laziness again sorry).

Settings:
---------

STUNServerHostname - STUN is a protocol used to determine a machine's public IP address.
This STUN server setting needs to be a public STUN server so that the application can
determine its public IP. If it can't then there will almost certainly be audio issues
on some calls.

GoogleVoiceUsername and GoogleVoicePassword - The same details you use to login to the 
Google Voice web site. They will allow VoIP calls to be placed through Google Voice's
XMPP gateway.

SIPUsername - The username for your SIP account.
SIPPassword - The password for your SIP account.
SIPServer - The host of your SIP server.
SIPFromName - The name you would like to appear as the display name on your SIP calls.

The sipsockets node can be used to configure the SIP transport layer. This is optional 
and if the node is left commented then default values will be used.

Calling:
--------

The softphone supports two types of calls SIP and Google Voice. For Google Voice and
authenticated SIP calls simple check the desired radio button, enter the destination number 
in the text box and click the Call button.

It's also possible to place anonymous SIP calls that won't use any preset SIP account information.
To place an anonymous SIP call check the SIP radio button and then enter a SIP address in the 
text box, e.g. music@iptel.org. The softphone will identify that it's a full SIP address and place
a call to it directly using anonymous details. 
