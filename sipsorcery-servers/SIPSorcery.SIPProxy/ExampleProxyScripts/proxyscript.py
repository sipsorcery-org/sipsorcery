import clr
clr.AddReference('SIPSorcery.SIP.Core')
from SIPSorcery.SIP import *

m_registrarSocket = "udp:127.0.0.1:5001"
m_regAgentSocket = "udp:127.0.0.1:5002"
m_proxySocketForRegisters = "udp:127.0.0.1:5060"
m_proxySocketInternal = "udp:1192.168.1.12:5060"

if isreq:
  
  #===== SIP Request Processing =====

  sys.Log("req " + summary)
  req.Header.MaxForwards = req.Header.MaxForwards - 1
  
  if remoteEndPoint.SocketEndPoint.ToString().StartsWith("x.x.x.x"):
    sys.Respond(req, SIPResponseStatusCodesEnum.NotAcceptable, "Traffic levels too high - BANNED")

  #elif req.Header.From != None and req.Header.From.FromURI != None and req.Header.From.FromURI.User = "badboy":
  #  sys.Respond(req, SIPResponseStatusCodesEnum.NotAcceptable, "Traffic levels too high - BANNED")
  
  else:
  
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
      if IsFromAppServer:
        dest = sys.Resolve(req)
        if dest == None:
          if sipMethod != "ACK":
            sys.Respond(req, SIPResponseStatusCodesEnum.DoesNotExistAnywhere, "Host " + req.URI.Host + " unresolvable")
        else:
          branch = req.Header.Vias.PopTopViaHeader().Branch
          sys.SendTransparent(dest, req, branch, publicip)

      else:

        dispatcherEndPoint = sys.DispatcherLookup(req)

        if dispatcherEndPoint != None:
          sys.Log("Dispatching request " + req.Method.ToString() + " to " + dispatcherEndPoint.ToString())
          sys.Send(dispatcherEndPoint, req, proxyBranch, m_proxySocketInternal)

        else:

          if sipMethod == "INVITE":
            req.Header.ProxyReceivedOn = localEndPoint.ToString()
            req.Header.ProxyReceivedFrom = remoteEndPoint.ToString()


          appServer = sys.GetAppServer()
          if appServer != None:
            sys.Send(appServer.ToString(), req, proxyBranch, m_proxySocketInternal)
          else:
            sys.Respond(req, SIPResponseStatusCodesEnum.BadGateway, "No sipsorcery app servers available")

  #===== End SIP Request Processing =====

else:

  #===== SIP Response Processing =====

  sys.Log("resp " + summary)
 
  if sipMethod == "REGISTER" and remoteEndPoint.ToString() == m_registrarSocket:
    # REGISTER response from SIP Registrar.
    resp.Header.ProxyReceivedOn = None
    resp.Header.ProxyReceivedFrom = None
    sys.Send(resp, outSocket)

  elif sipMethod == "REGISTER":
    # REGISTER response for SIP Registration Agent.
    resp.Header.Vias.PushViaHeader(SIPViaHeader(SIPEndPoint.ParseSIPEndPoint(m_regAgentSocket), topVia.Branch))  
    sys.Send(resp, SIPEndPoint.ParseSIPEndPoint(m_proxySocketForRegisters))

  elif IsFromAppServer:
    # Responses from SIP Application Servers for external UAs.    
    if sipMethod == "INVITE":
      # INVITE response from a SIP Application Server destined for external network.
      sys.Send(resp, outSocket, publicip)
    else:
      sys.Send(resp, outSocket)

  else: 
    # Responses from external UAs for SIP Application Servers.

    dstEndPoint = sys.GetAppServer()

    dispatcherEndPoint = sys.DispatcherLookup(branch, resp.Header.CSeqMethod)

    if dispatcherEndPoint != None:
      sys.Log("Dispatching response " + resp.Header.CSeqMethod.ToString() + " to " + dispatcherEndPoint.ToString())
      dstEndPoint = dispatcherEndPoint.ToString()

    resp.Header.ProxyReceivedOn = localEndPoint.ToString()
    resp.Header.ProxyReceivedFrom = remoteEndPoint.ToString()
    resp.Header.Vias.PushViaHeader(SIPViaHeader(SIPEndPoint.ParseSIPEndPoint(dstEndPoint), topVia.Branch))  
    sys.Send(resp, SIPEndPoint.ParseSIPEndPoint(m_proxySocketInternal))
        
  #===== End SIP Response Processing =====

