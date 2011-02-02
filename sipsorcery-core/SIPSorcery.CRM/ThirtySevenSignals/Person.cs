using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace SIPSorcery.CRM.ThirtySevenSignals
{
    [Serializable()]
    [XmlRootAttribute("person", Namespace = "", IsNullable = false)]
    public class Person
    {
        public const string DATETIME_STRING_FORMAT = "yyyy-MM-ddTHH:mm:ssZ";
        public const string AVATAR_URL_FORMAT = "{0}/avatars/person/{1}/{2}-large.png";

        [XmlElement("id")]
        public int ID { get; set; }

        [XmlElement("author-id")]
        public int AuthorID { get; set; }

        [XmlElement("first-name")]
        public string FirstName { get; set; }

        [XmlElement("last-name")]
        public string LastName { get; set; }

        [XmlElement("title")]
        public string Title { get; set; }

        [XmlElement("background")]
        public string Background { get; set; }

        [XmlElement("company-id")]
        public int? CompanyID { get; set; }

        [XmlElement("created-at")]
        public string CreatedAtStr { get; set; }

        [XmlIgnore]
        public DateTimeOffset CreatedAt
        {
            get { return DateTimeOffset.Parse(CreatedAtStr); }
            set { CreatedAtStr = value.ToUniversalTime().ToString(DATETIME_STRING_FORMAT); }
        }

        [XmlElement("updated-at")]
        public string UpdatedAtStr { get; set; }

        [XmlIgnore]
        public DateTimeOffset UpdatedAt
        {
            get { return DateTimeOffset.Parse(UpdatedAtStr); }
            set { UpdatedAtStr = value.ToUniversalTime().ToString(DATETIME_STRING_FORMAT); }
        }

        [XmlElement("visible-to")]
        public string VisibleTo { get; set; }

        [XmlElement("owner-id", IsNullable=true)]
        public int? OwnerID { get; set; }

        [XmlElement("group-id", IsNullable = true)]
        public int? GroupID { get; set; }

        [XmlElement("contact-data")]
        public ContactData ContactData { get; set; }

        [XmlIgnore]
        public string AvatarURL { get; private set; }

        public void SetAvatarURL(string url)
        {
            AvatarURL = String.Format(AVATAR_URL_FORMAT, url, (ID / 10000).ToString(), (ID % 10000).ToString());
        }
    }
}
