//-----------------------------------------------------------------------------
// Filename: LightningPaymentState.cs
//
// Description: An immutable data structure to represent the payment state for
// a paid WebRTC connection.
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

namespace demo;

public record LightningPaymentState(
    DateTimeOffset PaidUntil,
    bool IsFreePeriod,
    bool isPaidPeriod,
    bool HasLightningInvoiceBeenRequested,
    string? LightningInvoiceRHash,
    string? LightningPaymentRequest,
    int InvoicePurchaseSeconds);
