import clr
clr.AddReference('BlueFace.VoIP.Net')
from BlueFace.VoIP.Net.SIP import *

m_appServerSocket = "10.0.0.100:5061"
m_registrarSocket = "127.0.0.1:5001"
m_regAgentSocket = "127.0.0.1:5002"

if isreq:
  
  #===== SIP Request Processing =====

  #sys.Log("req " + summary)
  
  if sipMethod == "REGISTER":
    if remoteEndPoint.ToString() == m_regAgentSocket:
      # A couple of registrars get confused with multiple Via headers in a REGISTER request.
      # To get around that use the branch from the reg agent as the branch in a request that only has one Via.
      branch = req.Header.Via.PopTopViaHeader().Branch

      # The registration agent has indicated where it wants the REGISTER request sent to by adding a Route header.
      # Remove the header in case it confuses the SIP Registrar the REGISTER is being sent to.
      destRegistrar = req.Header.Routes.PopRoute().GetEndPoint().ToString()
      req.Header.Routes = None

      #sys.Log("regagent->" + destRegistrar)
      sys.Send(destRegistrar, req, branch)
    
    else:
      req.Header.MaxForwards = req.Header.MaxForwards - 1
      req.Header.Via.TopViaHeader.ViaParameters.Set("proxy", localEndPoint.ToString())
      sys.Send(m_registrarSocket, req, proxyBranch)
    
  elif sipMethod == "INVITE":
    req.Header.MaxForwards = req.Header.MaxForwards - 1
    
    if req.Header.To.ToTag == None:
      # Don't add a Record-Route header for in dialogue requests.
      req.Header.RecordRoutes.PushRoute("sip:" + localEndPoint.ToString())
      
    if remoteEndPoint.ToString() == m_appServerSocket:
      sys.Send(req.GetRequestEndPoint().ToString(), req, proxyBranch)
    else:
      # Mangle contact address so the proxy can get back to the user agent.
      if req.Header.Contact != None and req.Header.Contact.Count > 0:
        req.Header.Contact[0].ContactURI.Host = remoteEndPoint.ToString()  
      sys.Send(m_appServerSocket, req, proxyBranch)

  else:
    req.Header.MaxForwards = req.Header.MaxForwards - 1
    if remoteEndPoint.ToString() == m_appServerSocket:
      sys.Send(req.GetRequestEndPoint().ToString(), req, proxyBranch)
    else:
      sys.Send(m_appServerSocket, req, proxyBranch)

  #===== End SIP Request Processing =====

else:

  #===== SIP Response Processing =====

  #sys.Log("resp " + summary)
 
  proxyVia = resp.Header.Via.PopTopViaHeader()
  if not sys.IsLocal(proxyVia.GetEndPoint().ToString()):
    sys.Log("Dropping SIP response from " + remoteEndPoint + " because the top Via header was not for this proxy, top header was " + proxyVia.ToString() + ".")

  elif sipMethod == "REGISTER":

    if remoteEndPoint.ToString() == m_registrarSocket:
      # REGISTER response for Registrar
      proxy = resp.Header.Via.TopViaHeader.ViaParameters.Get("proxy")
      resp.Header.Via.TopViaHeader.ViaParameters.Remove("proxy")
      sys.Send(proxy, resp)

    else:
      # REGISTER response for Registration Agent.
      # Add back on the Via header that was removed when the original REGISTER request was passed through from the Agent.
      resp.Header.Via.PushViaHeader(SIPViaHeader(m_regAgentSocket, proxyVia.Branch))
      sys.Send(resp)
    
  elif sipMethod == "INVITE":
    # Mangle contact address if needed.
    if resp.Header.Contact != None and resp.Header.Contact.Count > 0:
      resp.Header.Contact[0].ContactURI.Host = remoteEndPoint.ToString()
    sys.Send(resp) 
   
  else:
    sys.Send(resp)
  
  #===== End SIP Response Processing =====

 