using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Lovecraft.Common.DataContracts;

namespace Lovecraft.WebAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [AllowAnonymous]
    public class HealthController : ControllerBase
    {
        private static readonly DateTime _started = DateTime.UtcNow;

        [HttpGet]
        public ActionResult<HealthInfo> Get()
        {
            var info = new HealthInfo
            {
                Ready = true,
                Version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? string.Empty,
                Uptime = DateTime.UtcNow - _started
            };
            return info;
        }
    }
}
