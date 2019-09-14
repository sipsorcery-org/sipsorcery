using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace SIPSorcery.SIP.App
{
    public delegate List<SIPDialogue> GetSIPDialogueListDelegate(Expression<Func<SIPDialogue, bool>> where, string orderByField, int offset, int limit);
    public delegate SIPDialogue GetSIPDialogueDelegate(Guid id);

    public class SIPDialogEventSubscription : SIPEventSubscription
    {
        private const int MAX_DIALOGUES_FOR_NOTIFY = 25;

        private static string m_contentType = SIPMIMETypes.DIALOG_INFO_CONTENT_TYPE;

        private SIPEventDialogInfo DialogInfo;

        private GetSIPDialogueListDelegate GetDialogues_External;
        private GetSIPDialogueDelegate GetDialogue_External;

        public override SIPEventPackage SubscriptionEventPackage
        {
            get { return SIPEventPackage.Dialog; }
        }

        public override string MonitorFilter
        {
            get { return "dialog " + ResourceURI.ToString(); }
        }

        public override string NotifyContentType
        {
            get { return m_contentType; }
        }

        public SIPDialogEventSubscription(
            SIPMonitorLogDelegate log,
            string sessionID,
            SIPURI resourceURI,
            SIPURI canonincalResourceURI,
            string filter,
            SIPDialogue subscriptionDialogue,
            int expiry,
            GetSIPDialogueListDelegate getDialogues,
            GetSIPDialogueDelegate getDialogue
            )
            : base(log, sessionID, resourceURI, canonincalResourceURI, filter, subscriptionDialogue, expiry)
        {
            GetDialogues_External = getDialogues;
            GetDialogue_External = getDialogue;
            DialogInfo = new SIPEventDialogInfo(0, SIPEventDialogInfoStateEnum.full, resourceURI);
        }

        public override void GetFullState()
        {
            try
            {
                DialogInfo.State = SIPEventDialogInfoStateEnum.full;
                List<SIPDialogue> dialogues = GetDialogues_External(d => d.Owner == SubscriptionDialogue.Owner, "Inserted", 0, MAX_DIALOGUES_FOR_NOTIFY);

                foreach (SIPDialogue dialogue in dialogues)
                {
                    DialogInfo.DialogItems.Add(new SIPEventDialog(dialogue.Id.ToString(), "confirmed", dialogue));
                }

                MonitorLogEvent_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.NotifySent, "Full state notification for dialog and " + ResourceURI.ToString() + ".", SubscriptionDialogue.Owner));
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPDialogEventSubscription GetFullState. " + excp.Message);
            }
        }

        public override string GetNotifyBody()
        {
            return DialogInfo.ToXMLText();
        }

        public override bool AddMonitorEvent(SIPMonitorMachineEvent machineEvent)
        {
            try
            {
                lock (DialogInfo)
                {
                    MonitorLogEvent_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.Monitor, "Monitor event " + machineEvent.MachineEventType + " dialog " + ResourceURI.ToString() + " (ID " + machineEvent.ResourceID + ").", SubscriptionDialogue.Owner));

                    string state = GetStateForEventType(machineEvent.MachineEventType);

                    if (machineEvent.MachineEventType == SIPMonitorMachineEventTypesEnum.SIPDialogueRemoved)
                    {
                        DialogInfo.DialogItems.Add(new SIPEventDialog(machineEvent.ResourceID, state, null));
                        return true;
                    }
                    else
                    {
                        SIPDialogue sipDialogue = GetDialogue_External(new Guid(machineEvent.ResourceID));

                        if (sipDialogue == null)
                        {
                            // Couldn't find the dialogue in the database so it must be terminated.
                            DialogInfo.DialogItems.Add(new SIPEventDialog(machineEvent.ResourceID, "terminated", null));
                            return true;
                        }
                        else if (machineEvent.MachineEventType == SIPMonitorMachineEventTypesEnum.SIPDialogueTransfer)
                        {
                            // For dialog transfer events add both dialogs involved to the notification.
                            DialogInfo.DialogItems.Add(new SIPEventDialog(sipDialogue.Id.ToString(), state, sipDialogue));

                            if (sipDialogue.BridgeId != Guid.Empty)
                            {
                                SIPDialogue bridgedDialogue = GetDialogues_External(d => d.BridgeId == sipDialogue.BridgeId && d.Id != sipDialogue.Id, null, 0, 1).FirstOrDefault();
                                if (bridgedDialogue != null)
                                {
                                    DialogInfo.DialogItems.Add(new SIPEventDialog(bridgedDialogue.Id.ToString(), state, bridgedDialogue));
                                }
                            }

                            return true;
                        }
                        else
                        {
                            DialogInfo.DialogItems.Add(new SIPEventDialog(sipDialogue.Id.ToString(), state, sipDialogue));
                            return true;
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                MonitorLogEvent_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.Monitor, "Exception AddMonitorEvent. " + excp.Message, null));
                logger.Error("Exception SIPDialogEventSubscription AddMonitorEvent. " + excp.Message);
                throw;
            }
        }

        public override void NotificationSent()
        {
            if (DialogInfo.State == SIPEventDialogInfoStateEnum.full)
            {
                MonitorLogEvent_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.NotifySent, "Full state notification sent for " + DialogInfo.Entity.ToString() + ", version " + DialogInfo.Version + ", cseq " + SubscriptionDialogue.CSeq + ".", SubscriptionDialogue.Owner));
            }
            else
            {
                foreach (SIPEventDialog dialog in DialogInfo.DialogItems)
                {
                    string remoteURI = (dialog.RemoteParticipant != null && dialog.RemoteParticipant.URI != null) ? ", " + dialog.RemoteParticipant.URI.ToString() : null;
                    MonitorLogEvent_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.NotifySent, "Partial state notification sent for " + DialogInfo.Entity.ToString() + " dialog ID " + dialog.ID + " " + dialog.State + ", version " + DialogInfo.Version + remoteURI + ", cseq " + SubscriptionDialogue.CSeq + ".", SubscriptionDialogue.Owner));
                }
            }

            DialogInfo.State = SIPEventDialogInfoStateEnum.partial;
            DialogInfo.DialogItems.RemoveAll(x => x.HasBeenSent);
            DialogInfo.Version++;
        }

        private string GetStateForEventType(SIPMonitorMachineEventTypesEnum machineEventType)
        {
            switch (machineEventType)
            {
                case SIPMonitorMachineEventTypesEnum.SIPDialogueCreated: return "confirmed";
                case SIPMonitorMachineEventTypesEnum.SIPDialogueRemoved: return "terminated";
                case SIPMonitorMachineEventTypesEnum.SIPDialogueUpdated: return "updated";
                case SIPMonitorMachineEventTypesEnum.SIPDialogueTransfer: return "updated";
                default: throw new ApplicationException("The state for a dialog SIP event could not be determined from the monitor event type of " + machineEventType + ".");
            }
        }
    }
}
