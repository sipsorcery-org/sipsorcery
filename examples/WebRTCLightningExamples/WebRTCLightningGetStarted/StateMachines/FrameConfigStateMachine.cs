//-----------------------------------------------------------------------------
// Filename: FrameConfigStateMachine.cs
//
// Description: State machine for managing the webrtc connection frame config.
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
using System.Drawing;
using Microsoft.Extensions.Logging;
using Stateless;

namespace demo;

public enum PaymentStatus
{
    Initial,
    FreePeriod,
    FreeTransition,
    FreeWaitingForPayment,
    PaidPeriod,
    PaidTransition,
    PaidWaitingForPayment
}

public enum PaymentTrigger
{
    Tick,
    InvoicePaid,
    InvoiceExpired
}

public interface IFrameConfigStateMachine
{
    FrameConfig GetUpdatedFrameConfig();
}

public class FrameConfigStateMachine : IFrameConfigStateMachine
{
    public const string FREE_IMAGE_PATH = "media/simple_flower.jpg";
    private const string PAID_IMAGE_PATH = "media/real_flowers.jpg";

    private const int FREE_PERIOD_SECONDS = 8;
    private const int TRANSITION_PERIOD_SECONDS = 7;
    private const int PAID_PERIOD_SECONDS = 10;
    private const int PAID_WAITING_SECONDS = 15;
    private const int MAX_ALPHA_TRANSPARENCY = 200;
    private const string INITIALISING_TITLE = "Initialising";
    private const string FREE_PERIOD_TITLE = "Free Period";
    private const string TRANSITION_PERIOD_TITLE = "Transition Period";
    private const string WAITING_FOR_PAYMENT_PERIOD_TITLE = "Waiting for Payment";
    private const string PAID_PERIOD_TITLE = "Thanks for Paying!";
    private const string INVOICE_DESCRIPTION = "Pay for more time!";

    private readonly ILogger _logger;
    private readonly ILightningPaymentService _lightningPaymentService;
    private readonly StateMachine<PaymentStatus, PaymentTrigger> _paymentStateMachine;
    private readonly FrameConfig _frameConfig;

    private DateTimeOffset _stateEntryTime = DateTimeOffset.MinValue;
    private string? _lightningPaymentRequest = null;

    public FrameConfigStateMachine(
        ILogger<FrameConfigStateMachine> logger,
        ILightningPaymentService lightningPaymentService)
    {
        _logger = logger;
        _lightningPaymentService = lightningPaymentService;
        _paymentStateMachine = CreatePaymentStateMachine();

        _frameConfig = new FrameConfig(DateTimeOffset.Now, null, 0, Color.Green, INITIALISING_TITLE, false, FREE_IMAGE_PATH);
    }

    private StateMachine<PaymentStatus, PaymentTrigger> CreatePaymentStateMachine()
    {
        var machine = new StateMachine<PaymentStatus, PaymentTrigger>(PaymentStatus.Initial);

        machine.OnTransitioned(t =>
        {
            _stateEntryTime = DateTimeOffset.Now;
            _logger.LogDebug($"Payment state machine transitioned from {t.Source} to {t.Destination}.");
        });

        machine.Configure(PaymentStatus.Initial)
             .PermitIf(PaymentTrigger.Tick, PaymentStatus.FreePeriod);

        machine.Configure(PaymentStatus.FreePeriod)
            .IgnoreIf(PaymentTrigger.Tick, () => !HasExpired(PaymentStatus.FreePeriod))
            .PermitIf(PaymentTrigger.Tick, PaymentStatus.FreeTransition, () => HasExpired(PaymentStatus.FreePeriod));

        machine.Configure(PaymentStatus.FreeTransition)
            .OnEntry(() => _lightningPaymentService.RequestLightningInvoice(INVOICE_DESCRIPTION))
            .IgnoreIf(PaymentTrigger.Tick, () => !HasExpired(PaymentStatus.FreeTransition))
            .PermitIf(PaymentTrigger.Tick, PaymentStatus.FreeWaitingForPayment, () => HasExpired(PaymentStatus.FreeTransition))
            .PermitReentry(PaymentTrigger.InvoiceExpired)
            .Permit(PaymentTrigger.InvoicePaid, PaymentStatus.PaidPeriod);

        machine.Configure(PaymentStatus.FreeWaitingForPayment)
            .Ignore(PaymentTrigger.Tick)
            .Permit(PaymentTrigger.InvoicePaid, PaymentStatus.PaidPeriod);

        machine.Configure(PaymentStatus.PaidPeriod)
            .IgnoreIf(PaymentTrigger.Tick, () => !HasExpired(PaymentStatus.PaidPeriod))
            .PermitIf(PaymentTrigger.Tick, PaymentStatus.PaidTransition, () => HasExpired(PaymentStatus.PaidPeriod));

        machine.Configure(PaymentStatus.PaidTransition)
            .OnEntry(() => _lightningPaymentService.RequestLightningInvoice(INVOICE_DESCRIPTION))
            .IgnoreIf(PaymentTrigger.Tick, () => !HasExpired(PaymentStatus.PaidTransition))
            .PermitIf(PaymentTrigger.Tick, PaymentStatus.PaidWaitingForPayment, () => HasExpired(PaymentStatus.PaidTransition))
            .PermitReentry(PaymentTrigger.InvoiceExpired)
            .Permit(PaymentTrigger.InvoicePaid, PaymentStatus.PaidPeriod);

        machine.Configure(PaymentStatus.PaidWaitingForPayment)
            .IgnoreIf(PaymentTrigger.Tick, () => !HasExpired(PaymentStatus.PaidWaitingForPayment))
            .PermitIf(PaymentTrigger.Tick, PaymentStatus.FreePeriod, () => HasExpired(PaymentStatus.PaidWaitingForPayment))
            .Permit(PaymentTrigger.InvoicePaid, PaymentStatus.PaidPeriod);

        _lightningPaymentService.OnLightningInvoiceSettled += () => _paymentStateMachine.Fire(PaymentTrigger.InvoicePaid);
        _lightningPaymentService.OnLightningInvoiceExpired += () => _paymentStateMachine.Fire(PaymentTrigger.InvoiceExpired);
        _lightningPaymentService.OnLightningPaymentRequestGenerated += (payreq) => _lightningPaymentRequest = payreq;

        return machine;
    }

    public FrameConfig GetUpdatedFrameConfig()
    {
        try
        {
            _paymentStateMachine.Fire(PaymentTrigger.Tick);
        }
        catch(Exception excp)
        {
            _logger.LogError("State machine internal exception. {excp}", excp);
        }

        int secondsRemaining = SecondsRemaining(_stateEntryTime, _paymentStateMachine.State);

        return GetFrameConfig(_paymentStateMachine.State, secondsRemaining, _lightningPaymentRequest);
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

    private FrameConfig GetFrameConfig(PaymentStatus paymentStatus, int secondsRemaining, string? lightningPaymentRequest)
    {
        switch (paymentStatus)
        {
            case PaymentStatus.FreePeriod:
                return _frameConfig with
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
                return _frameConfig with
                {
                    BorderColour = Color.Orange,
                    Title = TRANSITION_PERIOD_TITLE,
                    IsPaid = false,
                    LightningPaymentRequest = lightningPaymentRequest,
                    Opacity = opacityFree,
                    ImagePath = FREE_IMAGE_PATH
                };
            case PaymentStatus.FreeWaitingForPayment:
                return _frameConfig with
                {
                    BorderColour = Color.Red,
                    Title = WAITING_FOR_PAYMENT_PERIOD_TITLE,
                    IsPaid = false,
                    LightningPaymentRequest = lightningPaymentRequest,
                    Opacity = MAX_ALPHA_TRANSPARENCY,
                    ImagePath = FREE_IMAGE_PATH
                };
            case PaymentStatus.PaidPeriod:
                return _frameConfig with
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
                return _frameConfig with
                {
                    BorderColour = Color.Orange,
                    Title = TRANSITION_PERIOD_TITLE,
                    IsPaid = true,
                    LightningPaymentRequest = lightningPaymentRequest,
                    Opacity = opacityPaid,
                    ImagePath = PAID_IMAGE_PATH
                };
            case PaymentStatus.PaidWaitingForPayment:
                return _frameConfig with
                {
                    BorderColour = Color.Red,
                    Title = WAITING_FOR_PAYMENT_PERIOD_TITLE,
                    IsPaid = true,
                    LightningPaymentRequest = lightningPaymentRequest,
                    Opacity = MAX_ALPHA_TRANSPARENCY,
                    ImagePath = PAID_IMAGE_PATH
                };
            default:
                return _frameConfig with
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
