using System.ServiceModel;
using System.ServiceModel.Channels;

namespace SIPSorcery.Silverlight.Messaging
{
    public interface IClientCustomHeader
    {
        object BeforeSendRequest(ref Message request, IClientChannel channel);
        void AfterReceiveReply(ref Message reply, object correlationState);
    }
}
