using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace SIPSorcery.CRM.ThirtySevenSignals
{
    [XmlRootAttribute("people", Namespace = "", IsNullable = false)]
    public class People
    {
        [XmlElement("person")]
        public List<Person> PersonList { get; set; }
    }
}
