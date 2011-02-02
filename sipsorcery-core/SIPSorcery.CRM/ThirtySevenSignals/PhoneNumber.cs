using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace SIPSorcery.CRM.ThirtySevenSignals
{
    [XmlRootAttribute("phone-number", Namespace = "", IsNullable = false)]
    public class PhoneNumber
    {
        [XmlElement("id")]
        public int ID { get; set; }

        [XmlElement("location")]
        public string Location { get; set; }

        [XmlElement("number")]
        public string Number { get; set; }
    }
}
