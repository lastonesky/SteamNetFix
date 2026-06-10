namespace SteamNetFix.App;

// ── 加速状态 ──

public record AccelerationStatusDto(
    bool IsAccelerating,
    int EnabledServiceCount,
    List<EnabledServiceDto> EnabledServices,
    int SelectedIpCount,
    Dictionary<string, string> SelectedIps,
    DateTime? LastSpeedTest,
    bool ProxyEnabled,
    int ProxyPort,
    bool ProxyRunning,
    long ProxyConnections,
    bool IsAdmin,
    string HostsPath,
    int WebPort,
    int AutoTestIntervalHours
);

public record EnabledServiceDto(string Id, string Name, string Icon);

// ── 服务列表 ──

public record ServiceListItemDto(
    string Id,
    string Name,
    string Description,
    string Icon,
    int DomainCount,
    bool Enabled
);

// ── 服务详情 ──

public record ServiceDetailDto(
    string Id,
    string Name,
    string Description,
    string Icon,
    bool Enabled,
    List<DomainDetailDto> Domains
);

public record DomainDetailDto(
    string Domain,
    List<string> CandidateIps,
    string? SelectedIp
);

// ── 操作响应 ──

public record ToggleResponse(bool Enabled);

public record MessageResponse(string Message);

// ── 其它 ──

public record SpeedTestResultsDto(
    Dictionary<string, string> SelectedIps,
    DateTime? LastTest
);

public record HostsInfoDto(
    string Path,
    Dictionary<string, string> Entries,
    bool IsAdmin
);

public record ErrorResponse(string Error);

// ── 流量统计 ──

public record TrafficStatsDto(
    long TotalConnections,
    long ActiveConnections,
    long TotalBytesReceived,
    long TotalBytesSent,
    List<string> RecentLogs
);

// ── 健康检查 ──

public record PingResponse(DateTime ServerTime);
