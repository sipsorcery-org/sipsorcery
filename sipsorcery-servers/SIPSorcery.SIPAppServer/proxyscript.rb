require "E:\\Temp\\sipswitch\\allinone\\BlueFace.VoIP.Net.dll"
include BlueFace::VoIP::Net::SIP

m_appServerSocket = "10.0.0.100:5061".ToString()
#m_appServerSocket = "213.200.94.182:5060".ToString()
m_registrarSocket = "127.0.0.1:5001".ToString()
m_regAgentSocket = "127.0.0.1:5002".ToString()

if isreq
  
  #===== SIP Request Processing =====

  #sys.Log "req #{summary}"
  
  case sipMethod.to_s
    
    when "REGISTER"
      if(remoteEndPoint.ToString() == m_regAgentSocket) then
	# A couple of registrars get confused with multiple Via headers in a REGISTER request.
	# To get around that use the branch from the reg agent as the branch in a request that only has one Via.
	branch = req.Header.Via.PopTopViaHeader().Branch

	# The registration agent has indicated where it wants the REGISTER request sent to by adding a Route header.
        # Remove the header in case it confuses the SIP Registrar the REGISTER is being sent to.
        destRegistrar = req.Header.Routes.PopRoute().GetEndPoint().ToString()
        req.Header.Routes = nil

        #sys.Log("regagent->#{destRegistrar}")
        sys.Send(destRegistrar, req, branch)
      else
        req.Header.MaxForwards = req.Header.MaxForwards - 1
        req.Header.Via.TopViaHeader.ViaParameters.Set("proxy", localEndPoint.ToString())
        sys.Send(m_registrarSocket, req, proxyBranch)
      end
    
    when "INVITE"
      req.Header.MaxForwards = req.Header.MaxForwards - 1
      req.Header.RecordRoutes.PushRoute("sip:#{localEndPoint.ToString()}")
      
      if(remoteEndPoint.ToString() == m_appServerSocket) then
        sys.Send(req.GetRequestEndPoint().ToString(), req, proxyBranch)
      else
        # Mangle contact address so the proxy can get back to the user agent.
        if (req.Header.Contact != nil and req.Header.Contact.Count > 0) then
          req.Header.Contact[0].ContactURI.Host = remoteEndPoint.ToString() 
        end  
        sys.Send(m_appServerSocket, req, proxyBranch)
      end

    else
      req.Header.MaxForwards = req.Header.MaxForwards - 1
      if(remoteEndPoint.ToString() == m_appServerSocket) then
        sys.Send(req.GetRequestEndPoint().ToString(), req, proxyBranch)
      else
        sys.Send(m_appServerSocket, req, proxyBranch)
      end

  end 

  #===== End SIP Request Processing =====

else

  #===== SIP Response Processing =====

  #sys.Log "resp #{summary}"
 
  proxyVia = resp.Header.Via.PopTopViaHeader()
  if(!sys.IsLocal(proxyVia.GetEndPoint().ToString())) then
    sys.Log("Dropping SIP response from #{remoteEndPoint} because the top Via header was not for this proxy, top header was {proxyVia.ToString()}.")
    return
  end

  case sipMethod.to_s
    
    when "REGISTER"

      if(remoteEndPoint.ToString() == m_registrarSocket) then
        # REGISTER response for Registrar
        proxy = resp.Header.Via.TopViaHeader.ViaParameters.Get("proxy")
        resp.Header.Via.TopViaHeader.ViaParameters.Remove("proxy")
        sys.Send(proxy, resp)

      else
        # REGISTER response for Registration Agent.
        # Add back on the Via header that was removed when the original REGISTER request was passed through from the Agent.
        resp.Header.Via.PushViaHeader(SIPViaHeader.new(m_regAgentSocket, proxyVia.Branch))
        sys.Send(resp)

      end
    
    when "INVITE"
      # Mangle contact address if needed.
      if (resp.Header.Contact != nil and resp.Header.Contact.Count > 0) then
        resp.Header.Contact[0].ContactURI.Host = remoteEndPoint.ToString()
      end  
      sys.Send(resp) 
    
    else
      sys.Send(resp)
  
  end

  #===== End SIP Response Processing =====

end
 