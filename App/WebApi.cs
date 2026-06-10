using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Embedded;

namespace SteamNetFix.App;

// 源码生成器需要的Json上下文
[JsonSerializable(typeof(ToggleRequest))]
[JsonSerializable(typeof(ConfigUpdateRequest))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(AccelerationStatusDto))]
[JsonSerializable(typeof(EnabledServiceDto))]
[JsonSerializable(typeof(ServiceListItemDto))]
[JsonSerializable(typeof(ServiceDetailDto))]
[JsonSerializable(typeof(DomainDetailDto))]
[JsonSerializable(typeof(ToggleResponse))]
[JsonSerializable(typeof(MessageResponse))]
[JsonSerializable(typeof(SpeedTestResultsDto))]
[JsonSerializable(typeof(HostsInfoDto))]
[JsonSerializable(typeof(StartResultDto))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(List<ServiceListItemDto>))]
[JsonSerializable(typeof(List<EnabledServiceDto>))]
[JsonSerializable(typeof(List<DomainDetailDto>))]
[JsonSerializable(typeof(TrafficStatsDto))]
[JsonSerializable(typeof(PingResponse))]
internal partial class AppJsonContext : JsonSerializerContext { }

/// <summary>
/// Web API 服务 - 提供REST接口和Web UI
/// </summary>
public static class WebApi
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static WebApplication CreateWebApp(AcceleratorEngine engine, int port)
    {
        var builder = WebApplication.CreateBuilder();

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
        });

        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        var app = builder.Build();

        // 静态文件（Web UI）- 优先使用磁盘文件，其次使用嵌入式资源
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        IFileProvider fileProvider;
        
        if (Directory.Exists(wwwroot))
        {
            // 开发模式：直接从磁盘读取
            fileProvider = new PhysicalFileProvider(wwwroot);
        }
        else
        {
            // 单文件发布模式：从嵌入式资源读取
            fileProvider = new EmbeddedFileProvider(
                typeof(WebApi).Assembly, "SteamNetFix.wwwroot");
        }

        // UseDefaultFiles: 让 "/" 自动映射到 "index.html"
        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = fileProvider,
            DefaultFileNames = { "index.html" },
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider,
        });

        // CORS for local development
        app.Use(async (context, next) =>
        {
            context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            context.Response.Headers.Append("Access-Control-Allow-Headers", "Content-Type");
            
            if (context.Request.Method == "OPTIONS")
            {
                context.Response.StatusCode = 200;
                return;
            }
            await next();
        });

        // WebSocket 支持（用于前端检测客户端是否存活）
        app.UseWebSockets();
        app.Map("/ws", async (HttpContext context) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }

            using var ws = await context.WebSockets.AcceptWebSocketAsync();
            var buffer = new byte[256];

            try
            {
                // 连接保持：每隔5秒发一次心跳，同时检测客户端是否断开
                while (ws.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    // 发送心跳
                    var heartbeat = System.Text.Encoding.UTF8.GetBytes("hb");
                    await ws.SendAsync(
                        new ArraySegment<byte>(heartbeat),
                        System.Net.WebSockets.WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);

                    // 等待5秒，期间如果客户端发消息则读取（可忽略内容）
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        while (!cts.Token.IsCancellationRequested)
                        {
                            var result = await ws.ReceiveAsync(
                                new ArraySegment<byte>(buffer), cts.Token);
                            if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                                return;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // 超时，继续发心跳
                    }
                }
            }
            catch
            {
                // 连接异常断开，静默处理
            }
        });

        MapApiRoutes(app, engine);

        return app;
    }

    private static void MapApiRoutes(WebApplication app, AcceleratorEngine engine)
    {
        var api = app.MapGroup("/api");

        // 获取状态
        api.MapGet("/status", () => Results.Json(engine.GetStatus(), JsonOptions));

        // 获取可用服务列表
        api.MapGet("/services", () =>
        {
            var services = engine.GetAvailableServices();
            return Results.Json(services.Select(s => new ServiceListItemDto(
                s.Id, s.Name, s.Description, s.Icon, s.Domains.Count,
                engine.Config.EnabledServices.Contains(s.Id)
            )).ToList(), JsonOptions);
        });

        // 获取服务详情
        api.MapGet("/services/{id}", (string id) =>
        {
            var service = BuiltinRules.GetById(id);
            if (service == null)
                return Results.NotFound(new ErrorResponse("服务未找到"));
            
            return Results.Json(new ServiceDetailDto(
                service.Id, service.Name, service.Description, service.Icon,
                engine.Config.EnabledServices.Contains(service.Id),
                service.Domains.Select(d => new DomainDetailDto(
                    d.Domain, d.CandidateIps,
                    engine.Config.SelectedIps.GetValueOrDefault(d.Domain)
                )).ToList()
            ), JsonOptions);
        });

        // 启用/禁用服务
        api.MapPost("/services/{id}/toggle", (string id, ToggleRequest? req) =>
        {
            var service = BuiltinRules.GetById(id);
            if (service == null)
                return Results.NotFound(new ErrorResponse("服务未找到"));
            
            var enable = req?.Enabled ?? !engine.Config.EnabledServices.Contains(id);
            engine.ToggleService(id, enable);
            
            return Results.Json(new ToggleResponse(enable));
        });

        // 开始加速
        api.MapPost("/start", async () =>
        {
            var (success, message) = await engine.StartAccelerateAsync();
            var status = engine.GetStatus();
            return Results.Json(new StartResultDto(success, message, status), JsonOptions);
        });

        // 停止加速
        api.MapPost("/stop", async () =>
        {
            await engine.StopAccelerateAsync();
            return Results.Json(engine.GetStatus(), JsonOptions);
        });

        // 测速
        api.MapPost("/speedtest", async () =>
        {
            _ = Task.Run(() => engine.RunSpeedTestAsync());
            return Results.Json(new MessageResponse("测速已开始"));
        });

        // 重新测速
        api.MapPost("/retest", async () =>
        {
            _ = Task.Run(() => engine.RetestAsync());
            return Results.Json(new MessageResponse("重新测速已开始"));
        });

        // 获取测速结果
        api.MapGet("/speedtest/results", () =>
        {
            return Results.Json(new SpeedTestResultsDto(
                engine.Config.SelectedIps,
                engine.Config.LastSpeedTest
            ), JsonOptions);
        });

        // 获取配置
        api.MapGet("/config", () => Results.Json(engine.Config, JsonOptions));

        // 更新配置
        api.MapPost("/config", async (HttpRequest request) =>
        {
            try
            {
                var body = await request.ReadFromJsonAsync<ConfigUpdateRequest>();
                if (body == null)
                    return Results.BadRequest(new ErrorResponse("无效的请求"));

                if (body.WebPort.HasValue) engine.Config.WebPort = body.WebPort.Value;
                if (body.ProxyPort.HasValue) engine.Config.ProxyPort = body.ProxyPort.Value;
                if (body.ProxyEnabled.HasValue) engine.Config.ProxyEnabled = body.ProxyEnabled.Value;
                if (body.AutoTestIntervalHours.HasValue) engine.Config.AutoTestIntervalHours = body.AutoTestIntervalHours.Value;
                if (body.AutoStart.HasValue) engine.Config.AutoStart = body.AutoStart.Value;
                if (body.MinimizeToTray.HasValue) engine.Config.MinimizeToTray = body.MinimizeToTray.Value;
                if (body.CustomDns != null) engine.Config.CustomDns = body.CustomDns;
                if (body.ShowHttpLogs.HasValue) engine.Config.ShowHttpLogs = body.ShowHttpLogs.Value;
                if (body.SetSystemProxy.HasValue) engine.Config.SetSystemProxy = body.SetSystemProxy.Value;

                engine.Config.Save();
                return Results.Json(engine.Config, JsonOptions);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        // 获取hosts内容
        api.MapGet("/hosts", () =>
        {
            return Results.Json(new HostsInfoDto(
                HostsManager.GetHostsFilePath(),
                engine.GetHostsEntries(),
                HostsManager.IsRunningAsAdmin()
            ), JsonOptions);
        });

        // 刷新DNS
        api.MapPost("/dns/flush", () =>
        {
            engine.FlushDns();
            return Results.Json(new MessageResponse("DNS缓存已刷新"));
        });

        // 获取日志
        api.MapGet("/logs", (HttpRequest request) =>
        {
            return Results.Json(LogBuffer.GetRecentLogs(), JsonOptions);
        });

        // 获取流量统计
        api.MapGet("/stats/traffic", () =>
        {
            var stats = engine.GetTrafficStats();
            if (stats == null)
                return Results.Json(new TrafficStatsDto(0, 0, 0, 0, new List<string>()), JsonOptions);

            // 不显示HTTP日志时，清空RecentLogs以节省带宽
            if (!engine.Config.ShowHttpLogs)
                stats = stats with { RecentLogs = new List<string>() };

            return Results.Json(stats, JsonOptions);
        });

        // 健康检查（用于前端检测客户端是否存活）
        api.MapGet("/ping", () => Results.Json(new PingResponse(DateTime.UtcNow)));
    }
}

// 请求类型
public record ToggleRequest(bool? Enabled);
public record ConfigUpdateRequest(
    int? WebPort,
    int? ProxyPort,
    bool? ProxyEnabled,
    int? AutoTestIntervalHours,
    bool? AutoStart,
    bool? MinimizeToTray,
    string? CustomDns,
    bool? ShowHttpLogs,
    bool? SetSystemProxy);

/// <summary>
/// 日志缓冲
/// </summary>
public static class LogBuffer
{
    private static readonly Queue<string> _logs = new();
    private static readonly object _lock = new();
    private const int MaxLogs = 500;

    public static void Add(string message)
    {
        lock (_lock)
        {
            _logs.Enqueue(message);
            while (_logs.Count > MaxLogs)
                _logs.Dequeue();
        }
    }

    public static List<string> GetRecentLogs()
    {
        lock (_lock)
        {
            return _logs.ToList();
        }
    }
}
