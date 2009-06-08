import clr
clr.AddReference('SIPSorcery.SIP.Core')
from SIPSorcery.SIP import *

m_appServerSocket = "udp:10.1.1.2:5065"
m_registrarSocket = "udp:127.0.0.1:5001"
m_regAgentSocket = "udp:127.0.0.1:5002"
m_proxyExternalSocket = "udp:10.1.1.2:5060"

if isreq:
  
  #===== SIP Request Processing =====
 
  #sys.Log("req " + summary)
  req.Header.MaxForwards = req.Header.MaxForwards - 1

  # If the request has come from outside then record the proxy socket it arrived on so that the responses can be sent on the same.
  if remoteEndPoint.ToString() != m_appServerSocket and \
     remoteEndPoint.ToString() != m_registrarSocket and \
     remoteEndPoint.ToString() != m_regAgentSocket:
     req.Header.Vias.TopViaHeader.ViaParameters.Set("proxy", localEndPoint.ToString())
  
  if sipMethod == "REGISTER":
    if remoteEndPoint.ToString() == m_regAgentSocket:
      # A couple of registrars get confused with multiple Via headers in a REGISTER request.
      # To get around that use the branch from the reg agent as the branch in a request that only has one Via.
      branch = req.Header.Vias.PopTopViaHeader().Branch

      # The registration agent has indicated where it wants the REGISTER request sent to by adding a Route header.
      # Remove the header in case it confuses the SIP Registrar the REGISTER is being sent to.
      destRegistrar = sys.Resolve(req.Header.Routes.PopRoute().URI)
      req.Header.Routes = None
      req.LocalSIPEndPoint = SIPEndPoint.ParseSIPEndPoint(m_proxyExternalSocket)
      #sys.Log("regagent->" + destRegistrar)
      sys.Send(destRegistrar, req, branch, channelName, False)
    
    else:
      req.LocalSIPEndPoint = None					# Forward to the Registrar over default UDP channel.
      sys.Send(m_registrarSocket, req, proxyBranch, channelName, False)
    
  elif sipMethod == "INVITE":
    if remoteEndPoint.ToString() == m_appServerSocket:			# INVITEs from App Server - incoming calls.
      dstEndPoint = sys.Resolve(req)
      if dstEndPoint != None : 
        sys.Send(dstEndPoint, req, proxyBranch, channelName, (req.Header.To.ToTag == None))
      else:
        sys.Log("Failed to resolve destination for INVITE to " + req.URI.ToString() + ", request dropped.");

    else:								# INVITEs to App Server from UAs - outgoing calls.
      if req.Header.Contact != None and req.Header.Contact.Count > 0: 	# Mangle contact address so the proxy can get back to the user agent.
         req.Header.Contact[0].ContactURI.Host = remoteEndPoint.SocketEndPoint.ToString()  
      req.LocalSIPEndPoint = None					# Forward to the App Server over default UDP channel.
      sys.Send(m_appServerSocket, req, proxyBranch, channelName, (req.Header.To.ToTag == None))  # Don't add a Record-Route header for in dialogue requests.

  else:									# Not a REGISTER or INVITE request.
    if sipMethod == "BYE":
      sys.Log("BYE Routes=" + req.ReceivedRoute.ToString() + ".")

    req.Header.MaxForwards = req.Header.MaxForwards - 1
    req.LocalSIPEndPoint = None						# Force the proxy to forward over the default channel for the destination's protocol.
    if remoteEndPoint.ToString() == m_appServerSocket:
      dstEndPoint = sys.Resolve(req)
      if dstEndPoint != None : 
        sys.Send(dstEndPoint, req, proxyBranch, channelName, False)
      else:
        sys.Log("Failed to resolve destination for " + req.Method.ToString()  + " to " + req.URI.ToString() + ", request dropped.");
    else:
      sys.Send(m_appServerSocket, req, proxyBranch, channelName, False)

  #===== End SIP Request Processing =====

else:

  #===== SIP Response Processing =====

  #sys.Log("resp " + summary)
  #sys.Log("remoteendpoint= " + remoteEndPoint.ToString())
 
  proxyVia = resp.Header.Vias.PopTopViaHeader()

  # This block is used to make sure that a response from one of the server agents gets sent from the correct proxy socket.
  cn = proxyVia.ViaParameters.Get("cn")
  #sys.Log("Via cn= " + cn)
  if cn != None:
    cnSIPEndPoint = sys.GetChannelSIPEndPoint(cn)  # Forward back to UA from the same proxy socket the original request came on.
    #sys.Log("cn SIPEndPoint= " + cnSIPEndPoint.ToString())
    if cnSIPEndPoint != None:
      resp.LocalSIPEndPoint = cnSIPEndPoint
    else:
      resp.LocalSIPEndPoint = None
  else:
    resp.LocalSIPEndPoint = None				# Will force the response to be sent from the default channel for the Via protocol.

  if sipMethod == "REGISTER":

    if remoteEndPoint.ToString() != m_registrarSocket:
      # REGISTER response for Registration Agent.
      # Add back on the Via header that was removed when the original REGISTER request was passed through from the Agent.
      resp.Header.Vias.PushViaHeader(SIPViaHeader(SIPEndPoint.ParseSIPEndPoint(m_regAgentSocket), proxyVia.Branch))
      sys.Send(resp)
    else:
      sys.Send(resp)
    
  elif sipMethod == "INVITE":
    # Mangle contact address if needed.
    if resp.Header.Contact != None and resp.Header.Contact.Count > 0:
      resp.Header.Contact[0].ContactURI.Host = remoteEndPoint.SocketEndPoint.ToString()
    sys.Send(resp, localEndPoint, cnSIPEndPoint, channelName) 
   
  else:
    sys.Send(resp)
  
  #===== End SIP Response Processing =====

 