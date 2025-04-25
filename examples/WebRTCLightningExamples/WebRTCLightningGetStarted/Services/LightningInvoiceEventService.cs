//-----------------------------------------------------------------------------
// Filename: LightningInvoiceEventService.cs
//
// Description: Reactive event hub service for Lightning invoice notifications.
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
using System.Reactive.Subjects;

namespace demo;

public class LightningInvoiceEventService
{
    private readonly ISubject<LightningInvoicedNotification> _invoiceSubject = new Subject<LightningInvoicedNotification>();

    public IObservable<LightningInvoicedNotification> InvoiceStream => _invoiceSubject;

    public void PublishInvoiceEvent(LightningInvoicedNotification notification)
    {
        _invoiceSubject.OnNext(notification);
    }
}
