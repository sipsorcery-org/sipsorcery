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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin.DataEncoders;

namespace demo;

public interface ILightningPaymentService
{
    event Action OnLightningInvoiceSettled;

    event Action OnLightningInvoiceExpired;

    event Action<string> OnLightningPaymentRequestGenerated;

    void RequestLightningInvoice(string description, int expirySeconds);
}

public class LightningPaymentService : ILightningPaymentService, IDisposable
{
    private const int INVOICE_AMOUNT_MILLISATS = 10000;

    private readonly ILogger _logger;
    private readonly ILightningService _lightningService;
    private Task<Lnrpc.AddInvoiceResponse>? _getInvoiceTask = null;

    private readonly IDisposable _subscription;

    public event Action OnLightningInvoiceSettled = () => { };
    public event Action OnLightningInvoiceExpired = () => { };
    public event Action<string> OnLightningPaymentRequestGenerated = (payreq) => { };

    private int _hasLightningInvoiceBeenRequested = 0;

    private string? _lightningInvoiceRHash;

    public LightningPaymentService(
        ILogger<LightningPaymentService> logger,
        ILightningService lightningService,
        LightningInvoiceEventService lightningInvoiceEventService)
    {
        _logger = logger;
        _lightningService = lightningService;

        _subscription = lightningInvoiceEventService.InvoiceStream.Subscribe(OnInvoiceSettled);
    }

    public void OnInvoiceSettled(LightningInvoicedNotification notification)
    {
        if (notification.RHash == _lightningInvoiceRHash)
        {
            if (notification.NotificationType == LightningInvoiceNotificationTypeEnum.Settled)
            {
                _logger.LogDebug($"{nameof(LightningPaymentService)} invoice {notification.RHash} successfully settled.");
                OnLightningInvoiceSettled?.Invoke();
            }
            else if(notification.NotificationType == LightningInvoiceNotificationTypeEnum.Cancelled)
            {
                _logger.LogDebug($"{nameof(LightningPaymentService)} invoice {notification.RHash} expired.");
                OnLightningInvoiceExpired?.Invoke();
            }

            _hasLightningInvoiceBeenRequested = 0;
            _lightningInvoiceRHash = null;
        }
    }

    public void RequestLightningInvoice(string description, int expirySeconds)
    {
        if (Interlocked.CompareExchange(ref _hasLightningInvoiceBeenRequested, 1, 0) == 0)
        {
            _logger.LogDebug($"{nameof(RequestLightningInvoice)} for {INVOICE_AMOUNT_MILLISATS} msats and {expirySeconds} expiry seconds.");

            _getInvoiceTask = _lightningService.CreateInvoiceAsync(INVOICE_AMOUNT_MILLISATS, description, expirySeconds);

            _getInvoiceTask
                .ContinueWith(t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion)
                    {
                        Lnrpc.AddInvoiceResponse response = t.Result;
                        string rHash = Encoders.Hex.EncodeData(response.RHash.ToByteArray());

                        //_logger.LogDebug($"{nameof(RequestLightningInvoice)} successfully generated invoice with rhash {rHash}.");

                        _hasLightningInvoiceBeenRequested = 0;
                        _lightningInvoiceRHash = rHash;

                        OnLightningPaymentRequestGenerated?.Invoke(response.PaymentRequest);
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
