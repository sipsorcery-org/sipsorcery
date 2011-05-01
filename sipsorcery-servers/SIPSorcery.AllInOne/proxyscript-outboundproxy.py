import clr
clr.AddReference('SIPSorcery.SIP.Core')
from SIPSorcery.SIP import *

m_appServerSocket = "udp:10.0.0.100:5061"
m_registrarSocket = "udp:127.0.0.1:5001"
m_regAgentSocket = "udp:127.0.0.1:5002"
m_proxySocket = "udp:127.0.0.1:5060"

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
      sys.Send(destRegistrar, req, branch, channelName)
          
    else:
      req.Header.Vias.TopViaHeader.ViaParameters.Set("proxy", localEndPoint.ToString())
      sys.Send(m_registrarSocket, req, proxyBranch, channelName)
    
  elif sipMethod == "INVITE":
    
    if req.Header.To.ToTag == None:
      # Don't add a Record-Route header for in dialogue requests.
      req.Header.RecordRoutes.PushRoute("sip:" + localEndPoint.ToString())
      
    if remoteEndPoint.ToString() == m_appServerSocket:
      sys.Send(req.GetRequestEndPoint(), req, proxyBranch, channelName)
    else:
      # Mangle contact address so the proxy can get back to the user agent.
      if req.Header.Contact != None and req.Header.Contact.Count > 0:
        req.Header.Contact[0].ContactURI.Host = remoteEndPoint.ToString()  
      sys.Send(m_appServerSocket, req, proxyBranch, channelName)

  else:
    if remoteEndPoint.ToString() == m_appServerSocket:
      sys.Send(req.GetRequestEndPoint(), req, proxyBranch, channelName)
    else:
      sys.Send(m_appServerSocket, req, proxyBranch, channelName)

  #===== End SIP Request Processing =====

else:

  #===== SIP Response Processing =====

  #sys.Log("resp " + summary)
 
  if sipMethod == "REGISTER" and remoteEndPoint.ToString() != m_registrarSocket:
    # REGISTER response for Registration Agent.
    # Add back on the Via header that was removed when the original REGISTER request was passed through from the Agent.
    resp.Header.Vias.PushViaHeader(SIPViaHeader(SIPEndPoint.ParseSIPEndPoint(m_regAgentSocket), topVia.Branch))
    sys.Send(resp)
    
  elif sipMethod == "INVITE":
    # Mangle contact address if needed.
    if resp.Header.Contact != None and resp.Header.Contact.Count > 0:
      resp.Header.Contact[0].ContactURI.Host = remoteEndPoint.ToString()
    sys.Send(resp) 
   
  else:
    sys.Send(resp)
  
  #===== End SIP Response Processing =====

 