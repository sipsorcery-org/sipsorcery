import clr
clr.AddReference('SIPSorcery.SIP.Core')
from SIPSorcery.SIP import *

m_registrarSocket = "udp:127.0.0.1:5001"
m_regAgentSocket = "udp:127.0.0.1:5002"
m_notifierSocket = "udp:10.249.142.143:5003"
m_proxySocketLoopback = "udp:127.0.0.1:5060"
m_proxySocketInternal = "udp:10.249.142.143:5060"

if isreq:
  
  #===== SIP Request Processing =====

  #sys.Log("req " + summary)
  req.Header.MaxForwards = req.Header.MaxForwards - 1

  if req.Header.UserAgent == "Cisco-CP7965G/8.5.3" or req.Header.UserAgent == "Cisco-CP7911G/8.5.3":
    req.Header.Vias.TopViaHeader.ViaParameters.Remove("rport")

  if sipMethod == "REGISTER":
    if remoteEndPoint.ToString() == m_regAgentSocket:
      # The registration agent has indicated where it wants the REGISTER request sent to by adding a Route header.
      # Remove the header in case it confuses the SIP Registrar the REGISTER is being sent to.
      destRegistrar = req.Header.Routes.PopRoute().ToSIPEndPoint()
      req.Header.Routes = None
      sys.SendTransparent(destRegistrar, req, publicip)
    else:
      sys.SendInternal(remoteEndPoint, localEndPoint, m_registrarSocket, req, proxyBranch, m_proxySocketLoopback)
    
  elif sipMethod == "SUBSCRIBE":
    sys.SendInternal(remoteEndPoint, localEndPoint, m_notifierSocket, req, proxyBranch, m_proxySocketLoopback)

  elif sipMethod == "NOTIFY":
    if IsFromAppServer or remoteEndPoint.ToString() == m_notifierSocket:
      # Request from a SIP Application or Notification server for an external user agent.
      SendExternalRequest(localEndPoint, req, proxyBranch, publicip, False)
    else:
      # A notification from an external notification server.
      sys.SendInternal(remoteEndPoint, localEndPoint, m_notifierSocket, req, proxyBranch, m_proxySocketLoopback)
  
  else:
    # All other requests are processed by the Application Server.
    if IsFromAppServer:
      # Request from a SIP Application Server for an external user agent.
      if req.Method.ToString() == "ACK" or req.Method.ToString() == "CANCEL" or req.Method.ToString() == "INVITE":
        SendExternalRequest(None, req, None, publicip, True)
      else:
        SendExternalRequest(localEndPoint, req, proxyBranch, publicip, False)
    else:
      # Request from an external user agent for an Application Server.
      dispatcherEndPoint = sys.DispatcherLookup(req)
      if dispatcherEndPoint != None:
        sys.SendInternal(remoteEndPoint, localEndPoint, dispatcherEndPoint.ToString(), req, proxyBranch, m_proxySocketInternal)
      else:
        appServer = sys.GetAppServer()
        if appServer != None:
          sys.SendInternal(remoteEndPoint, localEndPoint, appServer.ToString(), req, proxyBranch, m_proxySocketInternal)
        else:
          sys.Respond(req, SIPResponseStatusCodesEnum.BadGateway, "No sipsorcery app servers available")

  #===== End SIP Request Processing =====

else:

  #===== SIP Response Processing =====

  #sys.Log("resp " + summary)
 
  if sipMethod == "REGISTER" and remoteEndPoint.ToString() == m_registrarSocket:
    # REGISTER response from SIP Registrar.
    sys.SendExternal(resp, outSocket)

  elif sipMethod == "REGISTER":
    # REGISTER response for SIP Registration Agent.
    sys.SendTransparent(remoteEndPoint, localEndPoint, resp, SIPEndPoint.ParseSIPEndPoint(m_proxySocketInternal), m_regAgentSocket, topVia.Branch)

  elif sipMethod == "NOTIFY" or sipMethod == "SUBSCRIBE":
    if remoteEndPoint.ToString() != m_notifierSocket and not IsFromAppServer:
      # Responses for SIP Notifier Server.
      sys.SendInternal(remoteEndPoint, localEndPoint, resp, outSocket)
    else:
      # Subscribe and notify responses for external user agents.
      SendExternalResponse(resp, outSocket, publicip)

  else: 
    if IsFromAppServer:
      # Response from an Application Server for an external UA.
      SendExternalResponse(resp, outSocket, publicip)
    else:
      # Responses from external UAs for SIP Application Servers.
      if resp.Header.CSeqMethod.ToString() == "ACK" or resp.Header.CSeqMethod.ToString() == "CANCEL" or resp.Header.CSeqMethod.ToString() == "INVITE":
        dstEndPoint = sys.DispatcherLookup(resp)
        if dispatcherEndPoint == None:
          dstEndPoint = sys.GetAppServer()
        sys.SendTransparent(remoteEndPoint, localEndPoint, resp, SIPEndPoint.ParseSIPEndPoint(m_proxySocketInternal), dstEndPoint, topVia.Branch)
      else:
        sys.SendInternal(remoteEndPoint, localEndPoint, resp, SIPEndPoint.ParseSIPEndPoint(m_proxySocketInternal))

  #===== End SIP Response Processing =====


# Attempts to send a SIP request to an external user agent by first resolving the destination for the 
# request and then checking whether it is on the same subnet.
#def SendExternalRequest(SIPEndPoint receivedOn, SIPRequest req, String proxyBranch, IPAddress publicIP, bool sendTransparently):
def SendExternalRequest(receivedOn, req, proxyBranch, publicIP, sendTransparently):  
  dest = sys.Resolve(req)
  if dest == None:
    if req.Method != "ACK":
      sys.Respond(req, SIPResponseStatusCodesEnum.DoesNotExistAnywhere, "Host " + req.URI.Host + " unresolvable")
  elif IsLocalNetDestination(dest):
    #sys.Log("Request destination " + dest.ToString() + " determined as local network.")
    if sendTransparently:
      sys.SendTransparent(dest, req, None) 
    else:
      sys.SendExternal(receivedOn, dest, req, proxyBranch, None) 
  else:
    #sys.Log("Request destination " + dest.ToString() + " determined as external network.")
    if sendTransparently:
      sys.SendTransparent(dest, req, publicIP) 
    else:
      sys.SendExternal(receivedOn, dest, req, proxyBranch, publicIP)

#def SendExternalResponse(SIPResponse resp, SIPEndPoint sendFromSIPEndPoint, IPAddress publicIP):
def SendExternalResponse(resp, sendFromSIPEndPoint, publicIP):
  dest = sys.Resolve(resp)
  #sys.Log("SendExternalResponse desination resolved to " + dest.ToString() + ".")
  if dest == None:
    sys.Log("The destination could not be resolved for a SIP response.")
    sys.Log(resp.ToString())
  elif IsLocalNetDestination(dest):
    sys.SendExternal(resp, sendFromSIPEndPoint)
  else:
    sys.SendExternal(resp, sendFromSIPEndPoint, publicIP)

# Determines if a destination IP address is on the same local subnet or whether it's 
# on an external network.
#def IsLocalNetDestination(SIPEndPoint destinationAddress) :
def IsLocalNetDestination(destinationEP) :
  return False
 