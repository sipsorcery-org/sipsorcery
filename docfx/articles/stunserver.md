## STUN Server

The [StunServer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/StunServer) contains an example of how to create a basic STUN server.

The example uses "classic STUN" as specified in [RFC3489](https://tools.ietf.org/html/rfc3489).

The example program can be tested with a STUN client. If using Ubuntu or Windows Subsystem for Linux (WSL) the shell commands below demonstrate how to test the STUN server:

````bash
$ sudo apt install stun-client
$ stun 127.0.0.1
STUN client version 0.97
Primary: Open
Return value is 0x000001
````

It's not useful to run the STUN server on the same network as the client. Placing the STUN server on an external host results in:

````bash
$ stun 67.222.131.146
STUN client version 0.97
Primary: Independent Mapping, Port Dependent Filter, random port, no hairpin
Return value is 0x000016
````

An alternative Windows only test program is [WinStun](https://sourceforge.net/projects/stun/files/WinStun/). It's somewhat old but since STUN is a very simple protocol it still works correctly.
The results of testing WinStun with the demo program:

````
Nat with Independend Mapping and Port Dependent Filter - VoIP will work with STUN
Does not preserve port number
Does not supports hairpin of media
Public IP address: 37.228.xxx.xxx
````



