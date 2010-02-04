using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Web.Services
{
    public class CallManagerServices : ICallManagerServices
    {
        public const string WEB_CALLBACK_DIALPLAN_NAME = "webcallback";
        public const string WEB_TRANSFER_DIALPLAN_NAME = "transfer";

        private static ILog logger = AppState.logger;

        private ISIPCallManager m_sipCallManager;

        public CallManagerServices() { }

        public CallManagerServices(ISIPCallManager sipCallManager)
        {
            m_sipCallManager = sipCallManager;
        }

        public bool IsAlive()
        {
            return true;
        }

        public string WebCallback(string username, string number)
        {
            try
            {
                logger.Debug("CallManagerServices webcallback, username=" + username + ", number=" + number + ".");

                if (username.IsNullOrBlank())
                {
                    return "A username must be specified when initiating a web callback, the callback was not initiated.";
                }
                else
                {
                    return m_sipCallManager.ProcessWebCall(username, number, WEB_CALLBACK_DIALPLAN_NAME, null);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception WebCallback. " + excp.Message);
                throw;
            }
        }

        public string BlindTransfer(string username, string destination, string replacesCallID)
        {
            try
            {
                logger.Debug("CallManagerServices BlindTransfer, user=" + username + ", destination=" + destination + ", callID=" + replacesCallID + ".");

                if (username.IsNullOrBlank())
                {
                    return "A username must be specified when initiating a blind transfer, the transfer was not initiated.";
                }
                else if (replacesCallID.IsNullOrBlank())
                {
                    return "Blind transfer requires a Call-ID to replace, the transfer was not initiated.";
                }
                else
                {
                    return m_sipCallManager.ProcessWebCall(username, destination, WEB_TRANSFER_DIALPLAN_NAME, replacesCallID);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception BlindTransfer. " + excp.Message);
                throw;
            }
        }
    }
}
