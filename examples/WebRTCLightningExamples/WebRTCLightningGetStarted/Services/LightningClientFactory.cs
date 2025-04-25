//-----------------------------------------------------------------------------
// Filename: LightningClientFactory.cs
//
// Description: Factory class to create a new lightning client.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 23 Feb 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NBitcoin.DataEncoders;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace demo;

public interface ILightningClientFactory
{
    Lnrpc.Lightning.LightningClient GetClient();

    Invoicesrpc.Invoices.InvoicesClient GetInvoicesClient();

    Routerrpc.Router.RouterClient GetRouterClient();
}

public class LightningClientFactory : ILightningClientFactory
{
    private ILogger _logger;
    private IConfiguration _config;

    private readonly string _lndUrl;
    private readonly string _lndMacaroonHex;
    private readonly X509Certificate2 _lndCertificate;

    public LightningClientFactory(
        ILogger<LightningClientFactory> logger,
        IConfiguration config)
    {
        _logger = logger;
        _config = config;

        _lndUrl = config[ConfigKeys.LND_URL] ?? string.Empty;
        _lndMacaroonHex = LoadLndMacaroonAsHex();
        _lndCertificate = LoadLndPublicCertificate() ?? new X509Certificate2([]);
    }

    private string LoadLndMacaroonAsHex()
    {
        var macaroonFilePath = _config[ConfigKeys.LND_MACAROON_PATH];

        if (!string.IsNullOrWhiteSpace(macaroonFilePath))
        {
            if(!File.Exists(macaroonFilePath))
            {
                _logger.LogError($"Macaroon file does not exist at {macaroonFilePath}.");
            }
            else
            {
                var macaroonBytes = File.ReadAllBytes(macaroonFilePath);
                return Encoders.Hex.EncodeData(macaroonBytes);
            }
        }

        return _config[ConfigKeys.LND_MACAROON_HEX] ?? string.Empty;
    }

    private X509Certificate2? LoadLndPublicCertificate()
    {
        X509Certificate2? certificate = null;
        var certificateFileName = _config[ConfigKeys.LND_CERTIFICATE_PATH];

        if (string.IsNullOrEmpty(certificateFileName))
        {
            var base64Cert = _config[ConfigKeys.LND_CERTIFICATE_BASE64];
            if (!string.IsNullOrEmpty(base64Cert))
            {
                certificate = new X509Certificate2(Convert.FromBase64String(base64Cert));

                if (certificate != null)
                {
                    //_logger.LogInformation($"LND certificate loaded from base64 config setting, thumbprint {certificate.Thumbprint}.");
                }
                else
                {
                    _logger.LogError($"LND base64 certificate could not be loaded from {base64Cert}.");
                }
            }
            else
            {
                _logger.LogError($"LND base64 certificate from config setting {ConfigKeys.LND_CERTIFICATE_BASE64} was empty.");
            }
        }
        else
        {
            if (File.Exists(certificateFileName))
            {
                certificate = new X509Certificate2(certificateFileName);

                if (certificate != null)
                {
                    //_logger.LogInformation($"LND certificate loaded from file {certificateFileName}, thumbprint {certificate.Thumbprint}.");
                }
                else
                {
                    _logger.LogError($"LND file certificate could not be loaded from {certificateFileName}.");
                }
            }
            else
            {
                _logger.LogError($"LND certificate was not found at the specified file path of {certificateFileName}.");
            }
        }

        return certificate;
    }

    public Lnrpc.Lightning.LightningClient GetClient()
    {
        var credentials = ChannelCredentials.Create(new SslCredentials(), CallCredentials.FromInterceptor(AddMacaroon));

        var lndGrpcChannel = GrpcChannel.ForAddress(_lndUrl,
                new GrpcChannelOptions { HttpHandler = GetSelfSignedCertificateHandler(_lndCertificate), Credentials = credentials });
        var lightningClient = new Lnrpc.Lightning.LightningClient(lndGrpcChannel);

        return lightningClient;
    }

    public Invoicesrpc.Invoices.InvoicesClient GetInvoicesClient()
    {
        var credentials = ChannelCredentials.Create(new SslCredentials(), CallCredentials.FromInterceptor(AddMacaroon));

        var lndGrpcChannel = GrpcChannel.ForAddress(_lndUrl,
                new GrpcChannelOptions { HttpHandler = GetSelfSignedCertificateHandler(_lndCertificate), Credentials = credentials });
        var invoicesClient = new Invoicesrpc.Invoices.InvoicesClient(lndGrpcChannel);

        return invoicesClient;
    }

    public Routerrpc.Router.RouterClient GetRouterClient()
    {
        var credentials = ChannelCredentials.Create(new SslCredentials(), CallCredentials.FromInterceptor(AddMacaroon));

        var lndGrpcChannel = GrpcChannel.ForAddress(_lndUrl,
                new GrpcChannelOptions { HttpHandler = GetSelfSignedCertificateHandler(_lndCertificate), Credentials = credentials });
        var routerClient = new Routerrpc.Router.RouterClient(lndGrpcChannel);

        return routerClient;
    }

    private Task AddMacaroon(AuthInterceptorContext context, Metadata metadata)
    {
        metadata.Add(new Metadata.Entry("macaroon", _lndMacaroonHex));
        return Task.CompletedTask;
    }

    private HttpMessageHandler GetSelfSignedCertificateHandler(X509Certificate2 lndCertificate)
    {
        return new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                {
                    if(sslPolicyErrors == SslPolicyErrors.None)
                    {
                        return true;
                    }

                    // Allow untrusted root, self-signed certificate or both.
                    if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors ||
                        sslPolicyErrors == SslPolicyErrors.RemoteCertificateNameMismatch ||
                        sslPolicyErrors == (SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch))
                    {
                        // Create a new X509Chain and add the LND certificate as a trusted root
                        var customChain = new X509Chain();
                        customChain.ChainPolicy.ExtraStore.Add(lndCertificate);
                        customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                        customChain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

                        // Verify the server certificate against the trusted root
                        var isValid = certificate != null ? customChain.Build(new X509Certificate2(certificate)) : false;

                        // Check if the LND certificate is in the custom chain
                        var isLndCertInChain = customChain.ChainElements
                            .Cast<X509ChainElement>()
                            .Any(x => x.Certificate.Thumbprint == lndCertificate.Thumbprint);

                        return isValid && isLndCertInChain;
                    }

                    return false;
                }
            }
        };
    }
}
