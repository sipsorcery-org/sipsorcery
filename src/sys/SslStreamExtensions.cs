using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace SIPSorcery.Sys;

internal static class SslStreamExtensions
{
#if !NETSTANDARD2_1_OR_GREATER || !NETCOREAPP3_1_OR_GREATER
    public static SslStream Create(Stream innerStream, bool leaveInnerStreamOpen, SslClientAuthenticationOptions sslClientAuthenticationOptions)
    {
        return new SslStream(
            innerStream,
            leaveInnerStreamOpen,
            sslClientAuthenticationOptions.RemoteCertificateValidationCallback,
            sslClientAuthenticationOptions.LocalCertificateSelectionCallback,
            sslClientAuthenticationOptions.EncryptionPolicy);
    }

    public static Task AuthenticateAsClientAsync(this SslStream sslStream, SslClientAuthenticationOptions sslClientAuthenticationOptions, CancellationToken cancellationToken = default)
    {
        return sslStream.AuthenticateAsClientAsync(
            sslClientAuthenticationOptions.TargetHost,
            sslClientAuthenticationOptions.ClientCertificates,
            sslClientAuthenticationOptions.EnabledSslProtocols,
            sslClientAuthenticationOptions.CertificateRevocationCheckMode != X509RevocationMode.Online);
    }
#else
    public static SslStream Create(Stream innerStream, bool leaveInnerStreamOpen, global::System.Net.Security.SslClientAuthenticationOptions sslClientAuthenticationOptions)
    {
        return new SslStream(innerStream, leaveInnerStreamOpen: leaveInnerStreamOpen);
    }
#endif
}
