SIPSorcery: [![Build status](https://ci.appveyor.com/api/projects/status/github/sipsorcery/sipsorcery?svg=true)](https://ci.appveyor.com/project/sipsorcery/sipsorcery) SIPSorceryMedia: [![Build status](https://ci.appveyor.com/api/projects/status/github/sipsorcery/sipsorcery?svg=true)](https://ci.appveyor.com/project/sipsorcery/sipsorcery-9ql6k)

The SIPSorcery project is an experiment into the depths of the Session Initiation Protocol (http://www.ietf.org/rfc/rfc3261.txt). The project is a combination of the source code available here on GitHub (previously sourceforge & codeplex) and a live service hosted at http://www.sipsorcery.com/. 
The project has its roots in a previous project called mysipswitch (http://www.codeplex.com/Wikipage?ProjectName=mysipswitch) which was deprecated in the mid noughties.

At its heart the project consists of a C# SIP protocol stack that implements all the required UDP, TCP and TLS transports. In addition to the SIP stack a number of related protocols: STUN (http://tools.ietf.org/html/rfc3489], SDP (http://tools.ietf.org/html/rfc4566), RTP & RTCP (http://tools.ietf.org/html/rfc3550) are implemented to varying degrees.

The SIP Proxy and SIP Application Server make heavy use of the Microsoft Dynamic Language Runtime (http://dlr.codeplex.com/) with the IronRuby (http://ironruby.codeplex.com/) engine being heavily used in dialplan processing and the IronPython (http://ironpython.codeplex.com/) engine being used for the SIP Proxy control script.

There are 3 main areas the SIPSorcery project focuses on:

The core SIP protocol stack and associated plumbing code, contained in the sipsorcery-core source code directory.
The SIP server applications are:
- SIP Proxy with dispatching mechanism for application server fault tolerance,
- SIP Registrar,
- SIP Registration Agent, registers contact information with 3rd party SIP providers,
- SIP Application Server, multi-user SIP call processing using Ruby dialplans,
- SIP Notification Server, supports the presence and dialog SIP event package notifications,
- SIP Monitoring Server, receives and collates log messages from the other servers that can then be viewed from a web page or SSH session,
- WatchTower Server, monitors SIP Application Servers and updates the SIP Proxy dispatch file,
- SSH Server, uses the [url:NSsh|http://nssh.codeplex.com/] project to provide a multi-user SSH session for server monitoring.
- An end-user Silverlight client application for managing the sipsorcery.com service, contained in the sipsorcery-silverlight code directory,
- The SIP protocol stack is able to run within Silverlight allowing SIP TCP communications directly from a browser. 
- A basic C# softphone example application.

Service at: http://www.sipsorcery.com/.<br/>
Blog at: http://blog.sipsorcery.com/.<br/>
Forum at: http://forum.sipsorcery.com/index.php.<br/>
Twitter:  http://twitter.com/sipsorcery.<br/>
NuGet: https://www.nuget.org/packages/SIPSorcery/.
