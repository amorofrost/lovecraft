namespace Lovecraft.Common.DTOs.Admin;

public class ContainerInfrastructureDto
{
    public string Name { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public double UptimeSeconds { get; set; }
    public DateTime AppStartedAtUtc { get; set; }
    public double AppUptimeSeconds { get; set; }
    public double CpuPercent { get; set; }
    public long MemoryUsageBytes { get; set; }
    public long MemoryLimitBytes { get; set; }
}

public class InfrastructureStatusDto
{
    public DateTime GeneratedAtUtc { get; set; }
    public List<ContainerInfrastructureDto> Containers { get; set; } = new();
}

