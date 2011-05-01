SIPSwitch Service Readme
------------------------

Date: 24 Sep 2008
Author: Aaron Clauson (aaron@mysipswitch.com)
URL: http://www.mysipswitch.com
Source Code: http://www.codeplex.com/mysipswitch (this release is revision 24409)
Further Help: http://www.mysipswitch.com/forum/index.php

Updates:
--------

- 24 Sep 2008, two bugs fixed in BlueFace.VoIP.SIPServerCores, re-released as version v0.2.1


Description:
------------

This readme provides some operating instructions for running a local version
of the SIPSwitch. The local version is identical to the version running at
sip.mysipswitch.com with functionality wise but the deployment method will
most likely be different depending on the network environment.

The sipswitch is a multi-function SIP Only Server, i.e. it deals with SIP signalling only
and does not get involved in any media processing. The SIP functions the sipswitch
provides are:

- Stateless SIP Proxy; acts as the optional front for other SIP server agents allowing
different servers to receive and originate traffic from the same public sockets,

- SIP Application Server; accepts calls from Client User Agents and forwards to external
SIP servers according to a user configurable dial plan,

- SIP Registrar Server; accepts registrations from Client User Agents. The contact 
recorded can be used to forward calls received by the sipswitch for the user,

- SIP Registration Agent; acts as a Client User Agent to register contacts with 
external SIP Registrars,

- Web Service Provider; allows the sipswitch Daemon to be queried for real-time 
updates on sipswitch status and activty,

- Telnet SIP Trace: allows telnet connections to the sipswitch Daemon to be
connected to in a console type fashion and events and traffic displayed.

- STUN Server.

Audience:
---------

The SIPSwitch software is targeted for programmers or technical users that
are comfortable with XML files and have a general understanding of SIP. It is
not recommended for end users looking for a simple VoIP PBX type solution. There
is no GUI and configuration and operation rely on the manipulation of XML files 
or an SQL database. In addition a good general understanding of how SIP works 
is essential since deploying SIP servers on private IP addresses behind NATs 
comes with a number of challenges.

Deployment:
-----------

It's possible to configure the SIPSwitch software to run one, multiple or all the 
server agents listed above. For the simplest deployment approach all agents except the
SIP Proxy can be enabled. This will provide all the functionality but avoid the 
complexity of understanding the relationship between the SIP Proxy and the Application
Server. The Application Server will actually carry out all the functions the SIP Proxy
carries out the main differnence being that for large deployments the Proxy adds
superior scalability and reliability. For small local deployments the SIP Proxy is overkill.

Files:
------

This release comes with two executables one of which is a console application
(and is suitable for running on Windows or Linux) and the other is a Windows
service application that is suitable for Windows only.

- sipswitchconsole.exe is the console application
- sipswitchservice.exe is the Windows Service (to install installutil sipswitchservice.exe)

The configuration file is broken into a number of sub-sections where each sub-section
relates to one of the SIP Server Agents. The settings that are available for each
sub-section and their purpose are briefly described below. In addition because a 
number of settings are common to one or more agents and because one, multiple or all
the agents can run in a single process it's possible that configuration file settings
may end up being duplicated. This will not cause any problems but where duplication
does occur the settings should be the same.

The settings below must all be contained in the App.Config file <appSettings> element.

- Persistence:

    <add key="ProxyDBStorageType" value="XML"/>
    <add key="ProxyDBConnStr" value="E:\Temp\sipswitch\allinone\config\" />

    or
    
    <add key="ProxyDBStorageType" value="Postgresql"/>
    <add key="ProxyDBConnStr" value="Server=localhost;Port=5432;User Id=username;Password=password;Database=sipswitch;MaxPoolSize=3;MinPoolSize=0;Timeout=5;Encoding=UNICODE;"/>
    
 ProxyDBStorageType: The type of persistence layer being used.
 ProxyDBConnStr: The connection information for the persistence layer.
 
- Telnet monitor agent:

    <!-- Monitor Settings-->
    <add key="MonitorEventPort" value="10002" />
    <add key="MonitorListenSocket" value="10.0.0.100:4502" />
    <add key="SuperUsername" value="user" />
    
MonitorEventPort: Loopback port that the monitor will listen on for events from other agents.
MonitorListenSocket: TCP socket the monitor will listen on for user telnet connections. Set to 0 to disable the monitor agent.
SuperUsername: The username of the admin user. The password for this user will be verified against a SIP account of the same name.    
               The admin user does not have any restriction over the filter that can be set on the telnet monitor.
               
- Stateless SIP Proxy:
       
    <!-- Stateless SIP Proxy Settings -->
    <add key="ProxyEnabled" value="true" />
    <add key="MonitorEventPort" value="10002" />
    <add key="ProxyScriptPath" value="proxyscript.py" />
    <add key="NATKeepAliveListenerSocket" value="127.0.0.1:11006" />
    <add key="STUNSecondarySocket" value="127.0.0.1:3348"/>
    
ProxyEnabled: Set to true to enable the agent, false to disable.
MonitorEventPort: Loopback port to send events for the monitor agent.
ProxyScriptPath: Path to the file for the proxy's runtime script (equivalent of the proxy's dial plan). If running as a Windows Service path must be fully qualified.
NATKeepAliveListenerSocket: Socket to listen on for requests to send NAT keep-alives to user agents. The SIP Registrar will make use of this.
STUNSecondarySocket: The secondary socket for the STUN server. The primary socket will be the Proxy's first SIP socket so ideally this secondary port should be a different IP address.

- Application Server:

    <!-- SIP App Server Settings -->
    <add key="AppServerEnabled" value="true" />
    <add key="MonitorEventPort" value="10002" />
    <add key="ProxyLogStorageType" value="Postgresql"/>
    <add key="ProxyLogDBConnStr" value="Server=localhost;Port=5432;User Id=username;Password=password;Database=sipswitch;MaxPoolSize=3;MinPoolSize=0;Timeout=5;Encoding=UNICODE;"/>
    <add key="MangleClientContact" value="True" />
    <add key="LogAll" value="False" /> 
    <add key="TraceDirectory" value="E:\Temp\sipswitch\allinone\" />
    <add key="RubyScriptCommonPath" value="E:\Temp\sipswitchrelease\dialplan-common.rby"/>
    <!--<add key="OutboundProxySocket" value="213.200.94.182:5061" />-->
    <add key="BetamaxServersPattern" value="(voipstunt|voipcheap|voipbuster)"/>  
    <add key="WebServiceEnabled" value="true" />
    <add key="NATKeepAliveListenerSocket" value="127.0.0.1:11006" />
    <add key="STUNSecondarySocket" value="127.0.0.1:3348"/>
    
AppServerEnabled: Set to true to enable the Application Server, false to disable.
MonitorEventPort: Loopback port to send events for the monitor agent.
ProxyLogStorageType: If monitor events and/or CDR should be logged the type of storage (only Postgresql supported).
ProxyLogDBConnStr: The connection information for the log storage layer.
MangleClientContact: Set to true if the Application server should mangle any private IP addresses it receives in Contact headers or SDP messages.
LogAll: If set to true all SIP packets will be written to the log file and console. Only suitable for low volume traffic. 
TraceDirectory: The directory to store dial plan traces in.
RubyScriptCommonPath: If user dial plans should include some common functionality it can be stored in this file.
OutboundProxySocket: If set all outgoing calls from the Application server will be sent via this socket. Typically it is the socket of a Stateless SIP Proxy.
BetamaxServersPattern: Any outgoing calls to hosts that match this regular expression will have the P-src-ip header added.
WebServiceEnabled: Set to true to enable the web server in the process that allows web service access to the Application Server operations.
NATKeepAliveListenerSocket: Socket to listen on for requests to send NAT keep-alives to user agents. The SIP Registrar will make use of this.
STUNSecondarySocket: The secondary socket for the STUN server. The primary socket will be the Proxy's first SIP socket so ideally this secondary port should be a different IP address.

SIP Registrar:

    <!-- Registrar Settings -->
    <add key="RegistrarEnabled" value="true" />
    <add key="MonitorEventPort" value="10002" />
    <add key="RegistrarContactsPerUser" value="11" /> <!-- The number of registrations per account that the SIP Registrar Server will allow after which the oldest one will be discarded. -->
    <add key="RegistrarUseDownstreamProxy" value="true" />
    <add key="RegistrarSocket" value="127.0.0.1:5001"/>
    <add key="NATKeepAliveListenerSocket" value="127.0.0.1:11006" />

RegistrarEnabled: Set to true to enable the SIP Registrar agent, false to disable
MonitorEventPort: Loopback port to send events for the monitor agent.
RegistrarContactsPerUser: The maximum number of bindings to accept per SIP account.
RegistrarUseDownstreamProxy: If set to true the Registrar will record the proxy socket the REGISTER request arrived from to allow calls to go back the same way.
RegistrarSocket: The socket the Registrar should operate from.
NATKeepAliveListenerSocket: The socket to send NAT keep alive requests to. Typically this will be hosted on the SIP Proxy.

Registration Agent:

    <!-- Registration Agent Settings -->
    <add key="RegistrationAgentEnabled" value="true" />
    <add key="MonitorEventPort" value="10002" />
    <add key="RegistrationAgentSocket" value="127.0.0.1:5002" /> 
    <add key="RegistrationAgentProxySocket" value="10.0.0.100:5060" />
    <add key="WebServiceEnabled" value="true" /> 
    
RegistrationAgentEnabled: Set to true to enable the registration agent, false to disable.
MonitorEventPort: Loopback port to send events for the monitor agent.
RegistrationAgentSocket: The socket the registration agent should operate from.
RegistrationAgentProxySocket: If the registration agent should send all 3rd party registrations via a separate SIP Proxy this socket needs to be set to the proxy's address.
WebServiceEnabled: Set to true to enable the web server in the process that allows web service access to the Registration Agent operations.

In addition there are some agents which have dedicated XML nodes that must be placed in the <configuration> node of the App.Config file.

- Stateless SIP Proxy
  
  <sipstatelessproxy>
    <sipsockets>
      <socket>10.0.0.100:5060</socket>
      <socket>10.0.0.100:5085</socket>
      <socket protocol="tcp">10.0.0.100:5060</socket>
    </sipsockets>
   </sipstatelessproxy>
   
Note: The channels that the SIP Proxy will create and listen on.

- Application Server

  <sipappserver>
    <sipsockets>
      <socket>10.0.0.100:5061</socket>
    </sipsockets>
   </sipappserver>

Note: The channels that the Application Server will create and listen on.

- SIP Registrar

  <!-- Used to decide what expiry time the registrar will set for useragents. -->
  <sipregistrar>
    <useragents>
        <useragent expiry="3600" contactlists="true">fring</useragent>
 	    <useragent expiry="3600" contactlists="false">Nokia</useragent>
        <useragent expiry="60" contactlists="false">Cisco-CP7960G/8.0</useragent>
        <useragent expiry="113">.*</useragent>
    </useragents>
  </sipregistrar>
  
Note: When a REGISTER request is received the Registrar will match the User-Agent string against the list in this node to determine
the maximum expiry it will allow and also whether the full list of Contact bindings will be returned in the response, as the standard
describes but that breaks some agents, or whether it will send back an identical Contact header to the one that arrived in the REGISTER
request.  

Configuration File:
------------------

The settings that determine the method of operation of the two executables are 
contained in each processes App.Config file. The name of that file is the same
as the name of the executable but with a .config appended. The settings in the
App.Config file determine which of the SIP Servers will run in the process and 
the configuration of each of those servers. This is different from the previous
versions of the local SIPSwitch where it was not possible to turn on and off 
different agents.

For example if you only require the Registration Agent to register contacts with 
your providers you do not need to run the Application, Registrar or other servers.
On sip.mysipswitch.com each of the main servers is installed as a separate Windows
Service but for local installs with much smaller loads this is not necessary and
it's usually a lot easier to run all the required agents in the same process.

Persistence Options:
--------------------

There are two options available for the SIPSwitch's persistence layer.
One is an SQL database (only tested with Postgresql but others may work) and the 
other is XML files. For local installs the XML file approach is much more manageable.
The format of the XML files and database tables in the SQL database are very close
so understanding one will translate to the other.

Since this readme is targeted for local versions the XML format is documented.

The 4 XML configuration files are:
 
- dialplans.xml which is where the Ruby or exten dial plans will be loaded from
for outgoing calls by the SIP Application Server,

- domains.xml which is where the list of domains that are serviced by the Application
and Registrar servers will be loaded from,

- extensions.xml contains a list of extensions that will be checked by the Application
server when a call arrives that is not for a known SIP account,

- sipaccounts.xml contains a list of SIP Accounts that are used by the Application
and Registrar Servers for authentication. The Monitor server will also use this
file to authenticate connections to the telnet monitor port if active,

- sipproviders.xml contains the SIP Provider settings for local users. The providers
are used by the Application and Registration Agent Servers.

examples:

dialplans.xml:

<dialplans>
 <dialplan username='user'>
  <![CDATA[#Ruby
sys.Log("call to #{req.URI.ToString()} from #{req.Header.From.ToString()}")
sys.Dial("blueface")
  ]]>
 </dialplan>
</dialplans>

Note: The username needs to uniquely match a SIP Account username in sipaccounts.xml.

domains.xml:

<domains>
 <domain>
  <value>10.0.0.100</value>
  <alias>10.0.0.100:5060</alias>
  <alias>local</alias>
 </domain>
 <domain>
  <value>mydomain</value>
 </domain>
</domains>

Note: The values entered into this file are critical as they are used to determine whether
a SIP account is local and requires authentication or is from the outside. In other words
it's used to decide what belongs SIPSwitch instance and what doesn't. When SIP Accounts are
created in sipaccounts.xml they MUST be created with a domain value that equals one of the 
VALUES in the domains.xml file otherwise it will not be recognised.

extensions.xml:

<extensions>
 <extension domain='10.0.0.100' extension='1234'>user</extension>
</extensions>

Note: Extensions are a way of associating an incoming call with a dial plan rather than a 
SIP Account. For example if extension 1234 is called it will be processed by the dial plan
belonging to user. 

sipaccounts.xml:

<sipaccounts>
 <sipaccount>
  <sipusername>user</sipusername>
  <sippassword>password</sippassword>
  <domain>10.0.0.100</domain>
 </sipaccount>
</sipaccounts>

Note: The SIP Accounts listed in this XML file are core to the operation of the Application
and Registrar Servers. The domain value MUST correspond to a domain VALUE (not alias) from
the sipdomains.xml file. The credentials are used to authenticate calls and registrations
to the SIPSwitch instance. The sipusername MUST match a dial plan entry with the same username
for dialplan use (the dialplan entries currently do not support domains).

sipproviders.xml:

<sipproviders>
 <sipprovider>
  <owner>user</owner>
  <providername>blueface</providername>
  <providerusername>user</providerusername>
  <providerpassword>password</providerpassword>
  <providerserver>sip.blueface.ie</providerserver>
  <registerenabled>true</registerenabled>
  <registercontact>sip:user@10.0.0.100</registercontact>
 </sipprovider>
</sipproviders>

Note: The SIP Provider entries are used by the Application and Registration Agent Servers. The 
Registration Agent Server is interested in any entries that have registerenabled set to true. 
It will attempt to register the contact with the provider for each entry that it finds like that.
The Application Server uses SIP Providers in its dialplan processing. As an example the dialplan
command below would attempt to locate a SIP Provider entry called "provider1" and forward the
call using the settings for it.

sys.Dial("${dst}@provider1")

