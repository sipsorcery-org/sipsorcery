import clr
clr.AddReference('SIPSorcery.SIP.Core')
from SIPSorcery.SIP import *

m_registrarSocket = "udp:127.0.0.1:5001"
m_regAgentSocket = "udp:127.0.0.1:5002"
m_proxySocketForRegisters = "udp:127.0.0.1:5060"
m_proxySocketInternal = "udp:10.1.1.5:5060"
m_appServerSocket = "udp:10.1.1.5:5065"
m_privateNetPrefix = "10."

if isreq:
  
  #===== SIP Request Processing =====

  #sys.Log("req " + summary)
  req.Header.MaxForwards = req.Header.MaxForwards - 1
  
  if sipMethod == "REGISTER":
    if remoteEndPoint.ToString() == m_regAgentSocket:
      # A couple of registrars get confused with multiple Via headers in a REGISTER request.
      # To get around that use the branch from the reg agent as the branch in a request that only has one Via.
      branch = req.Header.Vias.PopTopViaHeader().Branch

      # The registration agent has indicated where it wants the REGISTER request sent to by adding a Route header.
      # Remove the header in case it confuses the SIP Registrar the REGISTER is being sent to.
      destRegistrar = req.Header.Routes.PopRoute().ToSIPEndPoint()
      req.Header.Routes = None
      sys.SendTransparent(destRegistrar, req, branch, publicip)
          
    else:
      req.Header.ProxyReceivedOn = localEndPoint.ToString()
      req.Header.ProxyReceivedFrom = remoteEndPoint.ToString()
      sys.Send(m_registrarSocket, req, proxyBranch, m_proxySocketForRegisters)
    
  elif sipMethod == "SUBSCRIBE":
    sys.Respond(req, SIPResponseStatusCodesEnum.MethodNotAllowed, None)

  else:
     
    if remoteEndPoint.ToString() == m_appServerSocket:
      # Request from a SIP Application server for an external UA.
      dest = sys.Resolve(req)
      if dest == None:
        if sipMethod != "ACK":
          sys.Respond(req, SIPResponseStatusCodesEnum.DoesNotExistAnywhere, "Host " + req.URI.Host + " unresolvable")
      else:
        # Request from app server for external UA.
        branch = req.Header.Vias.PopTopViaHeader().Branch
        if dest.SocketEndPoint.Address.ToString().StartsWith(m_privateNetPrefix):
          # Request is for same private network as the proxy, don't use external public IP.
          sys.SendTransparent(dest, req, branch, None)  
        else:
          # Request is for an external UA, use public IP.
          sys.SendTransparent(dest, req, branch, publicip)

    else:

      # Request from an external UA for a SIP Application Server
      req.Header.ProxyReceivedOn = localEndPoint.ToString()
      req.Header.ProxyReceivedFrom = remoteEndPoint.ToString()
      sys.Send(m_appServerSocket, req, proxyBranch, m_proxySocketInternal)

  #===== End SIP Request Processing =====

else:

  #===== SIP Response Processing =====

  #sys.Log("resp " + summary)
 
  if sipMethod == "REGISTER" and remoteEndPoint.ToString() == m_registrarSocket:
    # REGISTER response from SIP Registrar.
    resp.Header.ProxyReceivedOn = None
    resp.Header.ProxyReceivedFrom = None
    sys.Send(resp, outSocket)

  elif sipMethod == "REGISTER":
    # REGISTER response for SIP Registration Agent.
    resp.Header.Vias.PushViaHeader(SIPViaHeader(SIPEndPoint.ParseSIPEndPoint(m_regAgentSocket), topVia.Branch))  
    sys.Send(resp, SIPEndPoint.ParseSIPEndPoint(m_proxySocketForRegisters))

  elif remoteEndPoint.ToString() == m_appServerSocket:
    resp.Header.ProxyReceivedOn = None
    resp.Header.ProxyReceivedFrom = None
    resp.Header.ProxySendFrom = None
    # Responses from SIP Application Servers for external UA's.    
    if outSocket.SocketEndPoint.Address.ToString().StartsWith(m_privateNetPrefix):
      sys.Send(resp, outSocket, None)
    else:
      # Response from a SIP Application Server destined for external network.
      sys.Send(resp, outSocket, publicip)

  else: 
    # Responses from external UAs for SIP Application Servers.

    dstEndPoint = m_appServerSocket

    resp.Header.ProxyReceivedOn = localEndPoint.ToString()
    resp.Header.ProxyReceivedFrom = remoteEndPoint.ToString()
    resp.Header.Vias.PushViaHeader(SIPViaHeader(SIPEndPoint.ParseSIPEndPoint(dstEndPoint), topVia.Branch))  
    sys.Send(resp, SIPEndPoint.ParseSIPEndPoint(m_proxySocketInternal))
        
  #===== End SIP Response Processing =====
