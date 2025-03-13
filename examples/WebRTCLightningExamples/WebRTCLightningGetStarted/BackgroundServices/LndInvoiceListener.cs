//-----------------------------------------------------------------------------
// Filename: LndInvoiceListener.cs
// 
// Description: Background task that subscribes to the LND GRPC invoice
// subscription end point to receive notifications about new invoice events.
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
using Lnrpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin.DataEncoders;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace demo;

public class LndInvoiceListener : BackgroundService
{
    /// <summary>
    /// Interval at which to check the GRPC connection to the LND server is still operational.
    /// </summary>
    private const int CONNECTION_CHECK_INTERVAL_SECONDS = 30;

    /// <summary>
    /// Short delay when restarting the listener to avoid denial of service type behaviour if something has
    /// gone wrong.
    /// </summary>
    private const int RESTART_DELAY_SECONDS = 5;

    private readonly ILogger _logger;
    private readonly ILightningClientFactory _lightningClientFactory;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly LightningInvoiceEventService _lightningInvoiceEventService;

    public LndInvoiceListener(
        ILogger<LndInvoiceListener> logger,
        IConfiguration config,
        ILightningClientFactory lightningClientFactory,
        IHostApplicationLifetime hostApplicationLifetime,
        LightningInvoiceEventService lightningInvoiceEventService)
    {
        _logger = logger;
        _lightningClientFactory = lightningClientFactory;
        _hostApplicationLifetime = hostApplicationLifetime;
        _lightningInvoiceEventService = lightningInvoiceEventService;
    }

    protected override async Task ExecuteAsync(CancellationToken cancelToken)
    {
        _logger.LogInformation($"{nameof(LndInvoiceListener)} started listening.");

        while (!_hostApplicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            try
            {
                await Listen(cancelToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex.InnerException is OperationCanceledException)
            {
                if (_hostApplicationLifetime.ApplicationStopping.IsCancellationRequested)
                {
                    _logger.LogDebug($"{nameof(LndInvoiceListener)} application is shutting down.");
                    break;
                }
                else
                {
                    _logger.LogWarning($"{nameof(LndInvoiceListener)} lnd connection failed, cancelling and restarting listener thread.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(LndInvoiceListener)} listener exception. {ex}");
            }

            _logger.LogDebug($"{nameof(LndInvoiceListener)} delaying {RESTART_DELAY_SECONDS}s before attempting to reconnect the grpc listener at.");
            await Task.Delay(1000 * RESTART_DELAY_SECONDS);
        }
    }

    /// <summary>
    /// Listens for new invoice events from the LND server over the GRPC connection.
    /// </summary>
    /// <param name="cancelToken">The cancellation token that will be set when the application shuts down
    /// or when there is a problem with the connection and it needs to be restarted.</param>
    private async Task Listen(CancellationToken cancelToken)
    {
        var keepAliveTokenSource = new CancellationTokenSource();
        var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancelToken, keepAliveTokenSource.Token);

        var lightningClient = _lightningClientFactory.GetClient();

        _ = Task.Run(() => CheckLndConnection(lightningClient, linkedTokenSource));

        // Subscribe to LND invoices
        using var invoiceStream = lightningClient.SubscribeInvoices(new InvoiceSubscription());

        // Receive invoices
        await foreach (var invoice in invoiceStream.ResponseStream.ReadAllAsync(linkedTokenSource.Token))
        {
            string rHash = Encoders.Hex.EncodeData(invoice.RHash.ToByteArray());

            _logger.LogInformation($"Lightning invoice event received for rhash {rHash}, description {invoice.Memo} and state {invoice.State}.");

            if (invoice.State == Invoice.Types.InvoiceState.Settled)
            {
                _lightningInvoiceEventService.PublishInvoiceEvent(
                    new LightningInvoicedNotification(LightningInvoiceNotificationTypeEnum.Settled, rHash, invoice.Memo));
            }
            else if(invoice.State == Invoice.Types.InvoiceState.Canceled)
            {
                _lightningInvoiceEventService.PublishInvoiceEvent(
                    new LightningInvoicedNotification(LightningInvoiceNotificationTypeEnum.Cancelled, rHash, invoice.Memo));
            }
        }
    }

    /// <summary>
    /// Sends a periodic check to the LND server to make sure the GRPC connection is still working.
    /// If the check fails
    /// </summary>
    /// <param name="client">The GRPC client to check the connection for.</param>
    /// <param name="cts">The cancellation token source to set if the check fails.</param>
    private async Task CheckLndConnection(Lnrpc.Lightning.LightningClient client, CancellationTokenSource cts)
    {
        var cancellationToken = cts.Token;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var timeoutTokenSource = new CancellationTokenSource();
                timeoutTokenSource.CancelAfter(TimeSpan.FromSeconds(CONNECTION_CHECK_INTERVAL_SECONDS));
                var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, timeoutTokenSource.Token);

                var response = await client.GetInfoAsync(new Lnrpc.GetInfoRequest(), cancellationToken: linkedTokenSource.Token);
            }
            catch(Exception ex)
            {
                _logger.LogWarning($"{nameof(CheckLndConnection)} check LND connection failed. {ex.GetType()} {ex.Message}");
                cts.Cancel();
                break;
            }

            await Task.Delay(CONNECTION_CHECK_INTERVAL_SECONDS * 1000, cancellationToken);
        }
    }
}
