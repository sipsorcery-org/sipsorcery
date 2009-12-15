using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIPSorcery.Web.Services {
   
    public interface ISIPCallDispatcher {
        CallManagerServiceClient GetCallManagerClient();
    }
}
