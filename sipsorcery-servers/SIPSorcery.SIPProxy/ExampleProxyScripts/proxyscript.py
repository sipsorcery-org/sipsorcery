import clr
clr.AddReference('SIPSorcery.SIP.Core')
from SIPSorcery.SIP import *

m_appServerSocket = "udp:127.0.0.1:5065"
m_registrarSocket = "udp:127.0.0.1:5001"
m_regAgentSocket = "udp:127.0.0.1:5002"
m_proxySocketInternal = "udp:127.0.0.1:5060"
m_proxySocketPublic = "124.177.23.142"


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
      req.Header.Vias.TopViaHeader.ViaParameters.Set("proxy", localEndPoint.ToString())
      req.Header.ProxyReceivedOn = localEndPoint.ToString()
      req.Header.ProxyReceivedFrom = remoteEndPoint.ToString()
      sys.Send(m_registrarSocket, req, proxyBranch, m_proxySocketInternal)
    
  elif sipMethod == "SUBSCRIBE":
    sys.Respond(req, SIPResponseStatusCodesEnum.MethodNotAllowed, None)

  else:
    if remoteEndPoint.ToString() == m_appServerSocket:
      contactURI = None
      dest = sys.Resolve(req)
      src = sys.GetDefaultSIPEndPoint(dest.SIPProtocol)
      if sipMethod == "INVITE":
        contactURI = SIPURI(req.URI.Scheme, src)
        if not dest.SocketEndPoint.ToString().StartsWith("10."):
          contactURI.Host = m_proxySocketPublic
      branch = req.Header.Vias.PopTopViaHeader().Branch
      sys.SendTransparent(dest, req, branch, src, contactURI)
      #sys.Log(req.ToString())
    else:
      if sipMethod == "INVITE":
        req.Header.Vias.TopViaHeader.ViaParameters.Set("proxy", localEndPoint.ToString())
        req.Header.ProxyReceivedOn = localEndPoint.ToString()
        req.Header.ProxyReceivedFrom = remoteEndPoint.ToString()
        #sys.Mangle(req)  
      sys.Send(m_appServerSocket, req, proxyBranch, m_proxySocketInternal)

  #===== End SIP Request Processing =====

else:

  #===== SIP Response Processing =====

  #sys.Log("resp " + summary)
 

  if sipMethod == "REGISTER" and remoteEndPoint.ToString() == m_registrarSocket:
    # REGISTER response from SIP Registrar.
    resp.Header.Vias.TopViaHeader.ViaParameters.Remove("proxy")
    sys.Send(resp, outSocket)

  elif sipMethod == "INVITE" and remoteEndPoint.ToString() == m_appServerSocket:
    resp.Header.Vias.TopViaHeader.ViaParameters.Remove("proxy")
    dest = sys.Resolve(resp)
    src = sys.GetDefaultSIPEndPoint(resp.Header.Vias.TopViaHeader.Transport)
    contactURI = SIPURI(resp.Header.To.ToURI.Scheme, src)
    if not dest.SocketEndPoint.ToString().StartsWith("10."):
      contactURI.Host = m_proxySocketPublic
    sys.Send(resp, outSocket, contactURI)

  else: 
    if sipMethod == "INVITE" and remoteEndPoint.ToString() != m_appServerSocket:
        resp.Header.ProxyReceivedOn = localEndPoint.ToString()
        resp.Header.ProxyReceivedFrom = remoteEndPoint.ToString()    
        #   sys.Log(resp.ToString())
        #  sys.Mangle(resp)
    
    if sipMethod == "REGISTER":
      outSocket = SIPEndPoint.ParseSIPEndPoint(m_proxySocketInternal)
      resp.Header.Vias.PushViaHeader(SIPViaHeader(SIPEndPoint.ParseSIPEndPoint(m_regAgentSocket), topVia.Branch))  

    elif remoteEndPoint.ToString() != m_appServerSocket:
      outSocket = SIPEndPoint.ParseSIPEndPoint(m_proxySocketInternal)
      resp.Header.Vias.PushViaHeader(SIPViaHeader(SIPEndPoint.ParseSIPEndPoint(m_appServerSocket), topVia.Branch))  

    sys.Send(resp, outSocket)
      
  
  #===== End SIP Response Processing =====
