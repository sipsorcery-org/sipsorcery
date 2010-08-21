using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net;
using System.Text;
using System.Threading;

namespace SIPSorcery.Net
{
    class Program
    {
        public const string IPADDRESS_ONE_KEY = "IPAddressOne";
        public const string PORT_ONE_KEY = "PortOne";
        public const string IPADDRESS_TWO_KEY = "IPAddressTwo";
        public const string PORT_TWO_KEY = "PortTwo";
        
        static void Main(string[] args)
        {
            try
            {
                IPAddress IPAddressOne = IPAddress.Parse(ConfigurationManager.AppSettings[IPADDRESS_ONE_KEY]); ;
                int PortOne = Convert.ToInt32(ConfigurationManager.AppSettings[PORT_ONE_KEY]);
                IPAddress IPAddressTwo = IPAddress.Parse(ConfigurationManager.AppSettings[IPADDRESS_TWO_KEY]);
                int PortTwo = Convert.ToInt32(ConfigurationManager.AppSettings[PORT_TWO_KEY]);

                IPEndPoint primaryEndPoint = new IPEndPoint(IPAddressOne, PortOne);
                IPEndPoint secondaryEndPoint = new IPEndPoint(IPAddressTwo, PortTwo);

                // Create the two listeners to receive STUN requests.
                STUNListener primaryListener = new STUNListener(primaryEndPoint);
                STUNListener secondaryListener = new STUNListener(secondaryEndPoint);

                // Wire up the STUN server to process the requests.
                STUNServer stunServer = new STUNServer(primaryEndPoint, primaryListener.Send, secondaryEndPoint, secondaryListener.Send);
                primaryListener.MessageReceived += new STUNMessageReceived(stunServer.STUNPrimaryReceived);
                secondaryListener.MessageReceived += new STUNMessageReceived(stunServer.STUNSecondaryReceived);

                ManualResetEvent dontStopEvent = new ManualResetEvent(false);
                dontStopEvent.WaitOne();
            }
            catch (Exception excp)
            {
                Console.WriteLine("Exception Main. " + excp.Message);
                Console.ReadLine();
            }
        }
    }
}
