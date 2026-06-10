namespace SteamNetFix.App;

/// <summary>
/// 加速服务定义
/// </summary>
public class ServiceDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";
    public List<DomainRule> Domains { get; set; } = new();
}

/// <summary>
/// 域名加速规则
/// </summary>
public class DomainRule
{
    /// <summary>需要加速的域名</summary>
    public string Domain { get; set; } = "";
    
    /// <summary>候选CDN IP列表</summary>
    public List<string> CandidateIps { get; set; } = new();
    
    /// <summary>TCP端口（用于测速）</summary>
    public int Port { get; set; } = 443;
    
    /// <summary>是否通过代理转发（而非hosts）</summary>
    public bool UseProxy { get; set; }
    
    /// <summary>代理转发目标IP（如需要）</summary>
    public string? ProxyTargetIp { get; set; }
    
    /// <summary>SNI覆写（用于HTTPS代理）</summary>
    public string? SniOverride { get; set; }
}

/// <summary>
/// 内置加速规则
/// </summary>
public static class BuiltinRules
{
    public static List<ServiceDefinition> GetAll()
    {
        return new List<ServiceDefinition>
        {
            CreateSteamRules(),
            CreateGithubRules(),
            CreateGoogleTranslateRules(),
            CreateOriginRules(),
            CreateUplayRules(),
            CreateTwitchRules(),
            CreateSpotifyRules(),
            CreateRobloxRules(),
            CreateModIoRules(),
            CreateNexusModsRules(),
            CreateDiscordRules(),
        };
    }

    public static ServiceDefinition? GetById(string id)
    {
        return GetAll().FirstOrDefault(s => s.Id == id);
    }

    private static ServiceDefinition CreateSteamRules()
    {
        return new ServiceDefinition
        {
            Id = "steam",
            Name = "Steam",
            Description = "Steam 商店、社区、API 加速",
            Icon = "🎮",
            Domains = new List<DomainRule>
            {
                // Steam 商店
                new()
                {
                    Domain = "store.steampowered.com",
                    CandidateIps = new List<string>
                    {
                        "23.52.164.132",   // Akamai
                        "104.75.136.29",   // Akamai
                        "23.44.238.172",   // Akamai CN
                        "23.36.238.110",   // Akamai
                        "23.215.179.31",   // Akamai
                    },
                    Port = 443,
                    UseProxy = true,
                },
                // Steam 社区
                new()
                {
                    Domain = "steamcommunity.com",
                    CandidateIps = new List<string>
                    {
                        "23.52.164.132",
                        "104.75.136.29",
                        "23.44.238.172",
                        "23.36.238.110",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                // Steam API
                new()
                {
                    Domain = "api.steampowered.com",
                    CandidateIps = new List<string>
                    {
                        "23.52.164.132",
                        "104.75.136.29",
                        "23.44.238.172",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                // Steam CDN
                new()
                {
                    Domain = "steamcdn-a.akamaihd.net",
                    CandidateIps = new List<string>
                    {
                        "23.52.164.132",
                        "104.75.136.29",
                        "23.44.238.172",
                        "23.36.238.110",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                // Steam 聊天
                new()
                {
                    Domain = "chat.steampowered.com",
                    CandidateIps = new List<string>
                    {
                        "23.52.164.132",
                        "104.75.136.29",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                // Steam 媒体
                new()
                {
                    Domain = "steamuserimages-a.akamaihd.net",
                    CandidateIps = new List<string>
                    {
                        "23.52.164.132",
                        "104.75.136.29",
                        "23.44.238.172",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                // Steam 静态资源
                new()
                {
                    Domain = "community.akamai.steamstatic.com",
                    CandidateIps = new List<string>
                    {
                        "23.52.164.132",
                        "104.75.136.29",
                        "23.44.238.172",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                new()
                {
                    Domain = "store.akamai.steamstatic.com",
                    CandidateIps = new List<string>
                    {
                        "23.52.164.132",
                        "104.75.136.29",
                        "23.44.238.172",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                // Steam 持久化内容
                new()
                {
                    Domain = "clan.akamai.steamstatic.com",
                    CandidateIps = new List<string>
                    {
                        "23.52.164.132",
                        "104.75.136.29",
                        "23.44.238.172",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                // Steam 客户端更新
                new()
                {
                    Domain = "media.steampowered.com",
                    CandidateIps = new List<string>
                    {
                        "23.52.164.132",
                        "104.75.136.29",
                    },
                    Port = 443,
                    UseProxy = true,
                },
            }
        };
    }

    private static ServiceDefinition CreateGithubRules()
    {
        return new ServiceDefinition
        {
            Id = "github",
            Name = "GitHub",
            Description = "GitHub 网站、Raw文件、Gist 加速",
            Icon = "🐙",
            Domains = new List<DomainRule>
            {
                new()
                {
                    Domain = "github.com",
                    CandidateIps = new List<string>
                    {
                        "20.205.243.166",
                        "140.82.121.3",
                        "140.82.121.4",
                        "140.82.113.3",
                        "140.82.113.4",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                new()
                {
                    Domain = "github.githubassets.com",
                    CandidateIps = new List<string>
                    {
                        "185.199.108.154",
                        "185.199.109.154",
                        "185.199.110.154",
                        "185.199.111.154",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                new()
                {
                    Domain = "raw.githubusercontent.com",
                    CandidateIps = new List<string>
                    {
                        "185.199.108.133",
                        "185.199.109.133",
                        "185.199.110.133",
                        "185.199.111.133",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                new()
                {
                    Domain = "gist.github.com",
                    CandidateIps = new List<string>
                    {
                        "140.82.121.3",
                        "140.82.121.4",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                new()
                {
                    Domain = "gist.githubusercontent.com",
                    CandidateIps = new List<string>
                    {
                        "185.199.108.133",
                        "185.199.109.133",
                        "185.199.110.133",
                        "185.199.111.133",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                // GitHub Copilot
                new()
                {
                    Domain = "copilot.github.com",
                    CandidateIps = new List<string>
                    {
                        "140.82.121.22",
                        "140.82.121.23",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                // GitHub API
                new()
                {
                    Domain = "api.github.com",
                    CandidateIps = new List<string>
                    {
                        "140.82.121.6",
                        "140.82.121.5",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                // GitHub releases
                new()
                {
                    Domain = "objects.githubusercontent.com",
                    CandidateIps = new List<string>
                    {
                        "185.199.108.133",
                        "185.199.109.133",
                        "185.199.110.133",
                        "185.199.111.133",
                    },
                    Port = 443,
                    UseProxy = true,
                },
            }
        };
    }

    private static ServiceDefinition CreateGoogleTranslateRules()
    {
        return new ServiceDefinition
        {
            Id = "google-translate",
            Name = "Google翻译",
            Description = "Google 翻译 API 加速",
            Icon = "🌐",
            Domains = new List<DomainRule>
            {
                new()
                {
                    Domain = "translate.googleapis.com",
                    CandidateIps = new List<string>
                    {
                        "142.250.80.10",
                        "142.250.185.206",
                        "142.250.186.46",
                        "172.217.16.206",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                new()
                {
                    Domain = "translate.google.com",
                    CandidateIps = new List<string>
                    {
                        "142.250.80.10",
                        "142.250.185.206",
                        "142.250.186.46",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                new()
                {
                    Domain = "www.google.com",
                    CandidateIps = new List<string>
                    {
                        "142.250.80.4",
                        "142.250.185.228",
                        "142.250.186.14",
                    },
                    Port = 443,
                    UseProxy = true,
                },
            }
        };
    }

    private static ServiceDefinition CreateOriginRules()
    {
        return new ServiceDefinition
        {
            Id = "origin",
            Name = "Origin / EA",
            Description = "EA Origin 平台加速",
            Icon = "🎯",
            Domains = new List<DomainRule>
            {
                new()
                {
                    Domain = "www.ea.com",
                    CandidateIps = new List<string>
                    {
                        "159.153.28.130",
                        "159.153.28.131",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                new()
                {
                    Domain = "accounts.ea.com",
                    CandidateIps = new List<string>
                    {
                        "159.153.28.130",
                        "159.153.28.131",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                new()
                {
                    Domain = "origin-a.akamaihd.net",
                    CandidateIps = new List<string>
                    {
                        "23.52.164.132",
                        "104.75.136.29",
                        "23.44.238.172",
                    },
                    Port = 443,
                    UseProxy = true,
                },
            }
        };
    }

    private static ServiceDefinition CreateUplayRules()
    {
        return new ServiceDefinition
        {
            Id = "uplay",
            Name = "Ubisoft Connect",
            Description = "育碧 Uplay/Connect 平台加速",
            Icon = "🎮",
            Domains = new List<DomainRule>
            {
                new()
                {
                    Domain = "connect.ubisoft.com",
                    CandidateIps = new List<string>
                    {
                        "216.98.48.20",
                        "216.98.48.21",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                new()
                {
                    Domain = "ubisoftconnect.com",
                    CandidateIps = new List<string>
                    {
                        "216.98.48.20",
                        "216.98.48.21",
                    },
                    Port = 443,
                    UseProxy = true,
                },
            }
        };
    }

    private static ServiceDefinition CreateTwitchRules()
    {
        return new ServiceDefinition
        {
            Id = "twitch",
            Name = "Twitch",
            Description = "Twitch 直播平台加速",
            Icon = "📺",
            Domains = new List<DomainRule>
            {
                new()
                {
                    Domain = "www.twitch.tv",
                    CandidateIps = new List<string>
                    {
                        "151.101.66.167",
                        "151.101.194.167",
                        "151.101.2.167",
                        "151.101.130.167",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                new()
                {
                    Domain = "static.twitchcdn.net",
                    CandidateIps = new List<string>
                    {
                        "151.101.66.167",
                        "151.101.194.167",
                    },
                    Port = 443,
                    UseProxy = true,
                },
            }
        };
    }

    private static ServiceDefinition CreateSpotifyRules()
    {
        return new ServiceDefinition
        {
            Id = "spotify",
            Name = "Spotify",
            Description = "Spotify 音乐服务加速",
            Icon = "🎵",
            Domains = new List<DomainRule>
            {
                new()
                {
                    Domain = "open.spotify.com",
                    CandidateIps = new List<string>
                    {
                        "151.101.66.167",
                        "151.101.194.167",
                        "151.101.2.167",
                        "151.101.130.167",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                new()
                {
                    Domain = "ap-gew4.spotify.com",
                    CandidateIps = new List<string>
                    {
                        "35.186.224.25",
                        "35.186.224.26",
                    },
                    Port = 443,
                    UseProxy = true,
                },
            }
        };
    }

    private static ServiceDefinition CreateRobloxRules()
    {
        return new ServiceDefinition
        {
            Id = "roblox",
            Name = "Roblox",
            Description = "Roblox 游戏平台加速",
            Icon = "🎮",
            Domains = new List<DomainRule>
            {
                new()
                {
                    Domain = "www.roblox.com",
                    CandidateIps = new List<string>
                    {
                        "128.116.0.1",
                        "128.116.0.2",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                new()
                {
                    Domain = "assetdelivery.roblox.com",
                    CandidateIps = new List<string>
                    {
                        "128.116.0.1",
                        "128.116.0.2",
                    },
                    Port = 443,
                    UseProxy = true,
                },
            }
        };
    }

    private static ServiceDefinition CreateModIoRules()
    {
        return new ServiceDefinition
        {
            Id = "modio",
            Name = "Mod.io",
            Description = "Mod.io 模组平台加速",
            Icon = "🔧",
            Domains = new List<DomainRule>
            {
                new()
                {
                    Domain = "mod.io",
                    CandidateIps = new List<string>
                    {
                        "104.22.58.179",
                        "104.22.59.179",
                        "172.67.0.99",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                new()
                {
                    Domain = "api.mod.io",
                    CandidateIps = new List<string>
                    {
                        "104.22.58.179",
                        "104.22.59.179",
                    },
                    Port = 443,
                    UseProxy = true,
                },
            }
        };
    }

    private static ServiceDefinition CreateNexusModsRules()
    {
        return new ServiceDefinition
        {
            Id = "nexusmods",
            Name = "Nexus Mods",
            Description = "Nexus Mods 模组平台加速",
            Icon = "📦",
            Domains = new List<DomainRule>
            {
                new()
                {
                    Domain = "www.nexusmods.com",
                    CandidateIps = new List<string>
                    {
                        "151.101.2.217",
                        "151.101.66.217",
                        "151.101.130.217",
                        "151.101.194.217",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                new()
                {
                    Domain = "staticdelivery.nexusmods.com",
                    CandidateIps = new List<string>
                    {
                        "151.101.2.217",
                        "151.101.66.217",
                    },
                    Port = 443,
                    UseProxy = true,
                },
            }
        };
    }

    private static ServiceDefinition CreateDiscordRules()
    {
        return new ServiceDefinition
        {
            Id = "discord",
            Name = "Discord",
            Description = "Discord 语音通讯加速",
            Icon = "💬",
            Domains = new List<DomainRule>
            {
                new()
                {
                    Domain = "discord.com",
                    CandidateIps = new List<string>
                    {
                        "162.159.135.234",
                        "162.159.136.234",
                        "162.159.137.234",
                        "162.159.138.234",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                new()
                {
                    Domain = "cdn.discordapp.com",
                    CandidateIps = new List<string>
                    {
                        "162.159.130.233",
                        "162.159.131.233",
                        "162.159.132.233",
                        "162.159.133.233",
                    },
                    Port = 443,
                    UseProxy = true,
                },
                new()
                {
                    Domain = "gateway.discord.gg",
                    CandidateIps = new List<string>
                    {
                        "162.159.135.234",
                        "162.159.136.234",
                    },
                    Port = 443,
                    UseProxy = true,
                },
            }
        };
    }
}
