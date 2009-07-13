import clr
clr.AddReference('SIPSorcery.SIP.Core')
from SIPSorcery.SIP import *

m_registrarSocket = "udp:127.0.0.1:5001"
m_regAgentSocket = "udp:127.0.0.1:5002"
m_proxySocketInternal = "udp:127.0.0.1:5060"
m_appServerSocket = "udp:127.0.0.1:5065"

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
      src = sys.GetDefaultSIPEndPoint(destRegistrar.SIPProtocol)
      req.Header.Routes = None
      sys.SendTransparent(destRegistrar, req, branch, src, None)
          
    else:
      req.Header.ProxyReceivedOn = localEndPoint.ToString()
      req.Header.ProxyReceivedFrom = remoteEndPoint.ToString()
      sys.Send(m_registrarSocket, req, proxyBranch, m_proxySocketInternal)
    
  elif sipMethod == "SUBSCRIBE":
    sys.Respond(req, SIPResponseStatusCodesEnum.MethodNotAllowed, None)

  else:
    if remoteEndPoint.ToString() == m_appServerSocket:
      # Request from a SIP Application server for an external UA.
      dest = sys.Resolve(req)
      contactURI = None
      if sipMethod == "INVITE":
        if not dest.SocketEndPoint.Address.ToString().StartsWith("10."):
          # Request is for an external UA, use public IP.
          contactURI = SIPURI(req.URI.Scheme, SIPEndPoint.ParseSIPEndPoint(publicip.ToString()))
        else:
          # Request is for same private network as the proxy, don't use external public IP.
          contactURI = SIPURI(req.URI.Scheme, sys.GetDefaultSIPEndPoint(dest.SIPProtocol))
      src = sys.GetDefaultSIPEndPoint(dest.SIPProtocol)
      branch = req.Header.Vias.PopTopViaHeader().Branch
      sys.SendTransparent(dest, req, branch, src, contactURI)
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
    sys.Send(resp, SIPEndPoint.ParseSIPEndPoint(m_proxySocketInternal))

  elif remoteEndPoint.ToString() == m_appServerSocket:
    # Responses from SIP Application Servers for external UAs.    
    if sipMethod == "INVITE":
      if not outSocket.SocketEndPoint.Address.ToString().StartsWith("10."):
        # INVITE response from an SIP Application Server, need to set the Contact URI to the proxy socket for the protocol.
        contactURI = SIPURI(resp.Header.To.ToURI.Scheme, SIPEndPoint.ParseSIPEndPoint(publicip.ToString()))
      else:
        contactURI = SIPURI(resp.Header.To.ToURI.Scheme, sys.GetDefaultSIPEndPoint(resp.Header.Vias.TopViaHeader.Transport))
      sys.Send(resp, outSocket, contactURI)
    else:
      sys.Send(resp, outSocket)

  else: 
    # Responses from external UAs for SIP Application Servers.
    resp.Header.ProxyReceivedOn = localEndPoint.ToString()
    resp.Header.ProxyReceivedFrom = remoteEndPoint.ToString()    
    resp.Header.Vias.PushViaHeader(SIPViaHeader(SIPEndPoint.ParseSIPEndPoint(m_appServerSocket), topVia.Branch))  
    sys.Send(resp, SIPEndPoint.ParseSIPEndPoint(m_proxySocketInternal))
        
  #===== End SIP Response Processing =====
