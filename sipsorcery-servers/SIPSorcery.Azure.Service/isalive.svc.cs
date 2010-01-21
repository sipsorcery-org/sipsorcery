using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Azure.Service
{
    [ServiceContract]
    public interface IIsAliveService
    {
        [OperationContract]
        [WebGet(UriTemplate = "isalive")]
        bool IsAlive();
    }

    public class IsAliveService : IIsAliveService
    {
        private ILog logger = AppState.logger;

        public bool IsAlive()
        {
            logger.Debug("IsAlive (log4net Debug).");
            Trace.WriteLine("IsAlive called"); 
            return true;
        }
    }
}
