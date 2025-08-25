﻿using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace SIPSorcery.Sys
{
#if !(NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER) || NETFRAMEWORK
    public class SslClientAuthenticationOptions
    {
        public LocalCertificateSelectionCallback? LocalCertificateSelectionCallback { get; set; }

        public RemoteCertificateValidationCallback? RemoteCertificateValidationCallback { get; set; }

        public string? TargetHost { get; set; }

        public X509CertificateCollection? ClientCertificates { get; set; }

        public EncryptionPolicy EncryptionPolicy { get; set; }

        public SslProtocols EnabledSslProtocols { get; set; }

        public X509RevocationMode CertificateRevocationCheckMode { get; set; }
    }
#endif
}
