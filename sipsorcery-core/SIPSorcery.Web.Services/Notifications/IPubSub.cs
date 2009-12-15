using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.Text;

namespace SIPSorcery.Web.Services
{
    [ServiceContract]
    public interface IPubSub : IPollingDuplex
    {
        [OperationContract(IsOneWay = true)]
        void Subscribe(string topic);

        [OperationContract(IsOneWay = true)]
        void Publish(string topic, string content);
    }
}
