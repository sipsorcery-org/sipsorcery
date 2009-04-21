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
using System.Threading;
using System.Text.RegularExpressions;
using System.Web;
using log4net;

namespace SIPSorcery.Sys
{
	public class Email
	{
		private static ILog logger = AppState.logger;

		private string m_toAddress = null;
		private string m_fromAddress = null;
		private string m_ccAddress = null;
		private string m_bccAddress = null;
		private string m_subject = null;
		private string m_body = null;
        
        #region SMPT server variables - used for relaying mail via remote server.

        private string m_smtpServer = AppState.GetConfigSetting("SMTPServer");
        private string m_smtpServerPort = AppState.GetConfigSetting("SMTPServerPort");
        private string m_smtpSendUsing = AppState.GetConfigSetting("SMTPSendUsing");
        private string m_smtpAuthenticate = AppState.GetConfigSetting("SMTPAuthenticate");
        private string m_smtpSendUsername = AppState.GetConfigSetting("SMTPSendUserName");
        private string m_smtpSendPassword = AppState.GetConfigSetting("SMTPSendPassword");

        #endregion

		public static void SendEmail(string toAddress, string fromAddress, string subject, string messageBody)
		{
			try
			{
				logger.Debug("SendEmail: To = " + toAddress + ", From = " + fromAddress + ", Subject = " + subject + ".");
			
				Email email = new Email();
				email.m_toAddress = toAddress;
				email.m_fromAddress = fromAddress;
				email.m_subject = subject;

                // Get around bare line feed issue with IIS and qmail.
                if (messageBody != null)
                {
                    messageBody = Regex.Replace(messageBody, @"(?<!\r)\n", "\r\n");
                }

				email.m_body = messageBody;

				Thread emailThread = new Thread(new ThreadStart(email.SendEmailAsync));
				emailThread.Start();

				//ThreadPool.QueueUserWorkItem(new WaitCallback(email.SendEmailAsync), new string[]{toAddress, fromAddress, subject, messageBody});
			}
			catch(Exception excp)
			{
				logger.Error("Exception SendEmail. " + excp.Message);
			}
		}

		/// <summary>
		/// This mehtod should be used when sending a large number of emails from a daemon to avoid having to create a new thread to send each email.
		/// </summary>
		public static void SendEmailBulk(string toAddress, string fromAddress, string ccAddress, string bccAddress, string subject, string messageBody)
		{
			logger.Debug("SendEmailBulk: To = " + toAddress + ", From = " + fromAddress + ", Subject = " + subject + ".");

			if(toAddress == null || toAddress.Trim().Length > 0)
			{
				try
				{
					// Send an email.
					System.Web.Mail.MailMessage Message = new System.Web.Mail.MailMessage();
					Message.BodyEncoding = System.Text.Encoding.UTF8;
					
					Message.To = toAddress;
					
					if(ccAddress != null && ccAddress.Trim().Length > 0)
					{
						Message.Cc = ccAddress;
					}

					if(bccAddress != null && bccAddress.Trim().Length > 0)
					{
						Message.Bcc = bccAddress;
					}
					
					Message.From = fromAddress;					
					Message.Subject = subject;

                    // Get around bare line feed issue with IIS and qmail.
                    if (messageBody != null)
                    {
                        messageBody = Regex.Replace(messageBody, @"(?<!\r)\n", "\r\n");
                    }

                    Message.Body = messageBody;
							
					System.Web.Mail.SmtpMail.Send(Message);
					logger.Debug("Email sent to " + toAddress);
				}
				catch(System.Web.HttpException ehttp)
				{
					logger.Warn("Exception attempting to send email to " + toAddress + ". " + ehttp.Message);
				}
			}
		}

		//private void SendEmailAsync(object param)
		private void SendEmailAsync()
		{
			//logger.Debug("SendEmailAsync");
			
			//string[] emailParams = (string[])param;

			string toAddress = m_toAddress; //emailParams[0];
			string fromAddress = m_fromAddress; //emailParams[1];
			string subject = m_subject; //emailParams[2];
			string messageBody = m_body; //emailParams[3];

			if(toAddress == null || toAddress.Trim().Length > 0)
			{
				try
				{
					// Send an email.
					System.Web.Mail.MailMessage Message = new System.Web.Mail.MailMessage();
					Message.BodyEncoding = System.Text.Encoding.UTF8;
					
					Message.To = toAddress;
					Message.From = fromAddress;					
					Message.Subject = subject;

                    // Get around bare line feed issue with IIS and qmail.
                    if (messageBody != null)
                    {
                        messageBody = Regex.Replace(messageBody, @"(?<!\r)\n", "\r\n");
                    }

					Message.Body = messageBody;

                    if (m_smtpServer != null && m_smtpServer.Length > 0)
                    {
                        RelayMail(Message);
                    }
                    else
                    {
                        System.Web.Mail.SmtpMail.Send(Message);
                        logger.Debug("Email sent to " + toAddress);
                    }
				}
				catch(System.Web.HttpException ehttp)
				{
					logger.Warn("Exception attempting to send email to " + toAddress + ". " + ehttp.Message);
				}
			}
		}

		public static void SendEmail(string toAddress, string fromAddress, string ccAddress, string bccAddress, string subject, string messageBody)
		{
			try
			{
				logger.Debug("SendEmail: To = " + toAddress + ", From = " + fromAddress + ", Subject = " + subject + ".");
			
				Email email = new Email();
				email.m_toAddress = toAddress;
				email.m_fromAddress = fromAddress;
				email.m_ccAddress = ccAddress;
				email.m_bccAddress = bccAddress;
				email.m_subject = subject;

                // Get around bare line feed issue with IIS and qmail.
                if (messageBody != null)
                {
                    messageBody = Regex.Replace(messageBody, @"(?<!\r)\n", "\r\n");
                }

				email.m_body = messageBody;

				Thread emailThread = new Thread(new ThreadStart(email.SendEmailExtraAddressesAsync));
				emailThread.Start();

				//ThreadPool.QueueUserWorkItem(new WaitCallback(email.SendEmailExtraAddressesAsync), new string[]{toAddress, fromAddress, ccAddress, bccAddress, subject, messageBody});
			}
			catch(Exception excp)
			{
				logger.Error("Exception SendEmail. " + excp.Message);
			}
		}

		//private void SendEmailExtraAddressesAsync(object param)
		private void SendEmailExtraAddressesAsync()
		{
			//logger.Debug("SendEmailExtraAddressesAsync");
			
			//string[] emailParams = (string[])param;

			string toAddress = m_toAddress; //emailParams[0];
			string fromAddress = m_fromAddress; //emailParams[1];
			string ccAddress = m_ccAddress; //emailParams[2];
			string bccAddress = m_bccAddress; //emailParams[3];
			string subject = m_subject; //emailParams[4];
			string messageBody = m_body; //emailParams[5];

			if(toAddress == null || toAddress.Trim().Length > 0)
			{
                try
                {
                    // Send an email.
                    System.Web.Mail.MailMessage Message = new System.Web.Mail.MailMessage();
                    Message.BodyEncoding = System.Text.Encoding.UTF8;

                    Message.To = toAddress;

                    if (ccAddress != null && ccAddress.Trim().Length > 0)
                    {
                        Message.Cc = ccAddress;
                    }

                    if (bccAddress != null && bccAddress.Trim().Length > 0)
                    {
                        Message.Bcc = bccAddress;
                    }

                    Message.From = fromAddress;
                    Message.Subject = subject;

                    // Get around bare line feed issue with IIS and qmail.
                    if (messageBody != null)
                    {
                        messageBody = Regex.Replace(messageBody, @"(?<!\r)\n", "\r\n");
                    }

                    Message.Body = messageBody;

                    if (m_smtpServer != null && m_smtpServer.Length > 0)
                    {
                        RelayMail(Message);
                    }
                    else
                    {
                        System.Web.Mail.SmtpMail.Send(Message);
                        logger.Debug("Email sent to " + toAddress);
                    }
                }
                catch (System.Web.HttpException ehttp)
                {
                    logger.Warn("Exception attempting to send email to " + toAddress + ". " + ehttp.Message);
                }
			}
		}
    
        private void RelayMail(System.Web.Mail.MailMessage email)
        {
            try
            {
                email.Fields["http://schemas.microsoft.com/cdo/configuration/smtsperver"] = m_smtpServer;
                email.Fields["http://schemas.microsoft.com/cdo/configuration/smtpserverport"] = m_smtpServerPort;
                email.Fields["http://schemas.microsoft.com/cdo/configuration/sendusing"] = m_smtpSendUsing;
                email.Fields["http://schemas.microsoft.com/cdo/configuration/smtpauthenticate"] = m_smtpAuthenticate;
                email.Fields["http://schemas.microsoft.com/cdo/configuration/sendusername"] = m_smtpSendUsername;
                email.Fields["http://schemas.microsoft.com/cdo/configuration/sendpassword"] = m_smtpSendPassword;
                System.Web.Mail.SmtpMail.SmtpServer = m_smtpServer;
                System.Web.Mail.SmtpMail.Send(email);
                logger.Debug("Mail " + email.Subject + " relayed via " + m_smtpServer + " to " + email.To);
            }
            catch (Exception ex)
            {
                logger.Error("Email.RelayMail exception trying to relay mail " + email.Subject
                    + " via " + m_smtpServer + " to " + email.To, ex);
                throw ex;
            }
        }
    }
}
