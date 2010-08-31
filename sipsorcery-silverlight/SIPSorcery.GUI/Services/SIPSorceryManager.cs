using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.ServiceModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SIPSorcery.Silverlight.Messaging;
using SIPSorcery.SIPSorceryManagerClient;

namespace SIPSorcery.Persistence
{
    public delegate void IsManagerAliveCompleteDelegate(SIPSorcery.SIPSorceryManagerClient.IsAliveCompletedEventArgs e);
    public delegate void GetRegistrarRecordCompleteDelegate(SIPRegistrarRecord registrarRecord);

    public class SIPSorceryManager
    {
        public event IsManagerAliveCompleteDelegate IsManagerAliveComplete;
        public event GetRegistrarRecordCompleteDelegate GetRegistrarRecordComplete;

        private SIPManagerWebServiceClient m_managerServiceProxy;

        public SIPSorceryManager(string serverURL)
        {
            //BasicHttpBinding binding = new BasicHttpBinding();
            BasicHttpCustomHeaderBinding binding = new BasicHttpCustomHeaderBinding(new SecurityHeader("myauthid"));
            EndpointAddress address = new EndpointAddress(serverURL);
            m_managerServiceProxy = new SIPManagerWebServiceClient(binding, address);
           
            m_managerServiceProxy.IsAliveCompleted += new EventHandler<SIPSorcery.SIPSorceryManagerClient.IsAliveCompletedEventArgs>(ManagerIsAliveCompleted);
            m_managerServiceProxy.GetRegistrarRecordCompleted += new EventHandler<GetRegistrarRecordCompletedEventArgs>(GetRegistrarRecordCompleted);
        }

        public void IsManagerAliveAsync()
        {
            m_managerServiceProxy.IsAliveAsync();
        }

        private void ManagerIsAliveCompleted(object sender, SIPSorcery.SIPSorceryManagerClient.IsAliveCompletedEventArgs e)
        {
            if (IsManagerAliveComplete != null)
            {
                IsManagerAliveComplete(e);
            }
        }

        private void GetRegistrarRecordCompleted(object sender, GetRegistrarRecordCompletedEventArgs e)
        {
            if (GetRegistrarRecordComplete != null)
            {
                GetRegistrarRecordComplete(e.Result);
            }
        }

        public void GetRegistrarRecordAsync(string sipUsername, string domain)
        {
            m_managerServiceProxy.GetRegistrarRecordAsync(sipUsername, domain);
        }
    }
}
