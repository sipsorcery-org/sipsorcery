SIPSorcery
==========
This is not an official package of SIPSorcery. Official versions can be found at http://sipsorcery.codeplex.com

The SIPSorcery project is an experiment into the depths of the Session Initiation Protocol (SIP). The project is a combination of the source code available here on CodePlex and a live service hosted at https://www.sipsorcery.com/sipsorcery.html. The project has its roots in a previous mysipswitch project which has now been deprecated.

At its heart the project consists of a C# SIP protocol stack that implements all the required UDP, TCP and TLS transports. In addition to the SIP stack a number of related protocols: STUN, SDP, RTP &amp; RTCP are implemented to varying degrees, usually only insofar as they are required for operation of the sipsorcery.com service.

The SIP Proxy and SIP Application Server make heavy use of the Microsoft Dynamic Language Runtime with the IronRuby engine being heavily used in dialplan processing and the IronPython engine being used for the SIP Proxy control script.
