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
using System.Xml.Linq;
#endif

namespace SIPSorcery.SIP.App {

    [Table(Name = "cdr")]
    public class SIPCDRAsset : ISIPAsset
    {
        public const string XML_DOCUMENT_ELEMENT_NAME = "sipcdrs";
        public const string XML_ELEMENT_NAME = "sipcdr";

        private static string m_newLine = AppState.NewLine;

        private SIPCDR m_sipCDR;

        public static int TimeZoneOffsetMinutes;

        [Column(Storage = "_id", Name = "id", DbType = "character varying(36)", IsPrimaryKey = true, CanBeNull = false)]
        public string Id {
            get { return m_sipCDR.CDRId.ToString(); }
            set { m_sipCDR.CDRId = new Guid(value); }
        }

        [DataMember]
        [Column(Storage = "_owner", Name = "owner", DbType = "character varying(32)", CanBeNull = true)]
        public string Owner {
            get { return m_sipCDR.Owner; }
            set { m_sipCDR.Owner = value; }
        }

        [Column(Storage = "_adminmemberid", Name = "adminmemberid", DbType = "character varying(32)", CanBeNull = true)]
        [DataMember]
        public string AdminMemberId {
            get { return m_sipCDR.AdminMemberId; }
            set { m_sipCDR.AdminMemberId = value; }
        }

        [Column(Storage = "_inserted", Name = "inserted", DbType = "timestamp", CanBeNull = false)]
        public DateTime InsertedUTC { get; set; }

        [Column(Storage = "_direction", Name = "direction", DbType = "character varying(3)", CanBeNull = false)]
        [DataMember]
        public string CallDirection {
            get { return m_sipCDR.CallDirection.ToString(); }
            set { m_sipCDR.CallDirection = (!value.IsNullOrBlank()) ? (SIPCallDirection)Enum.Parse(typeof(SIPCallDirection), value, true) : SIPCallDirection.None; }
        }

        [Column(Storage = "_created", Name = "created", DbType = "timestamp", CanBeNull = false)]
        [DataMember]
        public DateTime CreatedUTC {
            get { return m_sipCDR.Created.ToUniversalTime(); }
            set { m_sipCDR.Created = value.ToLocalTime(); }
        }

        public DateTime CreatedLocal {
            get { return CreatedUTC.AddMinutes(TimeZoneOffsetMinutes); }
        }

        [Column(Storage = "_dst", Name = "dst", DbType = "character varying(128)", CanBeNull = false)]
        [DataMember]
        public string Dst {
            get { return m_sipCDR.Destination.User; }
            set { } // Set on DstURI.
        }

        [Column(Storage = "_dsthost", Name = "dsthost", DbType = "character varying(128)", CanBeNull = false)]
        [DataMember]
        public string DstHost {
            get { return m_sipCDR.Destination.Host; }
            set { } // Set on DstURI.
        }

        [Column(Storage = "_dsturi", Name = "dsturi", DbType = "character varying(1024)", CanBeNull = true)]
        [DataMember]
        public string DstURI {
            get { return m_sipCDR.Destination.ToString(); }
            set { m_sipCDR.Destination = (!value.IsNullOrBlank()) ? SIPURI.ParseSIPURI(value) : null; }
        }

        [Column(Storage = "_fromuser", Name = "fromuser", DbType = "character varying(128)", CanBeNull = true)]
        [DataMember]
        public string FromUser {
            get { return (m_sipCDR.From != null) ? m_sipCDR.From.FromURI.User : null; }
            set { } // Set on FromHeader.
        }

        [Column(Storage = "_fromname", Name = "fromname", DbType = "character varying(128)", CanBeNull = true)]
        [DataMember]
        public string FromHost {
            get { return (m_sipCDR.From != null) ? m_sipCDR.From.FromName : null; }
            set { } // Set on FromHeader.
        }

        [Column(Storage = "_fromheader", Name = "fromheader", DbType = "character varying(1024)", CanBeNull = true)]
        [DataMember]
        public string FromHeader {
            get { return ( m_sipCDR.From != null) ? m_sipCDR.From.ToString() : null; }
            set { m_sipCDR.From = (!value.IsNullOrBlank()) ? SIPFromHeader.ParseFromHeader(value) : null; }
        }

        [Column(Storage = "_callid", Name = "callid", DbType = "character varying(256)", CanBeNull = false)]
        [DataMember]
        public string CallId {
            get { return m_sipCDR.CallId; }
            set { m_sipCDR.CallId = value; }
        }

        [Column(Storage = "_localsocket", Name = "localsocket", DbType = "character varying(64)", CanBeNull = false)]
        [DataMember]
        public string LocalSocket {
            get { return (m_sipCDR.LocalSIPEndPoint != null) ? m_sipCDR.LocalSIPEndPoint.ToString() : null; }
            set { m_sipCDR.LocalSIPEndPoint = (!value.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(value) : null; }
        }

        [Column(Storage = "_remotesocket", Name = "remotesocket", DbType = "character varying(64)", CanBeNull = false)]
        [DataMember]
        public string RemoteSocket {
            get { return (m_sipCDR.RemoteEndPoint != null) ? m_sipCDR.RemoteEndPoint.ToString() : null; }
            set { m_sipCDR.RemoteEndPoint = (!value.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(value) : null; }
        }

        [Column(Storage = "_bridgeid", Name = "bridgeid", DbType = "character varying(36)", CanBeNull = true)]
        [DataMember]
        public string BridgeId {
            get { return m_sipCDR.BridgeId.ToString(); }
            set { m_sipCDR.BridgeId = (!value.IsNullOrBlank()) ? new Guid(value) : Guid.Empty; }
        }

        [Column(Storage = "_inprogresstime", Name = "inprogresstime", DbType = "timestamp", CanBeNull = true)]
        [DataMember]
        public DateTime? InProgressTimeUTC {
            get {
                if (m_sipCDR.ProgressTime != null) {
                    return m_sipCDR.ProgressTime.Value.ToUniversalTime();
                }
                else {
                    return null;
                }
            }
            set {
                if (value != null) {
                    m_sipCDR.ProgressTime = value.Value.ToLocalTime();
                }
                else {
                    m_sipCDR.ProgressTime = null;
                }
            }
        }

        [Column(Storage = "_inprogressstatus", Name = "inprogressstatus", DbType = "int", CanBeNull = true)]
        [DataMember]
        public int InProgressStatus {
            get { return m_sipCDR.ProgressStatus; }
            set { m_sipCDR.ProgressStatus = value; }
        }

        [Column(Storage = "_inprogressreason", Name = "inprogressreason", DbType = "character varying(64)", CanBeNull = true)]
        [DataMember]
        public string InProgressReason {
            get { return m_sipCDR.ProgressReasonPhrase; }
            set { m_sipCDR.ProgressReasonPhrase = value; }
        }

        [Column(Storage = "_ringduration", Name = "ringduration", DbType = "int", CanBeNull = true)]
        [DataMember]
        public int RingDuration {
            get { return (m_sipCDR.ProgressTime != null && m_sipCDR.AnswerTime != null) ? (int)m_sipCDR.AnswerTime.Value.Subtract(m_sipCDR.ProgressTime.Value).TotalSeconds : 0; }
            set { }
        }

        [Column(Storage = "_answeredtime", Name = "answeredtime", DbType = "timestamp", CanBeNull = true)]
        [DataMember]
        public DateTime? AnsweredTimeUTC {
            get {
                if (m_sipCDR.AnswerTime != null) {
                    return m_sipCDR.AnswerTime.Value.ToUniversalTime();
                }
                else {
                    return null;
                }
            }
            set {
                if (value != null) {
                    m_sipCDR.AnswerTime = value.Value.ToLocalTime();
                }
                else {
                    m_sipCDR.AnswerTime = null;
                }
            }
        }

        public DateTime? AnsweredTimeLocal {
            get {
                if (AnsweredTimeUTC != null) {
                    return AnsweredTimeUTC.Value.AddMinutes(TimeZoneOffsetMinutes);
                }
                else {
                    return null;
                }
            }
        }

        [Column(Storage = "_answeredstatus", Name = "answeredstatus", DbType = "int", CanBeNull = true)]
        [DataMember]
        public int AnsweredStatus {
            get { return m_sipCDR.AnswerStatus; }
            set { m_sipCDR.AnswerStatus = value; }
        }

        [Column(Storage = "_answeredreason", Name = "answeredreason", DbType = "character varying(64)", CanBeNull = true)]
        [DataMember]
        public string AnsweredReason {
            get { return m_sipCDR.AnswerReasonPhrase; }
            set { m_sipCDR.AnswerReasonPhrase = value; }
        }

        [Column(Storage = "_duration", Name = "duration", DbType = "int", CanBeNull = true)]
        [DataMember]
        public int Duration {
            get { return (m_sipCDR.HangupTime != null && m_sipCDR.AnswerTime != null) ? (int)m_sipCDR.HangupTime.Value.Subtract(m_sipCDR.AnswerTime.Value).TotalSeconds : 0; }
            set { }
        }

        [Column(Storage = "_hunguptime", Name = "hunguptime", DbType = "timestamp", CanBeNull = true)]
        [DataMember]
        public DateTime? HungupTimeUTC {
            get {
                if (m_sipCDR.HangupTime != null) {
                    return m_sipCDR.HangupTime.Value.ToUniversalTime();
                }
                else {
                    return null;
                }
            }
            set {
                if (value != null) {
                    m_sipCDR.HangupTime = value.Value.ToLocalTime();
                }
                else {
                    m_sipCDR.HangupTime = null;
                }
            }
        }

        public DateTime? HungupTimeLocal {
            get {
                if (HungupTimeUTC != null) {
                    return HungupTimeUTC.Value.AddMinutes(TimeZoneOffsetMinutes);
                }
                else {
                    return null;
                }
            }
        }

        [Column(Storage = "_hungupreason", Name = "hungupreason", DbType = "character varying(64)", CanBeNull = true)]
        [DataMember]
        public string HungupReason {
            get { return m_sipCDR.HangupReason; }
            set { m_sipCDR.HangupReason = value; }
        }

        public SIPCDRAsset() {
            InsertedUTC = DateTime.Now.ToUniversalTime();
            m_sipCDR = new SIPCDR();
        }

        public SIPCDRAsset(SIPCDR sipCDR) {
            InsertedUTC = DateTime.Now.ToUniversalTime();
            m_sipCDR = sipCDR;
        }

#if !SILVERLIGHT

        public SIPCDRAsset(DataRow cdrRow) {
            Load(cdrRow);
        }

        public void Load(DataRow cdrRow) {
            m_sipCDR = new SIPCDR();
            m_sipCDR.CDRId = new Guid(cdrRow["id"] as string);
            m_sipCDR.Owner = cdrRow["owner"] as string;
            m_sipCDR.AdminMemberId = cdrRow["adminmemberid"] as string;
            InsertedUTC = Convert.ToDateTime(cdrRow["inserted"]);
            m_sipCDR.CallDirection = (cdrRow["direction"] as string == SIPCallDirection.In.ToString()) ? SIPCallDirection.In : SIPCallDirection.Out;
            m_sipCDR.Created = Convert.ToDateTime(cdrRow["created"]).ToLocalTime();
            m_sipCDR.Destination = SIPURI.ParseSIPURI(cdrRow["dsturi"] as string);
            m_sipCDR.From = SIPFromHeader.ParseFromHeader(cdrRow["from"] as string);
            m_sipCDR.CallId = cdrRow["callid"] as string;
            m_sipCDR.LocalSIPEndPoint = (!(cdrRow["localsocket"] as string).IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(cdrRow["localsocket"] as string) : null;
            m_sipCDR.RemoteEndPoint = (!(cdrRow["remotesocket"] as string).IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(cdrRow["remotesocket"] as string) : null;
            m_sipCDR.BridgeId = (!(cdrRow["bridgeid"] as string).IsNullOrBlank()) ? new Guid(cdrRow["bridgeid"] as string) : Guid.Empty;
            if (!(cdrRow["inprogresstime"] as string).IsNullOrBlank()) {
                m_sipCDR.ProgressTime = Convert.ToDateTime(cdrRow["inprogresstime"]).ToLocalTime();
            }
            else {
                m_sipCDR.ProgressTime = null;
            }
            m_sipCDR.ProgressStatus = (cdrRow["inprogressstatus"] != null) ? Convert.ToInt32(cdrRow["inprogressstatus"]) : 0;
            m_sipCDR.ProgressReasonPhrase = cdrRow["inprogressreason"] as string;
            if (!(cdrRow["answeredtime"] as string).IsNullOrBlank()) {
                m_sipCDR.AnswerTime = Convert.ToDateTime(cdrRow["answeredtime"]).ToLocalTime();
            }
            else {
                m_sipCDR.AnswerTime = null;
            }
            m_sipCDR.AnswerStatus = (cdrRow["answeredstatus"] != null) ? Convert.ToInt32(cdrRow["answeredstatus"]) : 0;
            m_sipCDR.AnswerReasonPhrase = cdrRow["answeredreason"] as string;
            if (!(cdrRow["hunguptime"] as string).IsNullOrBlank()) {
                m_sipCDR.HangupTime = Convert.ToDateTime(cdrRow["hunguptime"]).ToLocalTime();
            }
            else {
                m_sipCDR.HangupTime = null;
            }
            m_sipCDR.HangupReason = cdrRow["hungupreason"] as string;

            m_sipCDR.InProgress = (m_sipCDR.ProgressTime != null);
            m_sipCDR.IsAnswered = (m_sipCDR.AnswerTime != null);
            m_sipCDR.IsHungup = (m_sipCDR.HangupTime != null);
        }

        public Dictionary<Guid, object> Load(XmlDocument dom) {
            return SIPAssetXMLPersistor<SIPCDRAsset>.LoadAssetsFromXMLRecordSet(dom);
        }

#endif

        public string ToXML() {
            string cdrXML =
                " <" + XML_ELEMENT_NAME + ">" + m_newLine +
                ToXMLNoParent() +
                " </" + XML_ELEMENT_NAME + ">" + m_newLine;

            return cdrXML;
        }

        public string ToXMLNoParent() {
            string localSocketStr = (m_sipCDR.LocalSIPEndPoint != null) ? m_sipCDR.LocalSIPEndPoint.ToString() : null;
            string remoteSocketStr = (m_sipCDR.RemoteEndPoint != null) ? m_sipCDR.RemoteEndPoint.ToString() : null;
            string progressTimeStr = (m_sipCDR.ProgressTime != null) ? m_sipCDR.ProgressTime.Value.ToUniversalTime().ToString("o") : null;
            string answerTimeStr = (m_sipCDR.AnswerTime != null) ? m_sipCDR.AnswerTime.Value.ToUniversalTime().ToString("o") : null;
            string hangupTimeStr = (m_sipCDR.HangupTime != null) ? m_sipCDR.HangupTime.Value.ToUniversalTime().ToString("o") : null;

            string cdrXML =
                "  <id>" + m_sipCDR.CDRId.ToString() + "</id>" + m_newLine +
                "  <owner>" + m_sipCDR.Owner + "</owner>" + m_newLine +
                "  <adminmemberid>" + m_sipCDR.Owner + "</adminmemberid>" + m_newLine +
                "  <direction>" + m_sipCDR.CallDirection + "</direction>" + m_newLine +
                "  <inserted>" + InsertedUTC.ToString("o") + "</inserted>" + m_newLine +
                "  <created>" + m_sipCDR.Created.ToString("dd MMM yyyy HH:mm:ss") + "</created>" + m_newLine +
                "  <dsturi>" + SafeXML.MakeSafeXML(m_sipCDR.Destination.ToString()) + "</dsturi>" + m_newLine +
                "  <from>" + SafeXML.MakeSafeXML(m_sipCDR.From.ToString()) + "</from>" + m_newLine +
                "  <callid>" + m_sipCDR.CallId + "</callid>" + m_newLine +
                "  <localsocket>" + localSocketStr + "</localsocket>" + m_newLine +
                "  <remotesocket>" + remoteSocketStr + "</remotesocket>" + m_newLine +
                "  <bridgeid>" + m_sipCDR.BridgeId.ToString() + "</bridgeid>" + m_newLine +
                "  <inprogresstime>" + progressTimeStr + "</inprogresstime>" + m_newLine +
                "  <inprogressstatus>" + m_sipCDR.ProgressStatus + "</inprogressstatus>" + m_newLine +
                "  <inprogressreason>" + m_sipCDR.ProgressReasonPhrase + "</inprogressreason>" + m_newLine +
                "  <answeredtime>" + answerTimeStr + "</answeredtime>" + m_newLine +
                "  <answeredstatus>" + m_sipCDR.AnswerStatus + "</answeredstatus>" + m_newLine +
                "  <answeredreason>" + m_sipCDR.AnswerReasonPhrase + "</answeredreason>" + m_newLine +
                "  <hunguptime>" + hangupTimeStr + "</hunguptime>" + m_newLine +
                "  <hungupreason>" + m_sipCDR.HangupReason + "</hungupreason>" + m_newLine;

            return cdrXML;
        }

        public string GetXMLElementName() {
            return XML_ELEMENT_NAME;
        }

        public string GetXMLDocumentElementName() {
            return XML_DOCUMENT_ELEMENT_NAME;
        }

        public void Hungup(string hangupReason)
        {
            m_sipCDR.Hungup(hangupReason);
        }
    }
}
