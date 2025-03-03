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

    private const int FREE_PERIOD_SECONDS = 15;
    private const int TRANSITION_PERIOD_SECONDS = 8;
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
    private DateTimeOffset _startTime = DateTimeOffset.MinValue;

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
        var machine = new StateMachine<PaymentStatus, PaymentTrigger>(PaymentStatus.FreePeriod);

        machine.Configure(PaymentStatus.FreePeriod)
            .PermitIf(PaymentTrigger.Tick, PaymentStatus.FreeTransition,
                () => (DateTimeOffset.Now - _startTime).TotalSeconds >= TRANSITION_PERIOD_SECONDS)
            .IgnoreIf(PaymentTrigger.Tick,
                () => (DateTimeOffset.Now - _startTime).TotalSeconds < TRANSITION_PERIOD_SECONDS);

        machine.Configure(PaymentStatus.FreeTransition)
               .OnEntry(() =>
               {
                   var paymentState = _lightningPaymentService.GetPaymentState();
                   if (!paymentState.HasLightningInvoiceBeenRequested && paymentState.LightningPaymentRequest == null)
                   {
                       _lightningPaymentService.RequestLightningInvoice();
                   }
               })
               .IgnoreIf(PaymentTrigger.Tick,
                 () => (DateTimeOffset.Now - _startTime).TotalSeconds < FREE_PERIOD_SECONDS)
               .PermitIf(PaymentTrigger.Tick, PaymentStatus.FreeWaiting,
                 () => (DateTimeOffset.Now - _startTime).TotalSeconds >= FREE_PERIOD_SECONDS)
               .Permit(PaymentTrigger.InvoicePaid, PaymentStatus.PaidPeriod);

        machine.Configure(PaymentStatus.FreeWaiting)
            .Ignore(PaymentTrigger.Tick)
            .Permit(PaymentTrigger.InvoicePaid, PaymentStatus.PaidPeriod);

        return machine;
    }

    public FrameConfig GetUpdatedFrameConfig()
    {
        if(_startTime == DateTimeOffset.MinValue)
        {
            _startTime = DateTimeOffset.Now;
            _lightningPaymentService.SetInitialFreeSeconds(FREE_PERIOD_SECONDS);
        }

        var paymentState = _lightningPaymentService.GetPaymentState();

        var now = DateTimeOffset.Now;
        int remainingSeconds = paymentState.PaidUntil > now ? (int)paymentState.PaidUntil.Subtract(now).TotalSeconds : 0;
        //remainingSeconds = remainingSeconds < 0 ? 0 : remainingSeconds;

        _paymentStateMachine.Fire(PaymentTrigger.Tick);

        switch (_paymentStateMachine.State)
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
                int opacityFree = (int)(MAX_ALPHA_TRANSPARENCY * ((TRANSITION_PERIOD_SECONDS - remainingSeconds) / (double)TRANSITION_PERIOD_SECONDS));
                return _frameConfig with
                {
                    BorderColour = Color.Orange,
                    Title = TRANSITION_PERIOD_TITLE,
                    IsPaid = paymentState.isPaidPeriod,
                    LightningPaymentRequest = paymentState.LightningPaymentRequest,
                    Opacity = opacityFree,
                    ImagePath = FREE_IMAGE_PATH
                };
            case PaymentStatus.FreeWaiting:
                return _frameConfig with
                {
                    BorderColour = Color.Red,
                    Title = WAITING_FOR_PAYMENT_PERIOD_TITLE,
                    IsPaid = false,
                    LightningPaymentRequest = paymentState.LightningPaymentRequest,
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
                int opacityPaid = (int)(MAX_ALPHA_TRANSPARENCY * ((TRANSITION_PERIOD_SECONDS - remainingSeconds) / (double)TRANSITION_PERIOD_SECONDS));
                return _frameConfig with
                {
                    BorderColour = Color.Orange,
                    Title = TRANSITION_PERIOD_TITLE,
                    IsPaid = true,
                    LightningPaymentRequest = paymentState.LightningPaymentRequest,
                    Opacity = opacityPaid,
                    ImagePath = PAID_IMAGE_PATH
                };
            case PaymentStatus.PaidWaiting:
                return _frameConfig with
                {
                    BorderColour = Color.Red,
                    Title = WAITING_FOR_PAYMENT_PERIOD_TITLE,
                    IsPaid = true,
                    LightningPaymentRequest = paymentState.LightningPaymentRequest,
                    Opacity = MAX_ALPHA_TRANSPARENCY,
                    ImagePath = PAID_IMAGE_PATH
                };
            default:
                return _frameConfig with
                {
                    BorderColour = Color.Red,
                    Title = WAITING_FOR_PAYMENT_PERIOD_TITLE,
                    IsPaid = true,
                    LightningPaymentRequest = paymentState.LightningPaymentRequest,
                    Opacity = MAX_ALPHA_TRANSPARENCY,
                    ImagePath = FREE_IMAGE_PATH
                };
        }
    }

    public FrameConfig GetUpdatedFrameConfigX()
    {
        var paymentState = _lightningPaymentService.GetPaymentState();
        int remainingSeconds = (int)paymentState.PaidUntil.Subtract(DateTimeOffset.Now).TotalSeconds;

        if (paymentState.IsFreePeriod)
        {
            if (remainingSeconds > TRANSITION_PERIOD_SECONDS)
            {
                return _frameConfig with
                {
                    BorderColour = Color.Pink,
                    Title = FREE_PERIOD_TITLE,
                    IsPaid = false,
                    LightningPaymentRequest = null,
                    Opacity = 0,
                    ImagePath = FREE_IMAGE_PATH
                };
            }
            else if (remainingSeconds > 0)
            {
                if (!paymentState.HasLightningInvoiceBeenRequested && paymentState.LightningPaymentRequest == null)
                {
                    _lightningPaymentService.RequestLightningInvoice();
                }

                int opacity = (int)(MAX_ALPHA_TRANSPARENCY * ((TRANSITION_PERIOD_SECONDS - remainingSeconds) / (double)TRANSITION_PERIOD_SECONDS));

                return _frameConfig with
                {
                    BorderColour = Color.Orange,
                    Title = TRANSITION_PERIOD_TITLE,
                    IsPaid = paymentState.isPaidPeriod,
                    LightningPaymentRequest = paymentState.LightningPaymentRequest,
                    Opacity = opacity,
                    ImagePath = FREE_IMAGE_PATH
                };
            }
            else
            {
                return _frameConfig with
                {
                    BorderColour = Color.Red,
                    Title = WAITING_FOR_PAYMENT_PERIOD_TITLE,
                    IsPaid = false,
                    LightningPaymentRequest = paymentState.LightningPaymentRequest,
                    Opacity = MAX_ALPHA_TRANSPARENCY,
                    ImagePath = FREE_IMAGE_PATH
                };
            }
        }
        else if (paymentState.isPaidPeriod)
        {
            if (remainingSeconds > TRANSITION_PERIOD_SECONDS)
            {
                return _frameConfig with
                {
                    BorderColour = Color.Blue,
                    Title = PAID_PERIOD_TITLE,
                    IsPaid = true,
                    LightningPaymentRequest = null,
                    Opacity = 0,
                    ImagePath = PAID_IMAGE_PATH
                };
            }
            else if (remainingSeconds > 0)
            {
                if (!paymentState.HasLightningInvoiceBeenRequested && paymentState.LightningPaymentRequest == null)
                {
                    _lightningPaymentService.RequestLightningInvoice();
                }

                int opacity = (int)(MAX_ALPHA_TRANSPARENCY * ((TRANSITION_PERIOD_SECONDS - remainingSeconds) / (double)TRANSITION_PERIOD_SECONDS));

                return _frameConfig with
                {
                    BorderColour = Color.Orange,
                    Title = TRANSITION_PERIOD_TITLE,
                    IsPaid = true,
                    LightningPaymentRequest = paymentState.LightningPaymentRequest,
                    Opacity = opacity,
                    ImagePath = PAID_IMAGE_PATH
                };
            }
            else
            {
                return _frameConfig with
                {
                    BorderColour = Color.Red,
                    Title = WAITING_FOR_PAYMENT_PERIOD_TITLE,
                    IsPaid = true,
                    LightningPaymentRequest = paymentState.LightningPaymentRequest,
                    Opacity = MAX_ALPHA_TRANSPARENCY,
                    ImagePath = PAID_IMAGE_PATH
                };
            }
        }

        return _frameConfig with
        {
            BorderColour = Color.Red,
            Title = WAITING_FOR_PAYMENT_PERIOD_TITLE,
            IsPaid = true,
            LightningPaymentRequest = paymentState.LightningPaymentRequest,
            Opacity = MAX_ALPHA_TRANSPARENCY,
            ImagePath = FREE_IMAGE_PATH
        };
    }
}
