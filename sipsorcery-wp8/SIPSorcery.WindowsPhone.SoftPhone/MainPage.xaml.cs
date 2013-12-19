using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Net.NetworkInformation;
using Microsoft.Phone.Shell;
using SIPSorcery.WindowsPhone.SoftPhone.Resources;
using SIPSorcery.SIP;
using SIPSorcery.WP.SoftPhone;

namespace SIPSorcery.WindowsPhone.SoftPhone
{
    public partial class MainPage : PhoneApplicationPage
    {
        private Socket m_socket;

        // Constructor
        public MainPage()
        {
            InitializeComponent();

            // Sample code to localize the ApplicationBar
            //BuildLocalizedApplicationBar();

            this.Loaded += MainPage_Loaded;
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            SIPClient sipClient = new SIPClient(new IPEndPoint(IPAddress.Parse("10.1.1.8"), 35469), ResolveSIPEndPoint);
            sipClient.StatusMessage += (msg) => { Debug.WriteLine(msg); };
            sipClient.Call("aaron@10.1.1.2", new IPEndPoint(IPAddress.Parse("10.1.1.8"), 35470), "aaronwp8", "password", "10.1.1.2");

            //m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            //m_socket.SetNetworkPreference(NetworkSelectionCharacteristics.NonCellular);

            //SocketAsyncEventArgs receiveArgs = new SocketAsyncEventArgs();
            //receiveArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            //receiveArgs.SetBuffer(new Byte[2048], 0, 2048);
            //receiveArgs.Completed += SocketRead_Completed;
            ////m_socket.Bind(new IPEndPoint(IPAddress.Parse("10.1.1.7"), 0));
            //m_socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            //m_socket.ReceiveAsync(receiveArgs);

            //Debug.WriteLine("Sending some dummy data to sipsorcery.com from " + m_socket.LocalEndPoint);

            //SocketAsyncEventArgs sendArgs = new SocketAsyncEventArgs();
            //var buffer = Encoding.UTF8.GetBytes("REGISTER sip:124@10.1.1.2 SIP/2.0\r\n\r\n\r\n");
            //sendArgs.SetBuffer(buffer, 0, buffer.Length);
            //sendArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Parse("10.1.1.2"), 5060);
            //m_socket.SendToAsync(sendArgs);
        }

        //private void SocketRead_Completed(object sender, SocketAsyncEventArgs e)
        //{
        //    var remote = e.RemoteEndPoint;
        //}

        private SIPDNSLookupResult ResolveSIPEndPoint(SIPURI uri, bool synchronous)
        {
            return new SIPDNSLookupResult(null, SIPEndPoint.ParseSIPEndPoint("udp:10.1.1.2:5060"));
        }
    }
}