using System;
using System.ServiceModel;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Web.Services
{
    [MessageContract]
    public class NotificationData
    {
        public const string NOTIFICATION_ACTION = "http://microsoft.com/samples/pollingDuplex/notification";
        public const string CLOSE_ACTION = "http://microsoft.com/samples/pollingDuplex/notification";
        public const string NOTIFICATION_CLOSE_CONTENT = "closesession";

        [MessageBodyMember]
        public string NotificationContent { get; set; }
    }
}
