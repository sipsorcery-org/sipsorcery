using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace System.Net.Security;

public static class SslClientAuthenticationOptionsExtensions
{
    extension(SslClientAuthenticationOptions)
    {
        public static SslClientAuthenticationOptions CreateFrom(SslClientAuthenticationOptions? sslClientAuthenticationOptions)
        {
            var newSslClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.None,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            };

            if (sslClientAuthenticationOptions is { })
            {
                newSslClientAuthenticationOptions.ClientCertificates = sslClientAuthenticationOptions.ClientCertificates;
                newSslClientAuthenticationOptions.EnabledSslProtocols = sslClientAuthenticationOptions.EnabledSslProtocols;
                newSslClientAuthenticationOptions.EncryptionPolicy = sslClientAuthenticationOptions.EncryptionPolicy;
                newSslClientAuthenticationOptions.LocalCertificateSelectionCallback = sslClientAuthenticationOptions.LocalCertificateSelectionCallback;
                newSslClientAuthenticationOptions.RemoteCertificateValidationCallback = sslClientAuthenticationOptions.RemoteCertificateValidationCallback;
                newSslClientAuthenticationOptions.TargetHost = sslClientAuthenticationOptions.TargetHost;
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
                newSslClientAuthenticationOptions.ApplicationProtocols = sslClientAuthenticationOptions.ApplicationProtocols;
                newSslClientAuthenticationOptions.ApplicationProtocols = sslClientAuthenticationOptions.ApplicationProtocols;
#if NET8_0_OR_GREATER
                newSslClientAuthenticationOptions.AllowTlsResume = sslClientAuthenticationOptions.AllowTlsResume;
                newSslClientAuthenticationOptions.CertificateChainPolicy = sslClientAuthenticationOptions.CertificateChainPolicy;
                newSslClientAuthenticationOptions.CipherSuitesPolicy = sslClientAuthenticationOptions.CipherSuitesPolicy;
                newSslClientAuthenticationOptions.ClientCertificateContext = sslClientAuthenticationOptions.ClientCertificateContext;
#endif
#endif
            }

            return newSslClientAuthenticationOptions;
        }
    }
}
