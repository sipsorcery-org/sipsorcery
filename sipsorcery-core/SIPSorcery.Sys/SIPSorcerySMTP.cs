//-----------------------------------------------------------------------------
// Filename: Email.cs
//
// Description: Sends an Email.
//
// History:
// 11 Jun 2005	Aaron Clauson	Created.
//
// License: 
// Public Domain
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using log4net;

namespace SIPSorcery.Sys
{
	public class SIPSorcerySMTP
	{
		private static ILog logger = AppState.logger;

        private static string m_smtpServer = AppState.GetConfigSetting("SMTPServer");
        private static string m_smtpServerPort = AppState.GetConfigSetting("SMTPServerPort");
        private static string m_smtpServerUseSSL = AppState.GetConfigSetting("SMTPServerUseSSL");
        private static string m_smtpSendUsername = AppState.GetConfigSetting("SMTPServerUsername");
        private static string m_smtpSendPassword = AppState.GetConfigSetting("SMTPServerPassword");

        static SIPSorcerySMTP()
        {
            logger.Debug("SIPSorcerySMTP setting ServicePointManager.ServerCertificateValidationCallback.");

            ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
            {
                logger.Debug("ServerCertificateValidationCallback for " + certificate.Subject + ".");
                return true;
            };
        }

		public static void SendEmail(string toAddress, string fromAddress, string subject, string messageBody)
		{
            ThreadPool.QueueUserWorkItem(delegate { SendEmailAsync(toAddress, fromAddress, null, null, subject, messageBody); });
		}

        public static void SendEmail(string toAddress, string fromAddress, string ccAddress, string bccAddress, string subject, string messageBody)
        {
            ThreadPool.QueueUserWorkItem(delegate { SendEmailAsync(toAddress, fromAddress, ccAddress, bccAddress, subject, messageBody); });
        }

        private static void SendEmailAsync(string toAddress, string fromAddress, string ccAddress, string bccAddress, string subject, string messageBody)
		{
            if (toAddress.IsNullOrBlank())
            {
                throw new ApplicationException("An email cannot be sent with an empty To address.");
            }
            else
			{
				try
				{
                    // Get around bare line feed issue with IIS and qmail.
                    if (messageBody != null)
                    {
                        messageBody = Regex.Replace(messageBody, @"(?<!\r)\n", "\r\n");
                    }

					// Send an email.
                    MailMessage email = new MailMessage(fromAddress, toAddress, subject, messageBody);
                    email.BodyEncoding = Encoding.UTF8;

                    if (!ccAddress.IsNullOrBlank())
                    {
                        email.CC.Add(new MailAddress(ccAddress));
                    }

                    if (!bccAddress.IsNullOrBlank())
                    {
                        email.Bcc.Add(new MailAddress(bccAddress));
                    }

                    if (!m_smtpServer.IsNullOrBlank())
                    {
                        RelayMail(email);
                    }
                    else
                    {
                        SmtpClient smtpClient = new SmtpClient();
                        smtpClient.DeliveryMethod = SmtpDeliveryMethod.PickupDirectoryFromIis;
                        smtpClient.Send(email);
                        logger.Debug("Email sent to " + toAddress);
                    }
				}
				catch(Exception excp)
				{
                    logger.Error("Exception SendEmailAsync (To=" + toAddress + "). " + excp.Message);
				}
			}
		}
    
        private static void RelayMail(MailMessage email)
        {
            try
            {
                int smtpPort = (m_smtpServerPort.IsNullOrBlank()) ? 25 : Convert.ToInt32(m_smtpServerPort);

                logger.Debug("RelayMail attempting to send " + email.Subject + " via " + m_smtpServer + ":" + smtpPort + " to " + email.To);

                SmtpClient smtpClient = new SmtpClient(m_smtpServer, smtpPort);
                smtpClient.UseDefaultCredentials = false;
                smtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;

                if (!m_smtpServerUseSSL.IsNullOrBlank())
                {
                    smtpClient.EnableSsl = Convert.ToBoolean(m_smtpServerUseSSL);
                }

                if (!m_smtpSendUsername.IsNullOrBlank())
                {                   
                    smtpClient.Credentials = new NetworkCredential(m_smtpSendUsername, m_smtpSendPassword, "");
                }

                smtpClient.Send(email);
                logger.Debug("RelayMail " + email.Subject + " relayed via " + m_smtpServer + " to " + email.To);
            }
            catch (Exception ex)
            {
                 logger.Error("Exception RelayMail. " + ex.Message);
                throw;
            }
        }
    }
}
