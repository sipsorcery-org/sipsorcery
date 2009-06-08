using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using SIPSorcery.Sys;

#if !SILVERLIGHT
using System.Data;
using System.Data.Linq;
using System.Data.Linq.Mapping;
#endif

namespace SIPSorcery.SIP.App {

    [Table(Name = "sipdialogues")]
    public class SIPDialogueAsset : ISIPAsset
    {
        public const string XML_DOCUMENT_ELEMENT_NAME = "sipdialogues";
        public const string XML_ELEMENT_NAME = "sipdialogue";

        private static string m_newLine = AppState.NewLine;

        [IgnoreDataMember]
        public SIPDialogue SIPDialogue;

        [Column(Storage = "_id", Name = "id", DbType = "character varying(36)", IsPrimaryKey = true, CanBeNull = false)]
        public string Id {
            get { return SIPDialogue.Id.ToString(); }
            set { SIPDialogue.Id = new Guid(value); }
        }

        [Column(Storage = "_owner", Name = "owner", DbType = "character varying(32)", CanBeNull = false)]
        public string Owner {
            get { return SIPDialogue.Owner; }
            set { SIPDialogue.Owner = value; }
        }

        [Column(Storage = "_adminmemberid", Name = "adminmemberid", DbType = "character varying(32)", CanBeNull = true)]
        public string AdminMemberId {
            get { return SIPDialogue.AdminMemberId; }
            set { SIPDialogue.AdminMemberId = value; }
        }

        [Column(Storage = "_dialogueid", Name = "dialogueid", DbType = "character varying(256)", IsPrimaryKey = false, CanBeNull = false)]
        public string DialogueId {
            get { return SIPDialogue.DialogueId; }
            set { SIPDialogue.DialogueId = value; }
        }

        [Column(Storage = "_localtag", Name = "localtag", DbType = "character varying(64)", IsPrimaryKey = false, CanBeNull = false)]
        public string LocalTag {
            get { return SIPDialogue.LocalTag; }
            set { SIPDialogue.LocalTag = value; }
        }

        [Column(Storage = "_remotetag", Name = "remotetag", DbType = "character varying(64)", IsPrimaryKey = false, CanBeNull = false)]
        public string RemoteTag {
            get { return SIPDialogue.RemoteTag; }
            set { SIPDialogue.RemoteTag = value; }
        }

        [Column(Storage = "_callid", Name = "callid", DbType = "character varying(128)", IsPrimaryKey = false, CanBeNull = false)]
        public string CallId {
            get { return SIPDialogue.CallId; }
            set { SIPDialogue.CallId = value; }
        }

        [Column(Storage = "_cseq", Name = "cseq", DbType = "integer", IsPrimaryKey = false, CanBeNull = false)]
        public int CSeq {
            get { return SIPDialogue.CSeq; }
            set { SIPDialogue.CSeq = value; }
        }

        [Column(Storage = "_bridgeid", Name = "bridgeid", DbType = "character varying(36)", IsPrimaryKey = false, CanBeNull = false)]
        public string BridgeId {
            get { return SIPDialogue.BridgeId.ToString(); }
            set { SIPDialogue.BridgeId = (!value.IsNullOrBlank()) ? new Guid(value) : Guid.Empty; }
        }

        [Column(Storage = "_remotetarget", Name = "remotetarget", DbType = "character varying(256)", IsPrimaryKey = false, CanBeNull = false)]
        public string RemoteTarget {
            get { return SIPDialogue.RemoteTarget.ToString(); }
            set { SIPDialogue.RemoteTarget = (!value.IsNullOrBlank()) ? SIPURI.ParseSIPURI(value) : null; }
        }

        [IgnoreDataMember]
        [Column(Storage = "_localuserfield", Name = "localuserfield", DbType = "character varying(512)", IsPrimaryKey = false, CanBeNull = false)]
        public string LocalUserField {
            get { return SIPDialogue.LocalUserField.ToString(); }
            set { SIPDialogue.LocalUserField = (!value.IsNullOrBlank()) ? SIPUserField.ParseSIPUserField(value) : null; }
        }

        [DataMember]
        [Column(Storage = "_remoteuserfield", Name = "remoteuserfield", DbType = "character varying(512)", IsPrimaryKey = false, CanBeNull = false)]
        public string RemoteUserField {
            get { return SIPDialogue.RemoteUserField.ToString(); }
            set { SIPDialogue.RemoteUserField = (!value.IsNullOrBlank()) ? SIPUserField.ParseSIPUserField(value) : null; }
        }

        [IgnoreDataMember]
        [Column(Storage = "_routeset", Name = "routeset", DbType = "character varying(512)", IsPrimaryKey = false, CanBeNull = true)]
        public string RouteSet {
            get { return (SIPDialogue.RouteSet != null) ? SIPDialogue.RouteSet.ToString() : null; }
            set { SIPDialogue.RouteSet = (!value.IsNullOrBlank()) ? SIPRouteSet.ParseSIPRouteSet(value) : null; }
        }

        [Column(Storage = "_outboundproxy", Name = "outboundproxy", DbType = "character varying(128)", IsPrimaryKey = false, CanBeNull = true)]
        public string OutboundProxy {
            get { return (SIPDialogue.OutboundProxy != null) ? SIPDialogue.OutboundProxy.ToString() : null; }
            set { SIPDialogue.OutboundProxy = (!value.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(value) : null; }
        }

        [Column(Storage = "_cdrid", Name = "cdrid", DbType = "character varying(36)", IsPrimaryKey = false, CanBeNull = false)]
        public string CDRId {
            get { return SIPDialogue.CDRId.ToString(); }
            set { SIPDialogue.CDRId = (!value.IsNullOrBlank()) ? new Guid(value) : Guid.Empty; }
        }

        [Column(Storage = "_calldurationlimit", Name = "calldurationlimit", DbType = "integer", IsPrimaryKey = false, CanBeNull = false)]
        public int CallDurationLimit
        {
            get { return SIPDialogue.CallDurationLimit; }
            set { SIPDialogue.CallDurationLimit = value; }
        }

        public object OrderProperty
        {
            get { return Id; }
            set { }
        }

        public SIPDialogueAsset() {
            SIPDialogue = new SIPDialogue();
        }

        public SIPDialogueAsset(SIPDialogue sipDialogue) {
            SIPDialogue = sipDialogue;
        }

#if !SILVERLIGHT

        public SIPDialogueAsset(DataRow row) {
            Load(row);
        }

        public void Load(DataRow row) {
            SIPDialogue = new SIPDialogue();
            SIPDialogue.Id = new Guid(row["id"] as string);
            SIPDialogue.Owner = row["owner"] as string;
            SIPDialogue.AdminMemberId = row["adminmemberid"] as string;
            SIPDialogue.DialogueId = row["dialogueid"] as string;
            SIPDialogue.LocalTag = row["localtag"] as string;
            SIPDialogue.RemoteTag = row["remotetag"] as string;
            SIPDialogue.LocalUserField = SIPUserField.ParseSIPUserField(row["localuserfield"] as string);
            SIPDialogue.RemoteUserField = SIPUserField.ParseSIPUserField(row["remoteuserfield"] as string);
            SIPDialogue.CallId = row["callid"] as string;
            SIPDialogue.CSeq = Convert.ToInt32(row["cseq"]);
            SIPDialogue.BridgeId = new Guid(row["bridgeid"] as string);
            SIPDialogue.RemoteTarget = SIPURI.ParseSIPURI(row["remotetarget"] as string);
            SIPDialogue.RouteSet = (row["routeset"] != null && row["routeset"] != DBNull.Value && !(row["routeset"] as string).IsNullOrBlank()) ? SIPRouteSet.ParseSIPRouteSet(row["routeset"] as string) : null;
            SIPDialogue.OutboundProxy = (row["outboundproxy"] != null && row["outboundproxy"] != DBNull.Value && !(row["outboundproxy"] as string).IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(row["outboundproxy"] as string) : null;
            SIPDialogue.CDRId = new Guid(row["cdrid"] as string);
            SIPDialogue.CallDurationLimit = (row["calldurationlimit"] != null && row["calldurationlimit"] != DBNull.Value) ? Convert.ToInt32(row["calldurationlimit"]) : 0;
        }

        public Dictionary<Guid, object> Load(XmlDocument dom) {
            return SIPAssetXMLPersistor<SIPDialogueAsset>.LoadAssetsFromXMLRecordSet(dom);
        }

#endif

        public object GetOrderProperty()
        {
            return Id;
        }

        public string ToXML() {
            string dialogueXML =
                " <" + XML_ELEMENT_NAME + ">" + m_newLine +
                ToXMLNoParent() +
                " </" + XML_ELEMENT_NAME + ">" + m_newLine;

            return dialogueXML;
        }

        public string ToXMLNoParent() {
            string dialogueXML =
                 "  <id>" + SIPDialogue.Id + "</id>" + m_newLine +
                 "  <owner>" + SIPDialogue.Owner + "</owner>" + m_newLine +
                 "  <adminmemberid>" + SIPDialogue.AdminMemberId + "</adminmemberid>" + m_newLine +
                 "  <dialogueid>" + SIPDialogue.DialogueId + "</dialogueid>" + m_newLine +
                 "  <localtag>" + SIPDialogue.LocalTag + "</localtag>" + m_newLine +
                 "  <remotetag>" + SIPDialogue.RemoteTag + "</remotetag>" + m_newLine +
                 "  <callid>" + SIPDialogue.CallId + "</callid>" + m_newLine +
                 "  <cseq>" + SIPDialogue.CSeq + "</cseq>" + m_newLine +
                 "  <bridgeid>" + SIPDialogue.BridgeId + "</bridgeid>" + m_newLine +
                 "  <remotetarget>" + SafeXML.MakeSafeXML(SIPDialogue.RemoteTarget.ToString()) + "</remotetarget>" + m_newLine +
                 "  <localuserfield>" + SafeXML.MakeSafeXML(SIPDialogue.LocalUserField.ToString()) + "</localuserfield>" + m_newLine +
                 "  <remoteuserfield>" + SafeXML.MakeSafeXML(SIPDialogue.RemoteUserField.ToString()) + "</remoteuserfield>" + m_newLine +
                 "  <outboundproxy>" + OutboundProxy + "</outboundproxy>" + m_newLine +
                 "  <routeset>" + SafeXML.MakeSafeXML(RouteSet) + "</routeset>" + m_newLine +
                 "  <cdrid>" + SIPDialogue.CDRId + "</cdrid>" + m_newLine +
                 "  <calldurationlimit>" + SIPDialogue.CallDurationLimit + "</calldurationlimit>" + m_newLine;

            return dialogueXML;
        }

        public string GetXMLElementName() {
            return XML_ELEMENT_NAME;
        }

        public string GetXMLDocumentElementName() {
            return XML_DOCUMENT_ELEMENT_NAME;
        }
    }
}
