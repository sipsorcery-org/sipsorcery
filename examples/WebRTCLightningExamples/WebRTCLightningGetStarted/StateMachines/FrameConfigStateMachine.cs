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
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Stateless;

namespace demo;

public enum PaymentStatus
{
    Initial,
    FreePeriod,
    FreeTransition,
    FreeWaiting,
    PaidPeriod,
    PaidTransition,
    PaidWaiting
}

public enum PaymentTrigger
{
    Tick,
    InvoicePaid
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
    private const int MAX_ALPHA_TRANSPARENCY = 200;
    private const string INITIALISING_TITLE = "Initialising";
    private const string FREE_PERIOD_TITLE = "Free Period";
    private const string TRANSITION_PERIOD_TITLE = "Transition Period";
    private const string WAITING_FOR_PAYMENT_PERIOD_TITLE = "Waiting for Payment";
    private const string PAID_PERIOD_TITLE = "Thanks for Paying!";

    private readonly ILogger _logger;
    private readonly ILightningPaymentService _lightningPaymentService;
    private readonly StateMachine<PaymentStatus, PaymentTrigger> _paymentStateMachine;
    private readonly FrameConfig _frameConfig;
    private DateTimeOffset _stateEntryTime = DateTimeOffset.MinValue;

    public FrameConfigStateMachine(
        ILogger<FrameConfigStateMachine> logger,
        ILightningPaymentService lightningPaymentService)
    {
        _logger = logger;
        _lightningPaymentService = lightningPaymentService;
        _paymentStateMachine = CreatePaymentStateMachine();

        _lightningPaymentService.OnLightningInvoiceSettled += () => _paymentStateMachine.Fire(PaymentTrigger.InvoicePaid);

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
               .OnEntry(_lightningPaymentService.RequestLightningInvoice)
               .IgnoreIf(PaymentTrigger.Tick, () => !HasExpired(PaymentStatus.FreeTransition))
               .PermitIf(PaymentTrigger.Tick, PaymentStatus.FreeWaiting, () => HasExpired(PaymentStatus.FreeTransition))
               .Permit(PaymentTrigger.InvoicePaid, PaymentStatus.PaidPeriod);

        machine.Configure(PaymentStatus.FreeWaiting)
            .Ignore(PaymentTrigger.Tick)
            .Permit(PaymentTrigger.InvoicePaid, PaymentStatus.PaidPeriod);

        machine.Configure(PaymentStatus.PaidPeriod)
            .IgnoreIf(PaymentTrigger.Tick, () => !HasExpired(PaymentStatus.PaidPeriod))
            .PermitIf(PaymentTrigger.Tick, PaymentStatus.PaidTransition, () => HasExpired(PaymentStatus.PaidPeriod));

        machine.Configure(PaymentStatus.PaidTransition)
            .OnEntry(_lightningPaymentService.RequestLightningInvoice)
            .IgnoreIf(PaymentTrigger.Tick, () => !HasExpired(PaymentStatus.PaidTransition))
            .PermitIf(PaymentTrigger.Tick, PaymentStatus.PaidWaiting, () => HasExpired(PaymentStatus.PaidTransition))
            .Permit(PaymentTrigger.InvoicePaid, PaymentStatus.PaidPeriod);

        machine.Configure(PaymentStatus.PaidWaiting)
            .Ignore(PaymentTrigger.Tick)
            .Permit(PaymentTrigger.InvoicePaid, PaymentStatus.PaidPeriod);

        return machine;
    }

    public FrameConfig GetUpdatedFrameConfig()
    {
        int secondsRemaining = SecondsRemaining(_stateEntryTime, _paymentStateMachine.State);

        try
        {
            _paymentStateMachine.Fire(PaymentTrigger.Tick);
        }
        catch(Exception excp)
        {
            _logger.LogError("State machine internal exception. {excp}", excp);
        }

        var paymentRequest = _lightningPaymentService.GetLightningPaymentRequest();

        return GetFrameConfig(_paymentStateMachine.State, secondsRemaining, paymentRequest);
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
            PaymentStatus.FreeWaiting => 0,
            PaymentStatus.PaidPeriod => PAID_PERIOD_SECONDS - secondsInCurrentState,
            PaymentStatus.PaidTransition => TRANSITION_PERIOD_SECONDS - secondsInCurrentState,
            PaymentStatus.PaidWaiting => 0,
            _ => 0
        };

        return secondsRemaining < 0 ? 0 : secondsRemaining;
    }

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
                int opacityFree = (int)(MAX_ALPHA_TRANSPARENCY * ((TRANSITION_PERIOD_SECONDS - secondsRemaining) / (double)TRANSITION_PERIOD_SECONDS));
                return _frameConfig with
                {
                    BorderColour = Color.Orange,
                    Title = TRANSITION_PERIOD_TITLE,
                    IsPaid = false,
                    LightningPaymentRequest = lightningPaymentRequest,
                    Opacity = opacityFree,
                    ImagePath = FREE_IMAGE_PATH
                };
            case PaymentStatus.FreeWaiting:
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
                int opacityPaid = (int)(MAX_ALPHA_TRANSPARENCY * ((TRANSITION_PERIOD_SECONDS - secondsRemaining) / (double)TRANSITION_PERIOD_SECONDS));
                return _frameConfig with
                {
                    BorderColour = Color.Orange,
                    Title = TRANSITION_PERIOD_TITLE,
                    IsPaid = true,
                    LightningPaymentRequest = lightningPaymentRequest,
                    Opacity = opacityPaid,
                    ImagePath = PAID_IMAGE_PATH
                };
            case PaymentStatus.PaidWaiting:
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
