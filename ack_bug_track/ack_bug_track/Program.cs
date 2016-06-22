using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ack_bug_track
{
	class Program
	{

		static void Main(string[] args)
		{
			Program prg = new Program();
			prg.CallLegs = new List<Model.CallLeg>();
			prg.Cancel = new CancellationTokenSource();
			try
			{
				Task tRunSipHandler = new Task(() =>
				{
					if(!prg.StartSipHandler())
					{

					}
					else
					{
					}
				}, prg.Cancel.Token);
				tRunSipHandler.Start();

			}
			catch(Exception ex)
			{

			}

			Console.ReadKey();

			prg.StopSipHandler();
		}

		internal static SIPMonitorLogDelegate SIPLogDelegate = (evt) =>
		{
			if(evt is SIPMonitorConsoleEvent)
			{
				SIPMonitorConsoleEvent cevt = evt as SIPMonitorConsoleEvent;
				switch(cevt.EventType)
				{
					case SIPMonitorEventTypesEnum.SIPTransaction:
						//we use transaction trace events to log this
						break;
					default:
						Console.WriteLine(cevt.ToConsoleString("*"));
						break;
				}
			}
			else
			{
				Console.WriteLine(evt.Message);
			}
		};



		internal SIPTransport SipTransport
		{
			get;
			private set;
		}
		internal List<Model.CallLeg> CallLegs
		{
			get;
			private set;
		}
		internal CancellationTokenSource Cancel
		{
			get;
			private set;
		}

		internal bool StartSipHandler()
		{
			try
			{
				IPAddress ipAddr = SIPSorcery.Sys.LocalIPConfig.GetDefaultIPv4Address();

				SIPTCPChannel tcpChannel = null;
				tcpChannel = new SIPTCPChannel(new IPEndPoint(ipAddr, 5060));

				SIPUDPChannel udpChannel = new SIPUDPChannel(new IPEndPoint(ipAddr, 5060));

				this.SipTransport = new SIPTransport(SIPDNSManager.ResolveSIPService, new SIPTransactionEngine());
				if(this.SipTransport != null)
				{
					if(udpChannel != null)
						this.SipTransport.AddSIPChannel(udpChannel);
					if(tcpChannel != null)
						this.SipTransport.AddSIPChannel(tcpChannel);

					this.SipTransport.SIPTransportRequestReceived += SIPTransport_RequestReceived;
					this.SipTransport.SIPTransportResponseReceived += SIPTransport_ResponseReceived;
				}
				else
				{
					return false;
				}
			}
			catch(Exception ex)
			{
				return false;
			}
			return true;
		}

		internal void StopSipHandler()
		{
			if(this.SipTransport != null)
			{
				this.SipTransport.Shutdown();
				this.SipTransport.SIPTransportRequestReceived -= SIPTransport_RequestReceived;
				this.SipTransport.SIPTransportResponseReceived -= SIPTransport_ResponseReceived;
			}
		}

		private void SIPTransport_RequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
		{
			Model.CallLeg leg = (from l in this.CallLegs where l.CallId == sipRequest.Header.CallId select l).FirstOrDefault();
			if(sipRequest.Method == SIPMethodsEnum.INVITE && !string.IsNullOrWhiteSpace(sipRequest.Body))
			{
				Model.CallLeg legA = leg;
				if(leg == null)
				{
					legA = new Model.CallLeg();
					legA.LegType = Model.CallLegType.LegA;
					legA.LocalContact = new SIPContactHeader("LegA", SIPURI.ParseSIPURI($"sip:lega@{localSIPEndPoint.GetIPEndPoint().ToString()};transport={localSIPEndPoint.Protocol.ToString()}"));
					if(sipRequest.Header.Contact.Count>0)
						legA.RemoteContact = sipRequest.Header.Contact[0];
					this.CallLegs.Add(legA);
				}

				else if(leg.CallState != Model.CallState.Idle)
				{
					//handle re-invite: hold, unhold, re-invite
					SIPResponse tryingResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Trying, null);
					if(leg.SipServer != null)
					{
						UASInviteTransaction tryingTransaction = this.SipTransport.CreateUASTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, null);
						tryingTransaction.SendInformationalResponse(tryingResponse);
						//leg.SipServer.Progress(SIPResponseStatusCodesEnum.Trying, null, null, null, null);
					}
					else if(leg.SipClient != null)
					{
						leg.SipClient.ServerTransaction.SendInformationalResponse(tryingResponse);
					}
					if(leg.PartnerLeg == null)
					{
						//there is no legB, that's BAD -> reject
						SIPResponse notAllowedResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Decline, "no PartnerLeg present for forwading RE-INVITE");
						this.SipTransport.SendResponse(notAllowedResponse);
						return;
					}
					SIPDialogue dialogue = null;
					if(leg.PartnerLeg.SipClient != null)
						dialogue = leg.PartnerLeg.SipClient.SIPDialogue;
					else if(leg.PartnerLeg.SipServer != null)
						dialogue = leg.PartnerLeg.SipServer.SIPDialogue;
					if(dialogue != null)
					{
						dialogue.CSeq += 1;
						SIPRequest reInviteRequest = new SIPRequest(SIPMethodsEnum.INVITE, dialogue.RemoteTarget);
						SIPHeader reInviteHeader = new SIPHeader(SIPFromHeader.ParseFromHeader(dialogue.LocalUserField.ToString()), SIPToHeader.ParseToHeader(dialogue.RemoteUserField.ToString()), dialogue.CSeq, dialogue.CallId);
						reInviteHeader.Contact = new List<SIPContactHeader>();
						reInviteHeader.Contact.Add(leg.PartnerLeg.LocalContact);
						reInviteHeader.CSeqMethod = SIPMethodsEnum.INVITE;
						reInviteRequest.Header = reInviteHeader;
						reInviteRequest.Header.Routes = dialogue.RouteSet;
						if(dialogue.Direction == SIPCallDirection.Out)
						{
							if(reInviteRequest.Header.Vias.Length>0)
								reInviteRequest.Header.Vias.PopTopViaHeader();
							SIPViaHeader viaHeader = new SIPViaHeader(new SIPEndPoint(dialogue.RemoteTarget), CallProperties.CreateBranchId());
							reInviteRequest.Header.Vias.PushViaHeader(viaHeader);
						}
						else if(dialogue.Direction == SIPCallDirection.In)
						{
							if(reInviteRequest.Header.Vias.Length > 0)
								reInviteRequest.Header.Vias.PopTopViaHeader();
							SIPViaHeader viaHeader = new SIPViaHeader(new SIPEndPoint(dialogue.LocalUserField.URI), CallProperties.CreateBranchId());
							reInviteRequest.Header.Vias.PushViaHeader(viaHeader);
						}

						reInviteRequest.Body = sipRequest.Body;
						reInviteRequest.Header.ContentLength = sipRequest.Header.ContentLength;
						reInviteRequest.Header.ContentType = sipRequest.Header.ContentType;
						SIPEndPoint reinviteEndPoint = null;
						SIPDNSLookupResult lookupResult = this.SipTransport.GetRequestEndPoint(reInviteRequest, null, false);
						if(lookupResult.LookupError != null)
						{
						}
						else
						{
							reinviteEndPoint = lookupResult.GetSIPEndPoint();
						}
						SIPEndPoint reinviteLocalEndpoint = null;
						if(leg.PartnerLeg.SipClient != null)
							reinviteLocalEndpoint = leg.PartnerLeg.SipClient.ServerTransaction.LocalSIPEndPoint;
						else if(leg.PartnerLeg.SipServer != null)
							reinviteLocalEndpoint = leg.PartnerLeg.SipEndpointLocal;

						leg.SIPInvite = sipRequest;
						leg.PartnerLeg.SIPInvite = reInviteRequest;

						UACInviteTransaction reInviteTransaction = this.SipTransport.CreateUACTransaction(reInviteRequest, reinviteEndPoint, reinviteLocalEndpoint, null);
						reInviteTransaction.CDR = null;
						reInviteTransaction.UACInviteTransactionFinalResponseReceived += ReInviteTransaction_UACInviteTransactionFinalResponseReceived;
						reInviteTransaction.SendInviteRequest(reinviteEndPoint, reInviteRequest);
					}

					return;
				}
				lock(legA)
				{
					legA.SIPInvite = sipRequest;
					legA.CallId = sipRequest.Header.CallId;
					UASInviteTransaction inviteTransaction = this.SipTransport.CreateUASTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, null);
					legA.SipEndpointLocal = localSIPEndPoint;
					legA.SipServer = new SIPServerUserAgent(this.SipTransport, null, null, null, SIPCallDirection.In, null, null, null, inviteTransaction);
					legA.SipServer.CallCancelled += SipServer_CallCancelled;

					SIPResponse tryingResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Trying, null);

					legA.SIPFrom = sipRequest.Header.From;
					legA.SIPTo = tryingResponse.Header.To;

					////need valid sip target here
					string dst = $"sip:siptrunk@10.0.0.157:5060;transport=tcp";

					Model.CallLeg legB = legA.PartnerLeg;
					if(legB == null)
					{
						legB = new Model.CallLeg();
						legB.LegType = Model.CallLegType.LegB;
					}

					lock(legB)
					{
						string body = sipRequest.Body;

						SIPURI callURI = SIPURI.ParseSIPURIRelaxed(dst);

						SIPCallDescriptor callDescriptor = new SIPCallDescriptor(
								null,
								null,
								callURI.ToString(),
								legA.LocalContact.ToString(),
								callURI.User + "<" + callURI.ToString() + ">",
								null,
								null,
								null,
								SIPCallDirection.Out,
								"application/sdp",
								body,
								null);
						legB.SipClient = new SIPClientUserAgent(this.SipTransport, null, null, null, SIPLogDelegate);
						legB.SipClient.CallAnswered += SipClient_CallAnswered;
						legB.SipClient.CallFailed += SipClient_CallFailed;
						legB.SipClient.CallRinging += SipClient_CallRinging;
						legB.SipClient.CallTrying += SipClient_CallTrying;

						legA.CallState = Model.CallState.RxInvited;

						legB.PartnerLeg = legA;
						legA.PartnerLeg = legB;
						this.CallLegs.Add(legB);

						legB.CallState = Model.CallState.TxInvited;
						legB.SipClient.Call(callDescriptor);
					}
				}
			}
			else
			{
				switch(sipRequest.Method)
				{
					case SIPMethodsEnum.REGISTER:
						{
							SIPResponse ok = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
							ok.Header.Contact = sipRequest.Header.Contact;
							this.SipTransport.SendResponse(sipRequest.RemoteSIPEndPoint, ok);
						}
						break;
					case SIPMethodsEnum.BYE:
						{
							if(leg == null)
							{
								SIPNonInviteTransaction okTransaction = this.SipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, null);
								SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
								okTransaction.SendFinalResponse(okResponse);
								return;
							}
							if(leg.CallState != Model.CallState.Idle)
							{
								lock(leg)
								{
									SIPNonInviteTransaction okTransaction = this.SipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, null);
									SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
									okTransaction.SendFinalResponse(okResponse);
									leg.CallState = Model.CallState.Idle;

									Model.CallLeg PartnerLeg = leg.PartnerLeg;
									if(PartnerLeg.CallState != Model.CallState.Idle)
									{
										lock(PartnerLeg)
										{
											if(PartnerLeg.SipClient != null)
											{
												if(PartnerLeg.SipClient.SIPDialogue != null)
													PartnerLeg.SipClient.SIPDialogue.Hangup(this.SipTransport, null);
											}
											if(PartnerLeg.SipServer != null)
											{
												if(PartnerLeg.SipServer.SIPDialogue != null)
													PartnerLeg.SipServer.SIPDialogue.Hangup(this.SipTransport, null);
											}
										}
									}
									else
									{

									}
								}
								this.CleanupLeg(leg);
							}
							else
							{
								SIPResponse rsp = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, "Call/Transaction Does Not Exist");
								this.SipTransport.SendResponse(remoteEndPoint, rsp);
								this.CleanupLeg(leg);
							}
						}
						break;
					case SIPMethodsEnum.CANCEL:
						{
							if(leg == null)
							{
								UASInviteTransaction inviteTransaction = (UASInviteTransaction)this.SipTransport.GetTransaction(SIPTransaction.GetRequestTransactionId(sipRequest.Header.Vias.TopViaHeader.Branch, SIPMethodsEnum.INVITE));

								if(inviteTransaction != null)
								{
									SIPCancelTransaction cancelTransaction = this.SipTransport.CreateCancelTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, inviteTransaction);
									cancelTransaction.GotRequest(localSIPEndPoint, remoteEndPoint, sipRequest);
								}
								else
								{
									SIPResponse noCallLegResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
									this.SipTransport.SendResponse(noCallLegResponse);
								}
								return;
							}
							if(leg.CallState != Model.CallState.Idle)
							{
								lock(leg)
								{
									Model.CallLeg PartnerLeg = leg.PartnerLeg;
									if(PartnerLeg.CallState != Model.CallState.Idle)
									{
										lock(PartnerLeg)
										{
											if(PartnerLeg.SipClient != null)
											{
												PartnerLeg.SipClient.Cancel();
											}
										}
									}
									else
									{
									}
								}
								this.CleanupLeg(leg);
							}
							else
							{
								SIPResponse rsp = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, "Call/Transaction Does Not Exist");
								this.SipTransport.SendResponse(remoteEndPoint, rsp);
								this.CleanupLeg(leg);
							}
						}
						break;
					case SIPMethodsEnum.OPTIONS:
					case SIPMethodsEnum.PING:
						{
							SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
							this.SipTransport.SendResponse(okResponse);
						}
						break;
					default:
						{
							if(sipRequest.Method != SIPMethodsEnum.ACK)
							{
								SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
								this.SipTransport.SendResponse(okResponse);
							}
							else if(leg != null)
							{
								switch(leg.CallState)
								{
									case Model.CallState.RxOffHold:
									case Model.CallState.RxInvited:
										leg.CallState = Model.CallState.Connected;
										break;
								}
							}
						}
						break;
				}
			}
		}

		private void ReInviteTransaction_UACInviteTransactionFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
		{
			try
			{
				if(sipResponse.StatusCode >= 200 && sipResponse.StatusCode <= 299)
				{
					if(!string.IsNullOrWhiteSpace(sipResponse.Body))
					{
						Model.CallLeg leg = (from l in this.CallLegs where l.CallId == sipResponse.Header.CallId select l).FirstOrDefault();
						if(leg != null)
						{
							string body = sipResponse.Body;
							string bodytype = sipResponse.Header.ContentType;
							SIPResponse okResponse = SIPTransport.GetResponse(leg.PartnerLeg.SIPInvite, SIPResponseStatusCodesEnum.Ok, null);
							okResponse.Body = body;
							okResponse.Header.ContentType = bodytype;
							this.SipTransport.SendResponse(okResponse);
						}
					}
					else
					{
					}
				}
				else
				{
				}

			}
			catch(Exception excp)
			{
				throw excp;
			}
		}
		private void SIPTransport_ResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse)
		{
			var legA = (from leg in this.CallLegs where leg.CallId == sipResponse.Header.CallId select leg).FirstOrDefault();
			if(legA == null)
			{
				return;
			}
			if(sipResponse.StatusCode == 200 && sipResponse.Header.CSeqMethod == SIPMethodsEnum.BYE)
			{
				legA.CallState = Model.CallState.Idle;
				this.CleanupLeg(legA);
			}
		}


		private void SipClient_CallAnswered(ISIPClientUserAgent uac, SIPResponse sipResponse)
		{
			var legB = (from leg in this.CallLegs where leg.SipClient == uac select leg).FirstOrDefault();
			int CallResponseCode = sipResponse.StatusCode;
			if(legB != null)
			{
				legB.SipEndpointLocal = uac.ServerTransaction.LocalSIPEndPoint;
				legB.CallId = sipResponse.Header.CallId;
				Model.CallLeg legA = legB.PartnerLeg;
				if(legA != null)
				{
					if(legA.SipServer != null)
					{
					}
				}
				if(sipResponse.StatusCode >= 200 && sipResponse.StatusCode <= 299)
				{
					if(sipResponse.Header.ContentType != "application/sdp")
					{
						legA.SipServer.Reject(sipResponse.Status, sipResponse.ReasonPhrase, null);
					}
					else if(string.IsNullOrWhiteSpace(sipResponse.Body))
					{
						legA.SipServer.Reject(sipResponse.Status, sipResponse.ReasonPhrase, null);
					}
					else
					{
						legB.SIPTo = sipResponse.Header.To;
						if(sipResponse.Header.Contact.Count>0)
							legB.RemoteContact = sipResponse.Header.Contact[0];
						legB.CallState = Model.CallState.Connected;

						legA.SipServer.Answer("application/sdp", sipResponse.Body, null, SIPDialogueTransferModesEnum.NotAllowed);
						legA.CallState = Model.CallState.Connected;
					}
				}
				else
				{
					legA.SipServer.Reject(sipResponse.Status, sipResponse.ReasonPhrase, null);
					legB.CallState = Model.CallState.Idle;
				}
			}
		}

		private void SipClient_CallTrying(ISIPClientUserAgent uac, SIPResponse sipResponse)
		{
			var legB = (from leg in this.CallLegs where leg.SipClient == uac select leg).FirstOrDefault();
			if(legB != null)
			{
				if(uac.ServerTransaction.TransactionRequest.Header.Contact.Count > 0)
					legB.LocalContact = uac.ServerTransaction.TransactionRequest.Header.Contact[0];
				legB.SipEndpointLocal = uac.ServerTransaction.LocalSIPEndPoint;
				legB.CallId = sipResponse.Header.CallId;
				Model.CallLeg legA = legB.PartnerLeg;
				if(legA != null)
				{
					if(legA.SipServer != null)
					{
						legA.SipServer.Progress(SIPResponseStatusCodesEnum.Trying, null, null, null, null);
					}
				}
			}
		}

		private void SipClient_CallRinging(ISIPClientUserAgent uac, SIPResponse sipResponse)
		{
			var legB = (from leg in this.CallLegs where leg.SipClient == uac select leg).FirstOrDefault();
			if(legB != null)
			{
				if(uac.ServerTransaction.TransactionRequest.Header.Contact.Count>0)
					legB.LocalContact = uac.ServerTransaction.TransactionRequest.Header.Contact[0];
				legB.SipEndpointLocal = uac.ServerTransaction.LocalSIPEndPoint;
				legB.CallId = sipResponse.Header.CallId;
				Model.CallLeg legA = legB.PartnerLeg;
				if(legA != null)
				{
					if(legA.SipServer != null)
					{
						legA.SipServer.Progress(SIPResponseStatusCodesEnum.Ringing, null, null, null, null);
					}
				}
			}
		}

		private void SipClient_CallFailed(ISIPClientUserAgent uac, string errorMessage)
		{
			var legB = (from leg in this.CallLegs where leg.SipClient == uac select leg).FirstOrDefault();
			if(legB != null)
			{
				legB.SipEndpointLocal = uac.ServerTransaction.LocalSIPEndPoint;
				Model.CallLeg legA = legB.PartnerLeg;
				if(legA != null)
				{
					if(legA.SipServer != null)
					{
						legA.SipServer.Reject(SIPResponseStatusCodesEnum.ServiceUnavailable, errorMessage, null);
					}
				}
			}
		}

		private void SipServer_CallCancelled(ISIPServerUserAgent uas)
		{
			var legA = (from leg in this.CallLegs where leg.SipServer == uas select leg).FirstOrDefault();
			if(legA != null)
			{
				Model.CallLeg legB = legA.PartnerLeg;
				if(legB != null)
				{
					if(legB.SipClient != null)
					{
						legB.SipClient.Cancel();
					}
				}
			}

		}

		internal void CleanupLeg(Model.CallLeg leg)
		{
			return;
			if(leg.PartnerLeg != null)
			{
				leg.PartnerLeg = null;
				this.CallLegs.Remove(leg.PartnerLeg);
				leg.PartnerLeg.Dispose();
			}
			leg.PartnerLeg = null;
			this.CallLegs.Remove(leg);
			leg.Dispose();

			GC.Collect();
		}


	}
}
