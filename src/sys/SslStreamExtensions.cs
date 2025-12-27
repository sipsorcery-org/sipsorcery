using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace SIPSorcery.Sys;

internal static class SslStreamExtensions
{
#if !NETSTANDARD2_1_OR_GREATER || !NETCOREAPP3_1_OR_GREATER || NETFRAMEWORK
    public static SslClientAuthenticationOptions Clone(SslClientAuthenticationOptions? sslClientAuthenticationOptions)
    {
        var newSslClientAuthenticationOptions = new SslClientAuthenticationOptions
        {
            EnabledSslProtocols = SslProtocols.Tls12,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        };
        
        if (sslClientAuthenticationOptions is { } iceServerTls)
        {
            newSslClientAuthenticationOptions.ClientCertificates = iceServerTls.ClientCertificates;
            newSslClientAuthenticationOptions.EnabledSslProtocols = iceServerTls.EnabledSslProtocols;
            newSslClientAuthenticationOptions.EncryptionPolicy = iceServerTls.EncryptionPolicy;
            newSslClientAuthenticationOptions.LocalCertificateSelectionCallback = iceServerTls.LocalCertificateSelectionCallback;
            newSslClientAuthenticationOptions.RemoteCertificateValidationCallback = iceServerTls.RemoteCertificateValidationCallback;
            newSslClientAuthenticationOptions.TargetHost = iceServerTls.TargetHost;
        }

        return newSslClientAuthenticationOptions;
    }

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
    public static SslClientAuthenticationOptions Clone(global::System.Net.Security.SslClientAuthenticationOptions? sslClientAuthenticationOptions)
    {
        var newSslClientAuthenticationOptions = new SslClientAuthenticationOptions
        {
            EnabledSslProtocols = SslProtocols.None,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        };

        if (sslClientAuthenticationOptions is { })
        {
            newSslClientAuthenticationOptions.ApplicationProtocols = sslClientAuthenticationOptions.ApplicationProtocols;
            newSslClientAuthenticationOptions.ApplicationProtocols = sslClientAuthenticationOptions.ApplicationProtocols;
            newSslClientAuthenticationOptions.ClientCertificates = sslClientAuthenticationOptions.ClientCertificates;
            newSslClientAuthenticationOptions.EnabledSslProtocols = sslClientAuthenticationOptions.EnabledSslProtocols;
            newSslClientAuthenticationOptions.EncryptionPolicy = sslClientAuthenticationOptions.EncryptionPolicy;
            newSslClientAuthenticationOptions.LocalCertificateSelectionCallback = sslClientAuthenticationOptions.LocalCertificateSelectionCallback;
            newSslClientAuthenticationOptions.RemoteCertificateValidationCallback = sslClientAuthenticationOptions.RemoteCertificateValidationCallback;
            newSslClientAuthenticationOptions.TargetHost = sslClientAuthenticationOptions.TargetHost;
#if NET8_0_OR_GREATER
            newSslClientAuthenticationOptions.AllowTlsResume = sslClientAuthenticationOptions.AllowTlsResume;
            newSslClientAuthenticationOptions.CertificateChainPolicy = sslClientAuthenticationOptions.CertificateChainPolicy;
            newSslClientAuthenticationOptions.CipherSuitesPolicy = sslClientAuthenticationOptions.CipherSuitesPolicy;
            newSslClientAuthenticationOptions.ClientCertificateContext = sslClientAuthenticationOptions.ClientCertificateContext;
#endif
        }

        return newSslClientAuthenticationOptions;
    }

    public static SslStream Create(Stream innerStream, bool leaveInnerStreamOpen, global::System.Net.Security.SslClientAuthenticationOptions sslClientAuthenticationOptions)
    {
        return new SslStream(innerStream, leaveInnerStreamOpen: leaveInnerStreamOpen);
    }
#endif
}
