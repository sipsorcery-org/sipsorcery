//-----------------------------------------------------------------------------
// Filename: PaymentStateMachine.cs
//
// Description: State machine for managing the payment state of a paid webrtc
// connection which dictates the bitmap source for the video stream.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 01 Mar 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using SixLabors.ImageSharp;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stateless;

namespace demo;

public interface IFrameConfigStateMachine
{
    PaidVideoFrameConfig GetPaidFrameConfig();
}

public class PaymentStateMachine : IFrameConfigStateMachine
{
    public const string FREE_IMAGE_PATH = "media/simple_flower.jpg";
    private const string PAID_IMAGE_PATH = "media/real_flowers.jpg";
    private const string QR_CODE_LOGO_PATH = "media/logo.png";

    private const int FREE_PERIOD_SECONDS = 8;
    private const int TRANSITION_PERIOD_SECONDS = 7;
    private const int PAID_PERIOD_SECONDS = 10;
    private const int PAID_WAITING_SECONDS = 15;
    private const int LIGHNTING_INVOICE_EXPIRY_SECONDS = 20;

    private const string INITIALISING_TITLE = "Initialising";
    private const string FREE_PERIOD_TITLE = "Free Period";
    private const string TRANSITION_PERIOD_TITLE = "Transition Period";
    private const string WAITING_FOR_PAYMENT_PERIOD_TITLE = "Waiting for Payment";
    private const string PAID_PERIOD_TITLE = "Thanks for Paying!";
    private const string INVOICE_DESCRIPTION = "Pay for more time!";

    private const int MAX_ALPHA_TRANSPARENCY = 200;

    private readonly ILogger _logger;
    private readonly ILightningPaymentService _lightningPaymentService;
    private readonly StateMachine<PaymentStatus, PaymentStateTrigger> _paymentStateMachine;
    private readonly PaidVideoFrameConfig _paidFrameConfig;

    private DateTimeOffset _stateEntryTime = DateTimeOffset.MinValue;
    private string? _lightningPaymentRequest = null;
    private Timer? _lightingInvoiceExpiredTimer = null;

    public PaymentStateMachine(
        ILogger<PaymentStateMachine> logger,
        ILightningPaymentService lightningPaymentService)
    {
        _logger = logger;
        _lightningPaymentService = lightningPaymentService;
        _paymentStateMachine = CreatePaymentStateMachine();

        _paidFrameConfig = new PaidVideoFrameConfig(DateTimeOffset.Now, null, 0, Color.Green, INITIALISING_TITLE, false, FREE_IMAGE_PATH, QR_CODE_LOGO_PATH);
    }

    private StateMachine<PaymentStatus, PaymentStateTrigger> CreatePaymentStateMachine()
    {
        var machine = new StateMachine<PaymentStatus, PaymentStateTrigger>(PaymentStatus.Initial);

        machine.OnTransitioned(t =>
        {
            _stateEntryTime = DateTimeOffset.Now;
            _logger.LogDebug($"Payment state machine transitioned from {t.Source} to {t.Destination}.");
        });

        machine.Configure(PaymentStatus.Initial)
             .PermitIf(PaymentStateTrigger.Tick, PaymentStatus.FreePeriod);

        machine.Configure(PaymentStatus.FreePeriod)
            .IgnoreIf(PaymentStateTrigger.Tick, () => !HasExpired(PaymentStatus.FreePeriod))
            .PermitIf(PaymentStateTrigger.Tick, PaymentStatus.FreeTransition, () => HasExpired(PaymentStatus.FreePeriod))
            .Ignore(PaymentStateTrigger.InvoiceExpired);

        machine.Configure(PaymentStatus.FreeTransition)
            .OnEntry(() => _lightningPaymentService.RequestLightningInvoice(INVOICE_DESCRIPTION, LIGHNTING_INVOICE_EXPIRY_SECONDS))
            .IgnoreIf(PaymentStateTrigger.Tick, () => !HasExpired(PaymentStatus.FreeTransition))
            .PermitIf(PaymentStateTrigger.Tick, PaymentStatus.FreeWaitingForPayment, () => HasExpired(PaymentStatus.FreeTransition))
            .Permit(PaymentStateTrigger.InvoiceExpired, PaymentStatus.FreePeriod)
            .Permit(PaymentStateTrigger.InvoicePaid, PaymentStatus.PaidPeriod);

        machine.Configure(PaymentStatus.FreeWaitingForPayment)
            .Ignore(PaymentStateTrigger.Tick)
            .Permit(PaymentStateTrigger.InvoiceExpired, PaymentStatus.FreePeriod)
            .Permit(PaymentStateTrigger.InvoicePaid, PaymentStatus.PaidPeriod);

        machine.Configure(PaymentStatus.PaidPeriod)
            .IgnoreIf(PaymentStateTrigger.Tick, () => !HasExpired(PaymentStatus.PaidPeriod))
            .PermitIf(PaymentStateTrigger.Tick, PaymentStatus.PaidTransition, () => HasExpired(PaymentStatus.PaidPeriod))
            .Ignore(PaymentStateTrigger.InvoiceExpired);

        machine.Configure(PaymentStatus.PaidTransition)
            .OnEntry(() => _lightningPaymentService.RequestLightningInvoice(INVOICE_DESCRIPTION, LIGHNTING_INVOICE_EXPIRY_SECONDS))
            .IgnoreIf(PaymentStateTrigger.Tick, () => !HasExpired(PaymentStatus.PaidTransition))
            .PermitIf(PaymentStateTrigger.Tick, PaymentStatus.PaidWaitingForPayment, () => HasExpired(PaymentStatus.PaidTransition))
            .Permit(PaymentStateTrigger.InvoiceExpired, PaymentStatus.FreePeriod)
            .Permit(PaymentStateTrigger.InvoicePaid, PaymentStatus.PaidPeriod);

        machine.Configure(PaymentStatus.PaidWaitingForPayment)
            .IgnoreIf(PaymentStateTrigger.Tick, () => !HasExpired(PaymentStatus.PaidWaitingForPayment))
            .Permit(PaymentStateTrigger.InvoiceExpired, PaymentStatus.FreePeriod)
            .Permit(PaymentStateTrigger.InvoicePaid, PaymentStatus.PaidPeriod);

        _lightningPaymentService.OnLightningInvoiceSettled += async () =>
        {
            await ResetLightningInvoiceState();
            _paymentStateMachine.Fire(PaymentStateTrigger.InvoicePaid);
        };
        _lightningPaymentService.OnLightningInvoiceExpired += async () =>
        {
            await ResetLightningInvoiceState();
            _paymentStateMachine.Fire(PaymentStateTrigger.InvoiceExpired);
        };
        _lightningPaymentService.OnLightningPaymentRequestGenerated += async (payreq) =>
        {
            await ResetLightningInvoiceState();

            _lightningPaymentRequest = payreq;

            _lightingInvoiceExpiredTimer = new Timer(_ =>
            {
                _logger.LogDebug("Lightning payment request timer expired.");
                _paymentStateMachine.Fire(PaymentStateTrigger.InvoiceExpired);

            }, null, LIGHNTING_INVOICE_EXPIRY_SECONDS * 1000, Timeout.Infinite);
        };

        return machine;
    }

    public PaidVideoFrameConfig GetPaidFrameConfig()
    {
        try
        {
            _paymentStateMachine.Fire(PaymentStateTrigger.Tick);
        }
        catch(Exception excp)
        {
            _logger.LogError("State machine internal exception. {excp}", excp);
        }

        int secondsRemaining = SecondsRemaining(_stateEntryTime, _paymentStateMachine.State);

        return GetFrameConfig(_paymentStateMachine.State, secondsRemaining, _lightningPaymentRequest);
    }

    private async Task ResetLightningInvoiceState()
    {
        if (_lightingInvoiceExpiredTimer != null)
        {
            await _lightingInvoiceExpiredTimer.DisposeAsync();
        }

        _lightningPaymentRequest = null;
    }

    private bool HasExpired(PaymentStatus currentState)
        => SecondsRemaining(_stateEntryTime, currentState) <= 0;
    
    private int SecondsRemaining(DateTimeOffset startTime, PaymentStatus currentState)
    {
        int secondsInCurrentState = (int)(DateTimeOffset.Now - _stateEntryTime).TotalSeconds;

        int secondsRemaining = currentState switch
        {
            PaymentStatus.FreePeriod => FREE_PERIOD_SECONDS - secondsInCurrentState,
            PaymentStatus.FreeTransition => TRANSITION_PERIOD_SECONDS - secondsInCurrentState,
            PaymentStatus.FreeWaitingForPayment => 0,
            PaymentStatus.PaidPeriod => PAID_PERIOD_SECONDS - secondsInCurrentState,
            PaymentStatus.PaidTransition => TRANSITION_PERIOD_SECONDS - secondsInCurrentState,
            PaymentStatus.PaidWaitingForPayment => PAID_WAITING_SECONDS - secondsInCurrentState,
            _ => 0
        };

        return secondsRemaining < 0 ? 0 : secondsRemaining;
    }

    private int GetOpacity(int maxAlphaTransparency, int periodSeconds, int secondsRemaining)
        =>
            periodSeconds == 0 || secondsRemaining == 0 ? maxAlphaTransparency :
            (int)(maxAlphaTransparency * ((periodSeconds - secondsRemaining) / (double)periodSeconds));

    private PaidVideoFrameConfig GetFrameConfig(PaymentStatus paymentStatus, int secondsRemaining, string? lightningPaymentRequest)
    {
        switch (paymentStatus)
        {
            case PaymentStatus.FreePeriod:
                return _paidFrameConfig with
                {
                    BorderColour = Color.Pink,
                    Title = FREE_PERIOD_TITLE,
                    IsPaid = false,
                    LightningPaymentRequest = null,
                    Opacity = 0,
                    ImagePath = FREE_IMAGE_PATH
                };
            case PaymentStatus.FreeTransition:
                int opacityFree = GetOpacity(MAX_ALPHA_TRANSPARENCY, TRANSITION_PERIOD_SECONDS, secondsRemaining);
                return _paidFrameConfig with
                {
                    BorderColour = Color.Orange,
                    Title = TRANSITION_PERIOD_TITLE,
                    IsPaid = false,
                    LightningPaymentRequest = lightningPaymentRequest,
                    Opacity = opacityFree,
                    ImagePath = FREE_IMAGE_PATH
                };
            case PaymentStatus.FreeWaitingForPayment:
                return _paidFrameConfig with
                {
                    BorderColour = Color.Red,
                    Title = WAITING_FOR_PAYMENT_PERIOD_TITLE,
                    IsPaid = false,
                    LightningPaymentRequest = lightningPaymentRequest,
                    Opacity = MAX_ALPHA_TRANSPARENCY,
                    ImagePath = FREE_IMAGE_PATH
                };
            case PaymentStatus.PaidPeriod:
                return _paidFrameConfig with
                {
                    BorderColour = Color.Blue,
                    Title = PAID_PERIOD_TITLE,
                    IsPaid = true,
                    LightningPaymentRequest = null,
                    Opacity = 0,
                    ImagePath = PAID_IMAGE_PATH
                };
            case PaymentStatus.PaidTransition:
                int opacityPaid = GetOpacity(MAX_ALPHA_TRANSPARENCY, TRANSITION_PERIOD_SECONDS, secondsRemaining);
                return _paidFrameConfig with
                {
                    BorderColour = Color.Orange,
                    Title = TRANSITION_PERIOD_TITLE,
                    IsPaid = true,
                    LightningPaymentRequest = lightningPaymentRequest,
                    Opacity = opacityPaid,
                    ImagePath = PAID_IMAGE_PATH
                };
            case PaymentStatus.PaidWaitingForPayment:
                return _paidFrameConfig with
                {
                    BorderColour = Color.Red,
                    Title = WAITING_FOR_PAYMENT_PERIOD_TITLE,
                    IsPaid = true,
                    LightningPaymentRequest = lightningPaymentRequest,
                    Opacity = MAX_ALPHA_TRANSPARENCY,
                    ImagePath = PAID_IMAGE_PATH
                };
            default:
                return _paidFrameConfig with
                {
                    BorderColour = Color.Red,
                    Title = WAITING_FOR_PAYMENT_PERIOD_TITLE,
                    IsPaid = true,
                    LightningPaymentRequest = lightningPaymentRequest,
                    Opacity = MAX_ALPHA_TRANSPARENCY,
                    ImagePath = FREE_IMAGE_PATH
                };
        }
    }
}
