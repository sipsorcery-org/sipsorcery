namespace SIPSorcery.Net
{
    public class SDPConnectionInformation
    {
        public const string m_CRLF = "\r\n";

        public string ConnectionNetworkType = "IN";		// Type of network, IN = Internet.
        public string ConnectionAddressType = "IP4";	// Address type, typically IP4 or IP6.
        public string ConnectionAddress;				// IP or mulitcast address for the media connection.

        private SDPConnectionInformation()
        { }

        public SDPConnectionInformation(string connectionAddress)
        {
            ConnectionAddress = connectionAddress;
        }

        public static SDPConnectionInformation ParseConnectionInformation(string connectionLine)
        {
            SDPConnectionInformation connectionInfo = new SDPConnectionInformation();
            string[] connectionFields = connectionLine.Substring(2).Trim().Split(' ');
            connectionInfo.ConnectionNetworkType = connectionFields[0].Trim();
            connectionInfo.ConnectionAddressType = connectionFields[1].Trim();
            connectionInfo.ConnectionAddress = connectionFields[2].Trim();
            return connectionInfo;
        }

        public override string ToString()
        {
            return "c=" + ConnectionNetworkType + " " + ConnectionAddressType + " " + ConnectionAddress + m_CRLF;
        }
    }
}
