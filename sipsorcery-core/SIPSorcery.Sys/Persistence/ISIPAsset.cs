using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace SIPSorcery.Sys {
    
    public interface ISIPAsset {
        string Id { get; set; }

#if !SILVERLIGHT
        void Load(System.Data.DataRow row);
        Dictionary<Guid, object> Load(XmlDocument dom);
#endif

        string ToXML();
        string ToXMLNoParent();
        string GetXMLElementName();
        string GetXMLDocumentElementName();
    }
}
