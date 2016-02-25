using System;

namespace SIPSorcery.SIP.App
{
    public interface ISIPAsset
    {
        Guid Id { get; set; }

#if !SILVERLIGHT
        void Load(System.Data.DataRow row);
        //Dictionary<Guid, object> Load(XmlDocument dom);
        System.Data.DataTable GetTable();
#endif

        string ToXML();
        string ToXMLNoParent();
        string GetXMLElementName();
        string GetXMLDocumentElementName();
    }
}
