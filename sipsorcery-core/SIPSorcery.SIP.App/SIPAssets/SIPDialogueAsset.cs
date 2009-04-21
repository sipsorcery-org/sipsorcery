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

        public SIPDialogue SIPDialogue;

        [Column(Storage = "_id", Name = "id", DbType = "character varying(36)", IsPrimaryKey = true, CanBeNull = false)]
        public string Id {
            get { return SIPDialogue.Id.ToString(); }
            set { SIPDialogue.Id = new Guid(value); }
        }

        [Column(Storage = "_owner", Name = "owner", DbType = "character varying(32)", CanBeNull = false)]
        [DataMember]
        public string Owner {
            get { return SIPDialogue.Owner; }
            set { SIPDialogue.Owner = value; }
        }

        [Column(Storage = "_adminmemberid", Name = "adminmemberid", DbType = "character varying(32)", CanBeNull = true)]
        [DataMember]
        public string AdminMemberId {
            get { return SIPDialogue.AdminMemberId; }
            set { SIPDialogue.AdminMemberId = value; }
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
        }

        public Dictionary<Guid, object> Load(XmlDocument dom) {
            return SIPAssetXMLPersistor<SIPDialogueAsset>.LoadAssetsFromXMLRecordSet(dom);
        }

#endif

        public string ToXML() {
            string dialogueXML =
                " <" + XML_ELEMENT_NAME + ">" + m_newLine +
                ToXMLNoParent() +
                " </" + XML_ELEMENT_NAME + ">" + m_newLine;

            return dialogueXML;
        }

        public string ToXMLNoParent() {
            string dialogueXML =
                  " <id>" + SIPDialogue.Id + "</id>" + m_newLine;

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
