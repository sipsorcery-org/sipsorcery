// ============================================================================
// FileName: SIPNotificationAgent.cs
//
// Description:
// Provides SIP notifications to subscribed SIP clients (For MWI See RFC3842).
//
// See note in the StatelessProxyCore or RegistrarCore classes about the reason the UA socket address
// is stored in the fromdomain field in the database.
//
// Author(s):
// Aaron Clauson
//
// History:
// 06 Aug 2006	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
// ============================================================================

using System;
using System.Data;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{	
	public class SIPNotificationAgent
	{	
		public const string SIP_NOTIFICATIONUSERAGENT_STRING = "obelisk-notifier-v1.0.0";
		public const string SIP_NOTIFICATIONAGENT_DOMAIN = "sip.blueface.ie";
		public const int NEW_NOTIFICATION_CHECK_PERIOD = 10;	// Period in seconds to check for new notifications.
		public const string EVENT_TYPE = "message-summary";
		public const string SUBSCRIPTION_STATE = "active";
		public const int NUMBER_NOTIFICATIONS_STEP = 100;
		public const int PERIOD_BETWEENNOTIFICATIONS_STEP = 1;	// Period in seconds between each of the notification steps.
		public const string MWI_WAITING_VALUE = "yes";

		//private string m_mwiContentType = SIPConstants.MWI_CONTENT_TYPE;
		//private int m_defaultMaxForwards = SIPConstants.DEFAULT_MAX_FORWARDS;
		//private string m_notificationUserAgent = SIP_NOTIFICATIONUSERAGENT_STRING;

		private StorageTypes m_storageType;
		private string m_dbConnStr;
		private StorageLayer m_storageLayer;

        private SIPTransport m_sipTransport;
		private IPEndPoint m_proxyEndPoint;

		public Thread[] NotificationAgentThreads;
		public bool SendNotifications = true;

		private static ILog logger = AppState.GetLogger("SIPServers");

		public SIPNotificationAgent(SIPTransport sipTransport, IPEndPoint proxyEndPoint, StorageTypes storageType, string dbConnStr)
		{
            m_sipTransport = sipTransport;
			m_proxyEndPoint = proxyEndPoint;

			m_storageType = storageType;
			m_dbConnStr = dbConnStr;
			m_storageLayer = new StorageLayer(m_storageType, m_dbConnStr);
		}

		public void StartNotificationAgent(int numberThreads)
		{
			try
			{
				logger.Debug("SIPNotificationAgent thread(s) started.");

				NotificationAgentThreads = new Thread[numberThreads];
				
				for(int index = 0; index<numberThreads; index++)
				{
					NotificationAgentThreads[index] = new Thread(new ThreadStart(CheckMessagingAccounts));
					NotificationAgentThreads[index].Name = "noti-thread-" + index.ToString();
					NotificationAgentThreads[index].Start();
				}
			}
			catch(Exception excp)
			{
				string eventMessage = "Exception in SIPNotificationAgent StartNotificationAgent. " + excp.Message;
				SIPMonitorEvent monitorEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.NotifierAgent, SIPMonitorEventTypesEnum.Error, eventMessage, null);
				//ProxyMonitor.AddProxyEvent(monitorEvent);
			}
		}

		/// <summary>
		/// Checks the messaging accounts store, at the moment this is the database storing MWI, and notifies any
		/// subscribed user agents of status changes using a SIP Notify request.
		/// </summary>
		public void CheckMessagingAccounts()
		{
			try
			{
				// Record the time the notifications sending started.
				DateTime lastCheckTime = DateTime.Now;
				
				// Get initial set of voicemail accounts requiring notifications 100 at a time.
				int offset = 0;
				int count = NUMBER_NOTIFICATIONS_STEP;

				string initialSelectSQLTemplate = 
					"select distinct(fullcontact), name, sa.mailbox, mwi from " +
					"sipaccounts sa join voicemailaccounts vm " +
                    "on substring(sa.mailbox from '^([^@]+)') = vm.mailbox " +
					"where " +
                    //"vm.mailbox in ('3001', '3029', '3031') and " + 
					//"mwi = 'yes' and " +
					"not fullcontact = '' and " +
					"not fullcontact is null " +
					"offset {1} limit {2}";

				string subsequentSelectSQLTemplate = 
					"select distinct(fullcontact), name, sa.mailbox, mwi from " +
					"sipaccounts sa join voicemailaccounts vm " +
                    "on substring(sa.mailbox from '^([^@]+)') = vm.mailbox " +
					"where " +
                    //"vm.mailbox in ('3001', '3029', '3031') and " +
					"mwilastupdated > '{0:dd MMM yyyy HH:mm:ss}' and " +
					"not fullcontact = '' and " +
					"not fullcontact is null " +
					"offset {1} limit {2}";

				string sqlTemplate = initialSelectSQLTemplate;

				while(SendNotifications)
				{
					DateTime startCheckTime = DateTime.MinValue;
					int mwiCount = 0;
										
					try
					{
						string selectSQL = String.Format(sqlTemplate, lastCheckTime, offset, count);

						DataSet notificationsSet = m_storageLayer.GetDataSet(selectSQL);
						
						while(notificationsSet != null && notificationsSet.Tables[0].Rows.Count > 0)
						{
							if(startCheckTime == DateTime.MinValue)
							{
								startCheckTime = DateTime.Now;
							}
							
							foreach(DataRow notificationRow in notificationsSet.Tables[0].Rows)
							{
								string mailbox = notificationRow["mailbox"] as string;
								string name = notificationRow["name"] as string;
								string fullContact = notificationRow["fullcontact"] as string;
								string mwi = notificationRow["mwi"] as string;

                                try
                                {
                                    bool mwiInidcation = (mwi == MWI_WAITING_VALUE);

                                    SIPURI contactURI = SIPURI.ParseSIPURI(fullContact);

                                    logger.Debug("sending mwi for " + name + ", mailbox " + mailbox + " to " + contactURI.Host + " mwi=" + mwiInidcation + ".");

                                    SIPEndPoint clientEndPoint = new SIPEndPoint(IPSocket.GetIPEndPoint(contactURI.Host));
                                    SIPEndPoint localSIPEndPoint = m_sipTransport.GetDefaultSIPEndPoint();

                                    SIPRequest notifyRequest = GetNotifyRequest(localSIPEndPoint, m_proxyEndPoint, contactURI, name, mwiInidcation);
                                    SIPNonInviteTransaction notifyTransaction = m_sipTransport.CreateNonInviteTransaction(notifyRequest, clientEndPoint, localSIPEndPoint, null);
                                    notifyTransaction.SendReliableRequest();
                                }
                                catch (Exception mwiExcp)
                                {
                                    logger.Error("Exception Sending MWI. " + mwiExcp.Message);
                                }

								mwiCount++;

                                if (mwiCount % 10 == 0)
                                {
                                    Thread.Sleep(50);   // Don't want to blast the proxy too hard on start up.
                                }
							}

							offset += count;
                            selectSQL = String.Format(sqlTemplate, DateTime.Now, offset, count);

                            //logger.Debug(selectSQL);

                            notificationsSet = m_storageLayer.GetDataSet(selectSQL);

                            Thread.Sleep(50);
						}

                        logger.Debug(DateTime.Now.ToString("HH:mm:ss") + " mwi check complete, " + mwiCount + " notifications sent.");
					}
					catch(Exception notifyExcp)
					{
						logger.Error("Exception SIPNotificationAgent Notifying. " + notifyExcp.Message);
					}
					
					offset = 0;

					// Only update the time to check for MWI updates from if an update has been found since the last check. This avoids race conditions between
					// the script updating the flag and this agent polling it.
					if(startCheckTime != DateTime.MinValue)
					{
						lastCheckTime = startCheckTime;
					}

					sqlTemplate = subsequentSelectSQLTemplate;

					Thread.Sleep(NEW_NOTIFICATION_CHECK_PERIOD * 1000);
				}
			}
			catch(Exception excp)
			{
				string eventMessage = "Exception in SIPNotificationAgent CheckMessagingAccounts. " + excp.Message;
                SIPMonitorEvent monitorEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.NotifierAgent, SIPMonitorEventTypesEnum.Error, eventMessage, null);
				//ProxyMonitor.AddProxyEvent(monitorEvent);
			}
		}

		public static SIPRequest GetNotifyRequest(
			SIPEndPoint localSIPEndPoint,
            IPEndPoint proxyEndPoint,
			SIPURI uaURI,
            string username,
			bool messageWaiting)
		{	
			try
			{
				//SIPURI uaURI = new SIPURI(username, IPSocket.GetSocketString(uaEndPoint), null);

				SIPFromHeader fromHeader = new SIPFromHeader(null, new SIPURI(username, SIP_NOTIFICATIONAGENT_DOMAIN, null), CallProperties.CreateNewTag());
                SIPToHeader toHeader = new SIPToHeader(null, uaURI, null);
				//SIPURI contactURI = new SIPURI(username, IPSocket.GetSocketString(proxyEndPoint), null);
				//SIPContactHeader contactHeader = new SIPContactHeader(null, contactURI);

				string callId = CallProperties.CreateNewCallId();
				int cseq = DateTime.Now.Millisecond;	
				string branchId = SIPConstants.SIP_BRANCH_MAGICCOOKIE + Crypto.GetRandomInt().ToString();

				SIPRequest notifyRequest = new SIPRequest(SIPMethodsEnum.NOTIFY, uaURI.ToString());

				//SIPHeader header = new SIPHeader(contactHeader, fromHeader, toHeader, cseq, callId);
                SIPHeader header = new SIPHeader(fromHeader, toHeader, cseq, callId);
				header.CSeqMethod = SIPMethodsEnum.NOTIFY;
                header.UserAgent = SIP_NOTIFICATIONUSERAGENT_STRING;
				header.Event = EVENT_TYPE;
				header.SubscriptionState = SUBSCRIPTION_STATE;

                SIPViaHeader viaHeader = new SIPViaHeader(localSIPEndPoint, branchId);
				header.Vias.PushViaHeader(viaHeader);

                if (proxyEndPoint != null)
                {
                    // Add route header to get the proxy to send to the client User Agent.
                    header.Routes.PushRoute(new SIPRoute("sip:" + IPSocket.GetSocketString(proxyEndPoint) + ";lr"));
                }

				string notifyBody = "Messages-Waiting: ";
				notifyBody += (messageWaiting) ? "yes" : "no";

                header.ContentType = SIPConstants.MWI_CONTENT_TYPE;
				header.ContentLength = notifyBody.Length;

				notifyRequest.Header = header;
				notifyRequest.Body = notifyBody;

				return notifyRequest;
			}
			catch(Exception excp)
			{
				logger.Error("Exception StartNotificationAgent GetNotifyRequest. " + excp.Message);
				throw excp;
			}
		}
	}
}
