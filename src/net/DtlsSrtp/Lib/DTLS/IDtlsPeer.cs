// SharpSRTP
// Copyright (C) 2025 Lukas Volf
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE 
// SOFTWARE.

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Tls;
using System;

namespace SIPSorcery.Net.SRTP.DTLS
{
    public enum TlsAlertLevelsEnum
    {
        Warn = 1,
        Fatal = 2
    }

    // https://www.iana.org/assignments/tls-parameters/tls-parameters.xhtml#tls-parameters-6
    public enum TlsAlertTypesEnum
    {
        CloseNotify = 0,
        // 1-9 unassigned
        UnexpectedMessage = 10,
        // 11-19 unassigned
        BadRecordMac = 20,
        DecryptionFailedReserved = 21, // Used in TLS versions prior to 1.3.
        RecordOverflow = 22,
        // 23-29 unassigned
        DecompressionFailureReserved = 30, // Used in TLS versions prior to 1.3.
        // 31-39 unassigned
        HandshakeFailure = 40,
        NoCertificateReserved = 41, // Used in SSLv3 but not in TLS.
        BadCertificate = 42,
        UnsupportedCertificate = 43,
        CertificateRevoked = 44,
        CertificateExpired = 45,
        CertificateUnknown = 46,
        IllegalParameter = 47,
        UnknownCA = 48,
        AccessDenied = 49,
        DecodeError = 50,
        DecryptError = 51,
        TooManyCidsRequested = 52,
        // 53-59 unassigned
        ExportRestrictionReserved = 60, // Used in TLS 1.0 but not TLS 1.1 or later.
        ProtocolVersion = 70,
        InsufficientSecurity = 71,
        // 72-79 unassigned
        InternalEror = 80,
        // 81-85 unassigned
        InappropriateFallback = 86,
        // 87-89 unassigned
        UserCanceled = 90,
        // 91-99 unassigned
        NoRenegotiationReserved = 100, // Used in TLS versions prior to 1.3.
        // 101-108 unassigned
        MissingExtension = 109,
        UnsupportedExtension = 110,
        CertificateUnobtainableReserved = 111, // Used in TLS versions prior to 1.3.
        UnrecognizedName = 112,
        BadCertificateStatusResponse = 113,
        BadCertificateHashValueReserved = 114, // Used in TLS versions prior to 1.3.
        UnknownPskIdentity = 115,
        CertificateRequired = 116,
        GeneralError = 117,
        // 118-119 unassigned
        NoApplicationProtocol = 120,
        EchRequired = 121,
        // 122-255 unassigned
        Unassigned = 255
    }

    public class DtlsAlertEventArgs : EventArgs
    {
        public TlsAlertLevelsEnum Level { get; }
        public TlsAlertTypesEnum AlertType { get; }
        public string Description { get; }

        public DtlsAlertEventArgs(TlsAlertLevelsEnum level, TlsAlertTypesEnum type, string description)
        {
            this.Level = level;
            this.AlertType = type;
            this.Description = description;
        }
    }

    public class DtlsHandshakeCompletedEventArgs : EventArgs
    {
        public SecurityParameters SecurityParameters { get; }

        public DtlsHandshakeCompletedEventArgs(SecurityParameters securityParameters)
        {
            SecurityParameters = securityParameters;
        }
    }

    public interface IDtlsPeer
    {
        event EventHandler<DtlsAlertEventArgs> OnAlert;
        event EventHandler<DtlsHandshakeCompletedEventArgs> OnHandshakeCompleted;
        int TimeoutMilliseconds { get; set; }
        bool ForceUseExtendedMasterSecret { get; set; }

        Certificate Certificate { get; }
        AsymmetricKeyParameter CertificatePrivateKey { get; }
        short CertificateSignatureAlgorithm { get; }
        short CertificateHashAlgorithm { get; }
        void SetCertificate(Certificate certificate, AsymmetricKeyParameter privateKey, short signatureAlgorithm, short hashAlgorithm);

        DtlsTransport DoHandshake(out string handshakeError, DatagramTransport datagramTransport, Func<string> getRemoteEndpoint, Func<string, DatagramTransport> createClientDatagramTransport);
    }
}
