using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace demo;

[ApiController]
[Route("pay")]
public class PayController : ControllerBase
{
    private readonly ILogger<PayController> _logger;
    private readonly PeerConnectionPayState _peerConnectionPayState;

    public PayController(
        ILogger<PayController> logger,
        PeerConnectionPayState peerConnectionPayState)
    {
        _logger = logger;
        _peerConnectionPayState = peerConnectionPayState;
    }

    //[HttpGet("{id}")]
    //public IActionResult Get(string id)
    //{
    //    _logger.LogDebug($"pay id={id}");

    //    return _peerConnectionPayState.TrySetPaid(id) ?
    //        Ok() : BadRequest();
    //}

    //[HttpGet("list")]
    //public string List()
    //{
    //    _logger.LogDebug("pay/list");

    //    //return WebRtcDaemon.GetInstance().List();

    //    return string.Empty;
    //}
}
