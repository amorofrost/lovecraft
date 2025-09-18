using System;

namespace Lovecraft.Common.DataContracts
{
    public class HealthInfo
    {
        public bool Ready { get; set; }
        public string Version { get; set; } = string.Empty;
        public TimeSpan Uptime { get; set; }
    }
}
