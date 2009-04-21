if isreq
  #sys.Log "request #{summary}"
  sys.Send("213.200.94.182:5060".ToString(), req, proxyBranch)
else
  #sys.Log "response #{summary}"
  sys.Send(resp)
end
 