//-----------------------------------------------------------------------------
// Filename: LightningService.cs
//
// Description: Service to manage the creation and event handling for Lightning
// invoices and payments.
//
// LND GRPC proto files:
// https://github.com/lightningnetwork/lnd/blob/master/lnrpc/lightning.proto
// https://github.com/lightningnetwork/lnd/blob/master/lnrpc/invoicesrpc/invoices.proto
// https://github.com/lightningnetwork/lnd/blob/master/lnrpc/routerrpc/router.proto
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

using Invoicesrpc;
using Lnrpc;
using Microsoft.Extensions.Logging;
using NBitcoin.DataEncoders;
using System.Threading.Tasks;

namespace demo;

public interface ILightningService
{
    Task<GetInfoResponse> GetInfoAsync();

    Task<Invoice> GetInvoiceAsync(string paymentHash);

    Task<AddInvoiceResponse> CreateInvoiceAsync(long amountMilliSatoshis, string description, long expirySeconds);

    Task<CancelInvoiceResp> CancelInvoiceAsync(Google.Protobuf.ByteString rHash);

    PayReq DecodePaymentRequest(string paymentRequest);
}

public class LightningService : ILightningService
{
    private ILogger _logger;

    private readonly ILightningClientFactory _lightningClientFactory;

    public LightningService(
        ILogger<LightningService> logger,
        ILightningClientFactory lightningClientFactory)
    {
        _logger = logger;
        _lightningClientFactory = lightningClientFactory;
    }

    public async Task<GetInfoResponse> GetInfoAsync()
    {
        _logger.LogInformation($"{nameof(GetInfoAsync)}.");

        var lightningClient = _lightningClientFactory.GetClient();

        var response = await lightningClient.GetInfoAsync(new GetInfoRequest());

        return response;
    }

    public async Task<AddInvoiceResponse> CreateInvoiceAsync(long amountMilliSatoshis, string description, long expirySeconds)
    {
        _logger.LogInformation($"{nameof(CreateInvoiceAsync)} for {amountMilliSatoshis} millisats and expiry seconds {expirySeconds}.");

        var lightningClient = _lightningClientFactory.GetClient();

        var invoiceRequest = new Invoice
        {
            Memo = description,
            ValueMsat = amountMilliSatoshis,
            Expiry = expirySeconds
        };

        return await lightningClient.AddInvoiceAsync(invoiceRequest);
    }

    public PayReq DecodePaymentRequest(string paymentRequest)
    {
        _logger.LogInformation($"{nameof(DecodePaymentRequest)}");

        var lightningClient = _lightningClientFactory.GetClient();

        var payReqString = new PayReqString { PayReq = paymentRequest };
        var decodedInvoice = lightningClient.DecodePayReq(payReqString);

        return decodedInvoice;
    }

    public async Task<Invoice> GetInvoiceAsync(string paymentHash)
    {
        _logger.LogInformation($"{nameof(GetInvoiceAsync)} for payment hash {paymentHash}.");

        var lightningClient = _lightningClientFactory.GetClient();

        return await lightningClient.LookupInvoiceAsync(
            new PaymentHash { RHash = Google.Protobuf.ByteString.CopyFrom(Encoders.Hex.DecodeData(paymentHash)) });
    }

    public async Task<CancelInvoiceResp> CancelInvoiceAsync(Google.Protobuf.ByteString rHash)
    {
        _logger.LogInformation($"{nameof(CancelInvoiceAsync)} for rhash {rHash.ToBase64()}.");

        var invoicesClient = _lightningClientFactory.GetInvoicesClient();

        var cancelInvoiceRequest = new CancelInvoiceMsg { PaymentHash = rHash };

        return await invoicesClient.CancelInvoiceAsync(cancelInvoiceRequest);
    }
}
