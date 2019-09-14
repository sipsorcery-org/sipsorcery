using System;

namespace SIPSorcery.Persistence
{
    public interface ISIPAsset
    {
        Guid Id { get; set; }

        void Load(System.Data.DataRow row);

        System.Data.DataTable GetTable();

        string ToXML();
        string ToXMLNoParent();
        string GetXMLElementName();
        string GetXMLDocumentElementName();
    }
}
