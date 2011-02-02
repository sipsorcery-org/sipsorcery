using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace SIPSorcery.CRM.ThirtySevenSignals
{
    [XmlRootAttribute("contact-data", Namespace = "", IsNullable = false)]
    public class ContactData
    {
        [XmlArray(ElementName = "phone-numbers")]
        [XmlArrayItem(typeof(PhoneNumber), ElementName = "phone-number")]
        public List<PhoneNumber> PhoneNumbers { get; set; }
    }
}
