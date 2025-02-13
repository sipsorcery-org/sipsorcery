using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace demo;

[ApiController]
[Route("pay")]
public class PayController : ControllerBase
{
    private readonly ILogger<PayController> _logger;

    public PayController(ILogger<PayController> logger)
    {
        _logger = logger;
    }

    [HttpGet("{id}")]
    public int Get(string id)
    {
        _logger.LogDebug($"pay id={id}");

        //WebRtcDaemon.GetInstance().Pay(id);

        return 0;
    }

    //[HttpGet("list")]
    //public string List()
    //{
    //    _logger.LogDebug("pay/list");

    //    //return WebRtcDaemon.GetInstance().List();

    //    return string.Empty;
    //}
}
