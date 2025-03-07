//-----------------------------------------------------------------------------
// Filename: PaymentStatus.cs
//
// Description: The payment status for the payment state machine.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 05 Mar 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

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
