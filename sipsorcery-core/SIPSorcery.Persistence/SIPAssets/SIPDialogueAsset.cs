using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using SIPSorcery.Sys;

#if !SILVERLIGHT
using System.Data;
using System.Data.Linq;
using System.Data.Linq.Mapping;
#endif

namespace SIPSorcery.SIP.App
{
    [Table(Name = "sipdialogues")]
    public class SIPDialogueAsset: ISIPAsset
    {
        public const string XML_DOCUMENT_ELEMENT_NAME = "sipdialogues";
        public const string XML_ELEMENT_NAME = "sipdialogue";

        private static string m_newLine = AppState.NewLine;

        [IgnoreDataMember]
        public SIPDialogue SIPDialogue;

        [Column(Name = "id", DbType = "varchar(36)", IsPrimaryKey = true, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public Guid Id
        {
            get { return SIPDialogue.Id; }
            set { SIPDialogue.Id = value; }
        }

        [Column(Name = "owner", DbType = "varchar(32)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string Owner
        {
            get { return SIPDialogue.Owner; }
            set { SIPDialogue.Owner = value; }
        }

        [Column(Name = "adminmemberid", DbType = "varchar(32)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public string AdminMemberId
        {
            get { return SIPDialogue.AdminMemberId; }
            set { SIPDialogue.AdminMemberId = value; }
        }

        //[Column(Name = "dialogueid", DbType = "varchar(256)", IsPrimaryKey = false, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        //public string DialogueId {
        //   get { return SIPDialogue.DialogueId; }
        //    set { SIPDialogue.DialogueId = value; }
        //}

        [Column(Name = "localtag", DbType = "varchar(64)", IsPrimaryKey = false, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string LocalTag
        {
            get { return SIPDialogue.LocalTag; }
            set { SIPDialogue.LocalTag = value; }
        }

        [Column(Name = "remotetag", DbType = "varchar(64)", IsPrimaryKey = false, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string RemoteTag
        {
            get { return SIPDialogue.RemoteTag; }
            set { SIPDialogue.RemoteTag = value; }
        }

        [Column(Name = "callid", DbType = "varchar(128)", IsPrimaryKey = false, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string CallId
        {
            get { return SIPDialogue.CallId; }
            set { SIPDialogue.CallId = value; }
        }

        [Column(Name = "cseq", DbType = "int", IsPrimaryKey = false, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public int CSeq
        {
            get { return SIPDialogue.CSeq; }
            set { SIPDialogue.CSeq = value; }
        }

        [Column(Name = "bridgeid", DbType = "varchar(36)", IsPrimaryKey = false, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string BridgeId
        {
            get { return SIPDialogue.BridgeId.ToString(); }
            set { SIPDialogue.BridgeId = (!value.IsNullOrBlank()) ? new Guid(value) : Guid.Empty; }
        }

        [Column(Name = "remotetarget", DbType = "varchar(256)", IsPrimaryKey = false, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string RemoteTarget
        {
            get { return SIPDialogue.RemoteTarget.ToString(); }
            set { SIPDialogue.RemoteTarget = (!value.IsNullOrBlank()) ? SIPURI.ParseSIPURI(value) : null; }
        }

        [IgnoreDataMember]
        [Column(Name = "localuserfield", DbType = "varchar(512)", IsPrimaryKey = false, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string LocalUserField
        {
            get { return SIPDialogue.LocalUserField.ToString(); }
            set { SIPDialogue.LocalUserField = (!value.IsNullOrBlank()) ? SIPUserField.ParseSIPUserField(value) : null; }
        }

        [DataMember]
        [Column(Name = "remoteuserfield", DbType = "varchar(512)", IsPrimaryKey = false, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string RemoteUserField
        {
            get { return SIPDialogue.RemoteUserField.ToString(); }
            set { SIPDialogue.RemoteUserField = (!value.IsNullOrBlank()) ? SIPUserField.ParseSIPUserField(value) : null; }
        }

        [Column(Name = "proxysipsocket", DbType = "varchar(64)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string ProxySIPSocket
        {
            get { return SIPDialogue.ProxySendFrom; }
            set { SIPDialogue.ProxySendFrom = value; }
        }

        [IgnoreDataMember]
        [Column(Name = "routeset", DbType = "varchar(512)", IsPrimaryKey = false, CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public string RouteSet
        {
            get { return (SIPDialogue.RouteSet != null) ? SIPDialogue.RouteSet.ToString() : null; }
            set { SIPDialogue.RouteSet = (!value.IsNullOrBlank()) ? SIPRouteSet.ParseSIPRouteSet(value) : null; }
        }

        [Column(Name = "cdrid", DbType = "varchar(36)", IsPrimaryKey = false, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string CDRId
        {
            get { return SIPDialogue.CDRId.ToString(); }
            set { SIPDialogue.CDRId = (!value.IsNullOrBlank()) ? new Guid(value) : Guid.Empty; }
        }

        [DataMember]
        [Column(Name = "calldurationlimit", DbType = "int", IsPrimaryKey = false, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public int CallDurationLimit
        {
            get { return SIPDialogue.CallDurationLimit; }
            set { SIPDialogue.CallDurationLimit = value; }
        }

        [DataMember]
        [Column(Name = "inserted", DbType = "datetimeoffset", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public DateTimeOffset Inserted
        {
            get { return SIPDialogue.Inserted; }
            set { SIPDialogue.Inserted = value.DateTime; }
        }

        [DataMember]
        [Column(Name = "hangupat", DbType = "datetimeoffset", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public DateTimeOffset? HangupAt
        {
            get
            {
                if (CallDurationLimit != 0)
                {
                    return Inserted.AddSeconds(CallDurationLimit);
                }
                else
                {
                    return null;
                }
            }
            set { }     // The hangup time is stored in the database for info. It is calculated from inserted and calldurationlimit and does not need a setter.
        }

        [DataMember]
        [Column(Name = "transfermode", DbType = "varchar(16)", IsPrimaryKey = false, CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public string TransferMode
        {
            get { return SIPDialogue.TransferMode.ToString(); }
            set
            {
                if (!value.IsNullOrBlank())
                {
                    SIPDialogue.TransferMode = (SIPDialogueTransferModesEnum)Enum.Parse(typeof(SIPDialogueTransferModesEnum), value, true);
                }
            }
        }

        [Column(Name = "direction", DbType = "varchar(3)", IsPrimaryKey = false, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string Direction
        {
            get { return SIPDialogue.Direction.ToString(); }
            set 
            {
                if (!value.IsNullOrBlank())
                {
                    SIPDialogue.Direction = (SIPCallDirection)Enum.Parse(typeof(SIPCallDirection), value, true);
                }
            }
        }

        [Column(Name = "sdp", DbType = "varchar(2048)", IsPrimaryKey = false, CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public string SDP
        {
            get { return SIPDialogue.SDP; }
            set { SIPDialogue.SDP = value; }
        }

        [Column(Name = "remotesdp", DbType = "varchar(2048)", IsPrimaryKey = false, CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public string RemoteSDP
        {
            get { return SIPDialogue.RemoteSDP; }
            set { SIPDialogue.RemoteSDP = value; }
        }

        //[Column(Name = "switchboarddescription", DbType = "varchar(1024)", IsPrimaryKey = false, CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        //public string SwitchboardDescription
        //{
        //    get { return SIPDialogue.SwitchboardDescription; }
        //    set { SIPDialogue.SwitchboardDescription = value; }
        //}

        //[Column(Name = "switchboardcallerdescription", DbType = "varchar(1024)", IsPrimaryKey = false, CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        //public string SwitchboardCallerDescription 
        //{
        //    get { return SIPDialogue.SwitchboardCallerDescription; }
        //    set { SIPDialogue.SwitchboardCallerDescription = value; } 
        //}

        [Column(Name = "switchboardowner", DbType = "varchar(1024)", IsPrimaryKey = false, CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public string SwitchboardOwner
        {
            get { return SIPDialogue.SwitchboardOwner; }
            set { SIPDialogue.SwitchboardOwner = value; }
        }

        [Column(Name = "SwitchboardLineName", DbType = "varchar(128)", IsPrimaryKey = false, CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public string SwitchboardLineName
        {
            get { return SIPDialogue.SwitchboardLineName; }
            set { SIPDialogue.SwitchboardLineName = value; }
        }

        [Column(Name = "CRMPersonName", DbType = "varchar(256)", IsPrimaryKey = false, CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public string CRMPersonName
        {
            get { return SIPDialogue.CRMPersonName; }
            set { SIPDialogue.CRMPersonName = value; }
        }

        [Column(Name = "CRMCompanyName", DbType = "varchar(256)", IsPrimaryKey = false, CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public string CRMCompanyName
        {
            get { return SIPDialogue.CRMCompanyName; }
            set { SIPDialogue.CRMCompanyName = value; }
        }

        [Column(Name = "CRMPictureURL", DbType = "varchar(1024)", IsPrimaryKey = false, CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public string CRMPictureURL
        {
            get { return SIPDialogue.CRMPictureURL; }
            set { SIPDialogue.CRMPictureURL = value; }
        }

        public SIPDialogueAsset()
        {
            SIPDialogue = new SIPDialogue();
        }

        public SIPDialogueAsset(SIPDialogue sipDialogue)
        {
            SIPDialogue = sipDialogue;
        }

#if !SILVERLIGHT

        public SIPDialogueAsset(DataRow row)
        {
            Load(row);
        }

        public DataTable GetTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add(new DataColumn("id", typeof(String)));
            table.Columns.Add(new DataColumn("owner", typeof(String)));
            table.Columns.Add(new DataColumn("adminmemberid", typeof(String)));
            table.Columns.Add(new DataColumn("localtag", typeof(String)));
            table.Columns.Add(new DataColumn("remotetag", typeof(String)));
            table.Columns.Add(new DataColumn("localuserfield", typeof(String)));
            table.Columns.Add(new DataColumn("remoteuserfield", typeof(String)));
            table.Columns.Add(new DataColumn("callid", typeof(String)));
            table.Columns.Add(new DataColumn("cseq", typeof(Int32)));
            table.Columns.Add(new DataColumn("bridgeid", typeof(String)));
            table.Columns.Add(new DataColumn("proxysipsocket", typeof(String)));
            table.Columns.Add(new DataColumn("remotetarget", typeof(String)));
            table.Columns.Add(new DataColumn("routeset", typeof(String)));
            table.Columns.Add(new DataColumn("cdrid", typeof(String)));
            table.Columns.Add(new DataColumn("calldurationlimit", typeof(Int32)));
            table.Columns.Add(new DataColumn("hangupat", typeof(DateTimeOffset)));
            table.Columns.Add(new DataColumn("inserted", typeof(DateTimeOffset)));
            table.Columns.Add(new DataColumn("direction", typeof(DateTimeOffset)));
            table.Columns.Add(new DataColumn("sdp", typeof(String)));
            table.Columns.Add(new DataColumn("remotesdp", typeof(String)));
            table.Columns.Add(new DataColumn("transfermode", typeof(String)));
            //table.Columns.Add(new DataColumn("switchboarddescription", typeof(String)));
            //table.Columns.Add(new DataColumn("switchboardcallerdescription", typeof(String)));
            table.Columns.Add(new DataColumn("switchboardowner", typeof(String)));
            table.Columns.Add(new DataColumn("switchboardlinename", typeof(String)));
            table.Columns.Add(new DataColumn("crmpersonname", typeof(String)));
            table.Columns.Add(new DataColumn("crmcompanyname", typeof(String)));
            table.Columns.Add(new DataColumn("crmpictureurl", typeof(String)));
            return table;
        }

        public void Load(DataRow row)
        {
            SIPDialogue = new SIPDialogue();
            SIPDialogue.Id = new Guid(row["id"] as string);
            SIPDialogue.Owner = row["owner"] as string;
            SIPDialogue.AdminMemberId = row["adminmemberid"] as string;
            SIPDialogue.LocalTag = row["localtag"] as string;
            SIPDialogue.RemoteTag = row["remotetag"] as string;
            SIPDialogue.LocalUserField = SIPUserField.ParseSIPUserField(row["localuserfield"] as string);
            SIPDialogue.RemoteUserField = SIPUserField.ParseSIPUserField(row["remoteuserfield"] as string);
            SIPDialogue.CallId = row["callid"] as string;
            SIPDialogue.CSeq = Convert.ToInt32(row["cseq"]);
            SIPDialogue.BridgeId = new Guid(row["bridgeid"] as string);
            SIPDialogue.RemoteTarget = SIPURI.ParseSIPURI(row["remotetarget"] as string);
            SIPDialogue.RouteSet = (row["routeset"] != null && row["routeset"] != DBNull.Value && !(row["routeset"] as string).IsNullOrBlank()) ? SIPRouteSet.ParseSIPRouteSet(row["routeset"] as string) : null;
            SIPDialogue.ProxySendFrom = (row["proxysipsocket"] != null && row["proxysipsocket"] != DBNull.Value) ? row["proxysipsocket"] as string : null;
            SIPDialogue.CDRId = new Guid(row["cdrid"] as string);
            SIPDialogue.CallDurationLimit = (row["calldurationlimit"] != null && row["calldurationlimit"] != DBNull.Value) ? Convert.ToInt32(row["calldurationlimit"]) : 0;
            SIPDialogue.Direction = (row["direction"] != null) ? (SIPCallDirection)Enum.Parse(typeof(SIPCallDirection), row["direction"] as string, true) : SIPCallDirection.None;
            Inserted = DateTimeOffset.Parse(row["inserted"] as string);
            TransferMode = row["transfermode"] as string;
            SDP = row["sdp"] as string;
            RemoteSDP = row["remotesdp"] as string;
            //SIPDialogue.SwitchboardCallerDescription = (row["switchboardcallerdescription"] != null && row["switchboardcallerdescription"] != DBNull.Value) ? row["switchboardcallerdescription"] as string : null;
            //SIPDialogue.SwitchboardDescription = (row["switchboarddescription"] != null && row["switchboarddescription"] != DBNull.Value) ? row["switchboarddescription"] as string : null;
            SIPDialogue.SwitchboardOwner = (row["switchboardowner"] != null && row["switchboardowner"] != DBNull.Value) ? row["switchboardowner"] as string : null;
            SIPDialogue.SwitchboardLineName = (row["switchboardlinename"] != null && row["switchboardlinename"] != DBNull.Value) ? row["switchboardlinename"] as string : null;
            SIPDialogue.CRMPersonName = (row["crmpersonname"] != null && row["crmpersonname"] != DBNull.Value) ? row["crmpersonname"] as string : null;
            SIPDialogue.CRMCompanyName = (row["crmcompanyname"] != null && row["crmcompanyname"] != DBNull.Value) ? row["crmcompanyname"] as string : null;
            SIPDialogue.CRMPictureURL = (row["crmpictureurl"] != null && row["crmpictureurl"] != DBNull.Value) ? row["crmpictureurl"] as string : null;
        }

        //public Dictionary<Guid, object> Load(XmlDocument dom)
        //{
        //    return SIPAssetXMLPersistor<SIPDialogueAsset>.LoadAssetsFromXMLRecordSet(dom);
        //}

#endif

        public void Load(XElement dialogueElement)
        {
            SIPDialogue = new SIPDialogue();
            SIPDialogue.Id = new Guid(dialogueElement.Element("id").Value);
            SIPDialogue.Owner = dialogueElement.Element("owner").Value;
            SIPDialogue.AdminMemberId = dialogueElement.Element("adminmemberid").Value;
            SIPDialogue.LocalTag = dialogueElement.Element("localtag").Value;
            SIPDialogue.RemoteTag = dialogueElement.Element("remotetag").Value;
            SIPDialogue.LocalUserField = SIPUserField.ParseSIPUserField(dialogueElement.Element("localuserfield").Value);
            SIPDialogue.RemoteUserField = SIPUserField.ParseSIPUserField(dialogueElement.Element("remoteuserfield").Value);
            SIPDialogue.CallId = dialogueElement.Element("callid").Value;
            SIPDialogue.CSeq = Convert.ToInt32(dialogueElement.Element("cseq").Value);
            SIPDialogue.BridgeId = new Guid(dialogueElement.Element("bridgeid").Value);
            SIPDialogue.RemoteTarget = SIPURI.ParseSIPURI(dialogueElement.Element("remotetarget").Value);
            SIPDialogue.RouteSet = (dialogueElement.Element("routeset") != null && !dialogueElement.Element("routeset").Value.IsNullOrBlank()) ? SIPRouteSet.ParseSIPRouteSet(dialogueElement.Element("routeset").Value) : null;
            SIPDialogue.ProxySendFrom = (dialogueElement.Element("proxysipsocket") != null) ? dialogueElement.Element("proxysipsocket").Value : null;
            SIPDialogue.CDRId = new Guid(dialogueElement.Element("cdrid").Value);
            SIPDialogue.CallDurationLimit = (dialogueElement.Element("calldurationlimit") != null) ? Convert.ToInt32(dialogueElement.Element("calldurationlimit").Value) : 0;
            SIPDialogue.Direction = (dialogueElement.Element("direction") != null) ? (SIPCallDirection)Enum.Parse(typeof(SIPCallDirection), dialogueElement.Element("direction").Value, true) : SIPCallDirection.None;
            Inserted = DateTimeOffset.Parse(dialogueElement.Element("inserted").Value);
            TransferMode = dialogueElement.Element("transfermode").Value;
            SDP = dialogueElement.Element("sdp").Value;
            RemoteSDP = dialogueElement.Element("remotesdp").Value;
            //SIPDialogue.SwitchboardCallerDescription = (dialogueElement.Element("switchboardcallerdescription") != null) ? dialogueElement.Element("switchboardcallerdescription").Value : null;
            //SIPDialogue.SwitchboardDescription = (dialogueElement.Element("switchboarddescription") != null) ? dialogueElement.Element("switchboarddescription").Value : null;
            SIPDialogue.SwitchboardOwner = (dialogueElement.Element("switchboardowner") != null) ? dialogueElement.Element("switchboardowner").Value : null;
            SIPDialogue.SwitchboardLineName = (dialogueElement.Element("switchboardlinename") != null) ? dialogueElement.Element("switchboardlinename").Value : null;
            SIPDialogue.CRMPersonName = (dialogueElement.Element("crmpersonname") != null) ? dialogueElement.Element("crmpersonname").Value : null;
            SIPDialogue.CRMCompanyName = (dialogueElement.Element("crmcompanyname") != null) ? dialogueElement.Element("crmcompanyname").Value : null;
            SIPDialogue.CRMPictureURL = (dialogueElement.Element("crmpictureurl") != null) ? dialogueElement.Element("crmpictureurl").Value : null;
        }

        public string ToXML()
        {
            string dialogueXML =
                " <" + XML_ELEMENT_NAME + ">" + m_newLine +
                ToXMLNoParent() +
                " </" + XML_ELEMENT_NAME + ">" + m_newLine;

            return dialogueXML;
        }

        public string ToXMLNoParent()
        {
            string hanupAtStr = (HangupAt != null) ? HangupAt.Value.ToString("o") : null;

            string dialogueXML =
                 "  <id>" + SIPDialogue.Id + "</id>" + m_newLine +
                 "  <owner>" + SIPDialogue.Owner + "</owner>" + m_newLine +
                 "  <adminmemberid>" + SIPDialogue.AdminMemberId + "</adminmemberid>" + m_newLine +
                 "  <localtag>" + SIPDialogue.LocalTag + "</localtag>" + m_newLine +
                 "  <remotetag>" + SIPDialogue.RemoteTag + "</remotetag>" + m_newLine +
                 "  <callid>" + SIPDialogue.CallId + "</callid>" + m_newLine +
                 "  <cseq>" + SIPDialogue.CSeq + "</cseq>" + m_newLine +
                 "  <bridgeid>" + SIPDialogue.BridgeId + "</bridgeid>" + m_newLine +
                 "  <remotetarget>" + SafeXML.MakeSafeXML(SIPDialogue.RemoteTarget.ToString()) + "</remotetarget>" + m_newLine +
                 "  <localuserfield>" + SafeXML.MakeSafeXML(SIPDialogue.LocalUserField.ToString()) + "</localuserfield>" + m_newLine +
                 "  <remoteuserfield>" + SafeXML.MakeSafeXML(SIPDialogue.RemoteUserField.ToString()) + "</remoteuserfield>" + m_newLine +
                 "  <routeset>" + SafeXML.MakeSafeXML(RouteSet) + "</routeset>" + m_newLine +
                 "  <proxysipsocket>" + SafeXML.MakeSafeXML(ProxySIPSocket) + "</proxysipsocket>" + m_newLine +
                 "  <cdrid>" + SIPDialogue.CDRId + "</cdrid>" + m_newLine +
                 "  <calldurationlimit>" + SIPDialogue.CallDurationLimit + "</calldurationlimit>" + m_newLine +
                 "  <inserted>" + Inserted.ToString("dd MMM yyyy HH:mm:ss zz") + "</inserted>" + m_newLine +
                 "  <hangupat>" + hanupAtStr + "</hangupat>" + m_newLine +
                 "  <transfermode>" + TransferMode + "</transfermode>" + m_newLine +
                 "  <direction>" + Direction + "</direction>" + m_newLine +
                 "  <sdp>" + SafeXML.MakeSafeXML(SDP) + "</sdp>" + m_newLine +
                 "  <remotesdp>" + SafeXML.MakeSafeXML(RemoteSDP) + "</remotesdp>" + m_newLine +
                 //"  <switchboardcallerdescription>" + SafeXML.MakeSafeXML(SIPDialogue.SwitchboardCallerDescription) + "</switchboardcallerdescription>" + m_newLine +
                 //"  <switchboarddescription>" + SafeXML.MakeSafeXML(SIPDialogue.SwitchboardDescription) + "</switchboarddescription>" + m_newLine +
                 "  <switchboardowner>" + SafeXML.MakeSafeXML(SIPDialogue.SwitchboardOwner) + "</switchboardowner>" + m_newLine +
                 "  <switchboardlinename>" + SafeXML.MakeSafeXML(SIPDialogue.SwitchboardLineName) + "</switchboardlinename>" + m_newLine +
                 "  <crmpersonname>" + SafeXML.MakeSafeXML(SIPDialogue.CRMPersonName) + "</crmpersonname>" + m_newLine +
                 "  <crmcompanyname>" + SafeXML.MakeSafeXML(SIPDialogue.CRMCompanyName) + "</crmcompanyname>" + m_newLine +
                 "  <crmpictureurl>" + SafeXML.MakeSafeXML(SIPDialogue.CRMPictureURL) + "</crmpicutureurl>" + m_newLine;

            return dialogueXML;
        }

        public string GetXMLElementName()
        {
            return XML_ELEMENT_NAME;
        }

        public string GetXMLDocumentElementName()
        {
            return XML_DOCUMENT_ELEMENT_NAME;
        }
    }
}
