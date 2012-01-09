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
        private ISIPDialogueManager m_sipDialogueManager;

        public CallManagerServices() { }

        public CallManagerServices(ISIPCallManager sipCallManager, ISIPDialogueManager sipDialogueManager)
        {
            m_sipCallManager = sipCallManager;
            m_sipDialogueManager = sipDialogueManager;
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

                string transferResult = null;

                if (username.IsNullOrBlank())
                {
                    transferResult = "A username must be specified when initiating a blind transfer, the transfer was not initiated.";
                }
                else if (replacesCallID.IsNullOrBlank())
                {
                    transferResult = "Blind transfer requires a Call-ID to replace, the transfer was not initiated.";
                }
                else
                {
                    transferResult = m_sipCallManager.ProcessWebCall(username, destination, WEB_TRANSFER_DIALPLAN_NAME, replacesCallID);
                }

                logger.Debug("BlindTransfer result=" + transferResult + ".");

                return transferResult;
            }
            catch (Exception excp)
            {
                logger.Error("Exception BlindTransfer. " + excp.Message);
                throw;
            }
        }

        /// <summary>
        /// An attended transfer between two separate established calls where one leg of each call is being transferred 
        /// to the other.
        /// </summary>
        /// <param name="callID1">The Call-ID of the first call leg that is no longer required and of which the opposite end will be transferred.</param>
        /// <param name="callID2">The Call-ID of the second call leg that is no longer required and of which the opposite end will be transferred. If 
        /// left empty then the transfer will default to using the last call that was received.</param>
        /// <returns>If successful null otherwise a string with an error message.</returns>
        public string DualTransfer(string username, string callID1, string callID2)
        {
            try
            {
                logger.Debug("CallManagerServices DualTransfer, Username=" + username + ", CallID1=" + callID1 + ", CallID2=" + callID2 + ".");

                m_sipDialogueManager.DualTransfer(username, callID1, callID2);

                return null;
            }
            catch (ApplicationException appExcp)
            {
                logger.Warn("ApplicationException DualTransfer. " + appExcp.Message);
                return appExcp.Message;
            }
            catch (Exception excp)
            {
                logger.Error("Exception DualTransfer. " + excp.Message);
                throw;
            }
        }

        public string Callback(string username, string dialString1, string dialString2)
        {
            try
            {
                logger.Debug("CallManagerServices callback, dialString1=" + dialString1 + ", dialString2=" + dialString2 + ".");

                if (dialString1.IsNullOrBlank())
                {
                    return "The dialString1 parameter was empty, the callback was not initiated.";
                }
                else if (dialString2.IsNullOrBlank())
                {
                    return "The dialString2 parameter was empty, the callback was not initiated.";
                }
                else
                {
                    return m_sipCallManager.ProcessCallback(username, dialString1, dialString2);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception Callback. " + excp.Message);
                throw;
            }
        }
    }
}
