using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using SIPSorcery.Sys;
using SIPSorcery.Persistence;

#if !SILVERLIGHT
using System.Data;
using System.Data.Linq;
using System.Data.Linq.Mapping;
using System.Xml.Linq;
#endif

namespace SIPSorcery.SIP.App
{

    [Table(Name = "cdr")]
    public class SIPCDRAsset : ISIPAsset
    {
        public const string XML_DOCUMENT_ELEMENT_NAME = "sipcdrs";
        public const string XML_ELEMENT_NAME = "sipcdr";

        private static string m_newLine = AppState.NewLine;

        private SIPCDR m_sipCDR;

        public static int TimeZoneOffsetMinutes;

        [Column(Name = "id", DbType = "varchar(36)", IsPrimaryKey = true, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public Guid Id
        {
            get { return m_sipCDR.CDRId; }
            set { m_sipCDR.CDRId = value; }
        }

        [DataMember]
        [Column(Name = "owner", DbType = "varchar(32)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public string Owner
        {
            get { return m_sipCDR.Owner; }
            set { m_sipCDR.Owner = value; }
        }

        [Column(Name = "adminmemberid", DbType = "varchar(32)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string AdminMemberId
        {
            get { return m_sipCDR.AdminMemberId; }
            set { m_sipCDR.AdminMemberId = value; }
        }

        private DateTimeOffset m_inserted;
        [Column(Name = "inserted", DbType = "datetimeoffset", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public DateTimeOffset Inserted
        {
            get { return m_inserted.ToUniversalTime(); }
            set { m_inserted = value.ToUniversalTime(); }
        }

        [Column(Name = "direction", DbType = "varchar(3)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string CallDirection
        {
            get { return m_sipCDR.CallDirection.ToString(); }
            set { m_sipCDR.CallDirection = (!value.IsNullOrBlank()) ? (SIPCallDirection)Enum.Parse(typeof(SIPCallDirection), value, true) : SIPCallDirection.None; }
        }

        [Column(Name = "created", DbType = "datetimeoffset", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public DateTimeOffset Created
        {
            get { return m_sipCDR.Created.ToUniversalTime(); }
            set { m_sipCDR.Created = value.ToUniversalTime(); }
        }

        public DateTimeOffset CreatedLocal
        {
            get { return Created.AddMinutes(TimeZoneOffsetMinutes); }
        }

        [Column(Name = "dst", DbType = "varchar(128)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string Dst
        {
            get { return m_sipCDR.Destination.User; }
            set { } // Set on DstURI.
        }

        [Column(Name = "dsthost", DbType = "varchar(128)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string DstHost
        {
            get { return m_sipCDR.Destination.Host; }
            set { } // Set on DstURI.
        }

        [Column(Name = "dsturi", DbType = "varchar(1024)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string DstURI
        {
            get { return m_sipCDR.Destination.ToString(); }
            set { m_sipCDR.Destination = (!value.IsNullOrBlank()) ? SIPURI.ParseSIPURI(value) : null; }
        }

        [Column(Name = "fromuser", DbType = "varchar(128)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string FromUser
        {
            get { return (m_sipCDR.From != null) ? m_sipCDR.From.FromURI.User : null; }
            set { } // Set on FromHeader.
        }

        [Column(Name = "fromname", DbType = "varchar(128)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string FromName
        {
            get { return (m_sipCDR.From != null) ? m_sipCDR.From.FromName : null; }
            set { } // Set on FromHeader.
        }

        [Column(Name = "fromheader", DbType = "varchar(1024)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string FromHeader
        {
            get { return (m_sipCDR.From != null) ? m_sipCDR.From.ToString() : null; }
            set { m_sipCDR.From = (!value.IsNullOrBlank()) ? SIPFromHeader.ParseFromHeader(value) : null; }
        }

        [Column(Name = "callid", DbType = "varchar(256)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string CallId
        {
            get { return m_sipCDR.CallId; }
            set { m_sipCDR.CallId = value; }
        }

        [Column(Name = "localsocket", DbType = "varchar(64)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string LocalSocket
        {
            get { return (m_sipCDR.LocalSIPEndPoint != null) ? m_sipCDR.LocalSIPEndPoint.ToString() : null; }
            set { m_sipCDR.LocalSIPEndPoint = (!value.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(value) : null; }
        }

        [Column(Name = "remotesocket", DbType = "varchar(64)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string RemoteSocket
        {
            get { return (m_sipCDR.RemoteEndPoint != null) ? m_sipCDR.RemoteEndPoint.ToString() : null; }
            set { m_sipCDR.RemoteEndPoint = (!value.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(value) : null; }
        }

        [Column(Name = "bridgeid", DbType = "varchar(36)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string BridgeId
        {
            get { return m_sipCDR.BridgeId.ToString(); }
            set { m_sipCDR.BridgeId = (!value.IsNullOrBlank()) ? new Guid(value) : Guid.Empty; }
        }

        [Column(Name = "inprogresstime", DbType = "datetimeoffset", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public DateTimeOffset? InProgressTime
        {
            get
            {
                if (m_sipCDR.ProgressTime != null)
                {
                    return m_sipCDR.ProgressTime.Value.ToUniversalTime();
                }
                else
                {
                    return null;
                }
            }
            set
            {
                if (value != null)
                {
                    m_sipCDR.ProgressTime = value.Value.ToUniversalTime();
                }
                else
                {
                    m_sipCDR.ProgressTime = null;
                }
            }
        }

        [Column(Name = "inprogressstatus", DbType = "int", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public int InProgressStatus
        {
            get { return m_sipCDR.ProgressStatus; }
            set { m_sipCDR.ProgressStatus = value; }
        }

        [Column(Name = "inprogressreason", DbType = "varchar(512)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string InProgressReason
        {
            get { return m_sipCDR.ProgressReasonPhrase; }
            set { m_sipCDR.ProgressReasonPhrase = value; }
        }

        [Column(Name = "ringduration", DbType = "int", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public int RingDuration
        {
            get { return m_sipCDR.GetProgressDuration(); }
            set { }
        }

        [Column(Name = "answeredtime", DbType = "datetimeoffset", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public DateTimeOffset? AnsweredTime
        {
            get
            {
                if (m_sipCDR.AnswerTime != null)
                {
                    return m_sipCDR.AnswerTime.Value.ToUniversalTime();
                }
                else
                {
                    return null;
                }
            }
            set
            {
                if (value != null)
                {
                    m_sipCDR.AnswerTime = value.Value.ToUniversalTime();
                }
                else
                {
                    m_sipCDR.AnswerTime = null;
                }
            }
        }

        public DateTimeOffset? AnsweredTimeLocal
        {
            get
            {
                if (AnsweredTime != null)
                {
                    return AnsweredTime.Value.AddMinutes(TimeZoneOffsetMinutes);
                }
                else
                {
                    return null;
                }
            }
        }

        [Column(Name = "answeredstatus", DbType = "int", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public int AnsweredStatus
        {
            get { return m_sipCDR.AnswerStatus; }
            set { m_sipCDR.AnswerStatus = value; }
        }

        [Column(Name = "answeredreason", DbType = "varchar(512)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string AnsweredReason
        {
            get { return m_sipCDR.AnswerReasonPhrase; }
            set { m_sipCDR.AnswerReasonPhrase = value; }
        }

        [Column(Name = "duration", DbType = "int", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public int Duration
        {
            get { return m_sipCDR.GetAnsweredDuration(); }
            set { }
        }

        [Column(Name = "hunguptime", DbType = "datetimeoffset", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public DateTimeOffset? HungupTime
        {
            get
            {
                if (m_sipCDR.HangupTime != null)
                {
                    return m_sipCDR.HangupTime.Value.ToUniversalTime();
                }
                else
                {
                    return null;
                }
            }
            set
            {
                if (value != null)
                {
                    m_sipCDR.HangupTime = value.Value.ToUniversalTime();
                }
                else
                {
                    m_sipCDR.HangupTime = null;
                }
            }
        }

        public DateTimeOffset? HungupTimeLocal
        {
            get
            {
                if (HungupTime != null)
                {
                    return HungupTime.Value.AddMinutes(TimeZoneOffsetMinutes);
                }
                else
                {
                    return null;
                }
            }
        }

        [Column(Name = "hungupreason", DbType = "varchar(512)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string HungupReason
        {
            get { return m_sipCDR.HangupReason; }
            set { m_sipCDR.HangupReason = value; }
        }

        [Column(Name = "answeredat", DbType = "datetime", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public DateTime? AnsweredAt
        {
            get { return m_sipCDR.AnsweredAt; }
            set { m_sipCDR.AnsweredAt = value; }
        }

        [Column(Name = "dialplancontextid", DbType = "varchar(36)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string DialPlanContextId
        {
            get { return m_sipCDR.DialPlanContextID.ToString(); }
            set { m_sipCDR.DialPlanContextID = (!value.IsNullOrBlank()) ? new Guid(value) : Guid.Empty; }
        }

        public SIPCDRAsset()
        {
            Inserted = DateTimeOffset.UtcNow;
            m_sipCDR = new SIPCDR();
        }

        public SIPCDRAsset(SIPCDR sipCDR)
        {
            Inserted = DateTimeOffset.UtcNow;
            m_sipCDR = sipCDR;
        }

#if !SILVERLIGHT

        public SIPCDRAsset(DataRow cdrRow)
        {
            Load(cdrRow);
        }

        public DataTable GetTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add(new DataColumn("id", typeof(String)));
            table.Columns.Add(new DataColumn("owner", typeof(String)));
            table.Columns.Add(new DataColumn("adminmemberid", typeof(String)));
            table.Columns.Add(new DataColumn("inserted", typeof(DateTimeOffset)));
            table.Columns.Add(new DataColumn("direction", typeof(String)));
            table.Columns.Add(new DataColumn("created", typeof(DateTimeOffset)));
            table.Columns.Add(new DataColumn("dst", typeof(String)));
            table.Columns.Add(new DataColumn("dsturi", typeof(String)));
            table.Columns.Add(new DataColumn("dsthost", typeof(String)));
            table.Columns.Add(new DataColumn("fromuser", typeof(String)));
            table.Columns.Add(new DataColumn("fromname", typeof(String)));
            table.Columns.Add(new DataColumn("fromheader", typeof(String)));
            table.Columns.Add(new DataColumn("callid", typeof(String)));
            table.Columns.Add(new DataColumn("localsocket", typeof(String)));
            table.Columns.Add(new DataColumn("remotesocket", typeof(String)));
            table.Columns.Add(new DataColumn("bridgeid", typeof(String)));
            table.Columns.Add(new DataColumn("inprogresstime", typeof(DateTimeOffset)));
            table.Columns.Add(new DataColumn("inprogressreason", typeof(String)));
            table.Columns.Add(new DataColumn("inprogressstatus", typeof(Int32)));
            table.Columns.Add(new DataColumn("answeredtime", typeof(DateTimeOffset)));
            table.Columns.Add(new DataColumn("answeredreason", typeof(String)));
            table.Columns.Add(new DataColumn("answeredstatus", typeof(Int32)));
            table.Columns.Add(new DataColumn("hunguptime", typeof(DateTimeOffset)));
            table.Columns.Add(new DataColumn("hungupreason", typeof(String)));
            table.Columns.Add(new DataColumn("ringduration", typeof(Int32)));
            table.Columns.Add(new DataColumn("duration", typeof(Int32)));
            table.Columns.Add(new DataColumn("answeredat", typeof(DateTime)));
            table.Columns.Add(new DataColumn("dialplancontextid", typeof(String)));
            return table;
        }

        public void Load(DataRow cdrRow)
        {
            m_sipCDR = new SIPCDR();
            m_sipCDR.CDRId = new Guid(cdrRow["id"] as string);
            m_sipCDR.Owner = cdrRow["owner"] as string;
            m_sipCDR.AdminMemberId = cdrRow["adminmemberid"] as string;
            Inserted = DateTimeOffset.Parse(cdrRow["inserted"] as string);
            m_sipCDR.CallDirection = (cdrRow["direction"] as string == SIPCallDirection.In.ToString()) ? SIPCallDirection.In : SIPCallDirection.Out;
            Created = DateTimeOffset.Parse(cdrRow["created"] as string);
            m_sipCDR.Destination = SIPURI.ParseSIPURI(cdrRow["dsturi"] as string);
            m_sipCDR.From = SIPFromHeader.ParseFromHeader(cdrRow["fromheader"] as string);
            m_sipCDR.CallId = cdrRow["callid"] as string;
            m_sipCDR.LocalSIPEndPoint = (!(cdrRow["localsocket"] as string).IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(cdrRow["localsocket"] as string) : null;
            m_sipCDR.RemoteEndPoint = (!(cdrRow["remotesocket"] as string).IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(cdrRow["remotesocket"] as string) : null;
            m_sipCDR.BridgeId = (!(cdrRow["bridgeid"] as string).IsNullOrBlank()) ? new Guid(cdrRow["bridgeid"] as string) : Guid.Empty;
            if (cdrRow["inprogresstime"] != DBNull.Value && cdrRow["inprogresstime"] != null && !(cdrRow["inprogresstime"] as string).IsNullOrBlank())
            {
                InProgressTime = DateTimeOffset.Parse(cdrRow["inprogresstime"] as string);
            }
            else
            {
                InProgressTime = null;
            }
            m_sipCDR.ProgressStatus = (cdrRow["inprogressstatus"] != null && cdrRow["inprogressstatus"] != DBNull.Value) ? Convert.ToInt32(cdrRow["inprogressstatus"]) : 0;
            m_sipCDR.ProgressReasonPhrase = cdrRow["inprogressreason"] as string;
            if (cdrRow["answeredtime"] != DBNull.Value && cdrRow["answeredtime"] != null && !(cdrRow["answeredtime"] as string).IsNullOrBlank())
            {
                AnsweredTime = DateTimeOffset.Parse(cdrRow["answeredtime"] as string);
            }
            else
            {
                AnsweredTime = null;
            }
            m_sipCDR.AnswerStatus = (cdrRow["answeredstatus"] != DBNull.Value && cdrRow["answeredstatus"] != null) ? Convert.ToInt32(cdrRow["answeredstatus"]) : 0;
            m_sipCDR.AnswerReasonPhrase = cdrRow["answeredreason"] as string;
            if (cdrRow["hunguptime"] != DBNull.Value && cdrRow["hunguptime"] != null && !(cdrRow["hunguptime"] as string).IsNullOrBlank())
            {
                HungupTime = DateTimeOffset.Parse(cdrRow["hunguptime"] as string);
            }
            else
            {
                HungupTime = null;
            }
            m_sipCDR.HangupReason = cdrRow["hungupreason"] as string;
            m_sipCDR.InProgress = (m_sipCDR.ProgressTime != null);
            m_sipCDR.IsAnswered = (m_sipCDR.AnswerTime != null);
            m_sipCDR.IsHungup = (m_sipCDR.HangupTime != null);
            if (cdrRow["answeredat"] != DBNull.Value && cdrRow["answeredat"] != null)
            {
                AnsweredAt = (DateTime)cdrRow["answeredat"];
            }
            m_sipCDR.DialPlanContextID = (!(cdrRow["dialplancontextid"] as string).IsNullOrBlank()) ? new Guid(cdrRow["dialplancontextid"] as string) : Guid.Empty;
        }

        public Dictionary<Guid, object> Load(XmlDocument dom)
        {
            return SIPAssetXMLPersistor<SIPCDRAsset>.LoadAssetsFromXMLRecordSet(dom);
        }

#endif

        public string ToXML()
        {
            string cdrXML =
                " <" + XML_ELEMENT_NAME + ">" + m_newLine +
                ToXMLNoParent() +
                " </" + XML_ELEMENT_NAME + ">" + m_newLine;

            return cdrXML;
        }

        public string ToXMLNoParent()
        {
            string localSocketStr = (m_sipCDR.LocalSIPEndPoint != null) ? m_sipCDR.LocalSIPEndPoint.ToString() : null;
            string remoteSocketStr = (m_sipCDR.RemoteEndPoint != null) ? m_sipCDR.RemoteEndPoint.ToString() : null;
            string progressTimeStr = (m_sipCDR.ProgressTime != null) ? m_sipCDR.ProgressTime.Value.ToString("o") : null;
            string answerTimeStr = (m_sipCDR.AnswerTime != null) ? m_sipCDR.AnswerTime.Value.ToString("o") : null;
            string hangupTimeStr = (m_sipCDR.HangupTime != null) ? m_sipCDR.HangupTime.Value.ToString("o") : null;

            string cdrXML =
                "  <id>" + m_sipCDR.CDRId.ToString() + "</id>" + m_newLine +
                "  <owner>" + m_sipCDR.Owner + "</owner>" + m_newLine +
                "  <adminmemberid>" + m_sipCDR.AdminMemberId + "</adminmemberid>" + m_newLine +
                "  <direction>" + m_sipCDR.CallDirection + "</direction>" + m_newLine +
                "  <inserted>" + Inserted.ToString("o") + "</inserted>" + m_newLine +
                "  <created>" + m_sipCDR.Created.ToString("o") + "</created>" + m_newLine +
                "  <dsturi>" + SafeXML.MakeSafeXML(m_sipCDR.Destination.ToString()) + "</dsturi>" + m_newLine +
                "  <fromheader>" + SafeXML.MakeSafeXML(m_sipCDR.From.ToString()) + "</fromheader>" + m_newLine +
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
                "  <hungupreason>" + m_sipCDR.HangupReason + "</hungupreason>" + m_newLine +
                "  <bridgeid>" + m_sipCDR.DialPlanContextID.ToString() + "</bridgeid>" + m_newLine;

            return cdrXML;
        }

        public string GetXMLElementName()
        {
            return XML_ELEMENT_NAME;
        }

        public string GetXMLDocumentElementName()
        {
            return XML_DOCUMENT_ELEMENT_NAME;
        }

        public void Hungup(string hangupReason)
        {
            m_sipCDR.Hungup(hangupReason);
        }
    }
}
