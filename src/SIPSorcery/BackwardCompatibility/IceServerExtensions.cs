namespace SIPSorcery.Net;

public static class IceServerExtensions
{
    extension(IceServer source)
    {
        public int _id => source.Id;
    }
}
