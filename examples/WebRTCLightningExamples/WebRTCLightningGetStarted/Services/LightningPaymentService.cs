//-----------------------------------------------------------------------------
// Filename: PeerConnectionPayState.cs
//
// Description: Maintains the payment state of each WebRTC peer.
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

using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin.DataEncoders;

namespace demo;

public interface ILightningPaymentService
{
    event Action OnLightningInvoiceSettled;

    string? GetLightningPaymentRequest();

    void RequestLightningInvoice();
}

public class LightningPaymentService : ILightningPaymentService, IDisposable
{
    private const int INVOICE_AMOUNT_MILLISATS = 10000;
    private const int INVOICE_EXPIRY_SECONDS = 60;
    private const string INVOICE_DESCRIPTION = "Plz Pay LOLZZZ";

    private readonly ILogger _logger;
    private readonly ILightningService _lightningService;
    private Task<Lnrpc.AddInvoiceResponse>? _getInvoiceTask = null;

    private readonly IDisposable _subscription;

    public event Action OnLightningInvoiceSettled = () => { };

    private bool _hasLightningInvoiceBeenRequested = false;
    private string? _lightningInvoiceRHash;
    private string? _lightningPaymentRequest;

    public LightningPaymentService(
        ILogger<LightningPaymentService> logger,
        ILightningService lightningService,
        LightningInvoiceEventService lightningInvoiceEventService)
    {
        _logger = logger;
        _lightningService = lightningService;

        _subscription = lightningInvoiceEventService.InvoiceStream.Subscribe(OnInvoiceSettled);
    }

    public string? GetLightningPaymentRequest()
    {
        return _lightningPaymentRequest;
    }

    public void OnInvoiceSettled(InvoiceSettledNotification notification)
    {
        //_logger.LogDebug($"{nameof(LightningPaymentService)} Handle notification recevied for rhash {notification.RHash} and payment state rhash {_paymentState.LightningInvoiceRHash}.");

        if (notification.RHash == _lightningInvoiceRHash)
        {
            _logger.LogDebug($"{nameof(LightningPaymentService)} invoice {notification.RHash} successfully settled.");

            OnLightningInvoiceSettled?.Invoke();

            _hasLightningInvoiceBeenRequested = false;
            _lightningInvoiceRHash = null;
            _lightningPaymentRequest = null;
        }
    }

    public void RequestLightningInvoice()
    {
        if (!_hasLightningInvoiceBeenRequested)
        {
            _hasLightningInvoiceBeenRequested = true;

            _logger.LogDebug($"{nameof(RequestLightningInvoice)} for {INVOICE_AMOUNT_MILLISATS} msats and {INVOICE_EXPIRY_SECONDS} expiry seconds.");

            _getInvoiceTask = _lightningService.CreateInvoiceAsync(INVOICE_AMOUNT_MILLISATS, INVOICE_DESCRIPTION, INVOICE_EXPIRY_SECONDS);

            _getInvoiceTask
                .ContinueWith(t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion)
                    {
                        Lnrpc.AddInvoiceResponse response = t.Result;
                        string rHash = Encoders.Hex.EncodeData(response.RHash.ToByteArray());

                        //_logger.LogDebug($"{nameof(RequestLightningInvoice)} successfully generated invoice with rhash {rHash}.");

                        _hasLightningInvoiceBeenRequested = false;
                        _lightningInvoiceRHash = rHash;
                        _lightningPaymentRequest = response.PaymentRequest;
                    }
                });
        }
    }

    public void Dispose()
    {
        _logger.LogDebug($"{nameof(LightningPaymentService)} dispose.");

        _subscription.Dispose();
    }
}
