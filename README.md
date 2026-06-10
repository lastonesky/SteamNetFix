# 🚀 SteamNetFix

**跨平台网络加速工具** — 无广告的 Watt Toolkit (Steam++) 替代品

## ✨ 特性

- 🚀 **纯网络加速** — 专注于 Steam、GitHub 等服务的网络加速
- 🚫 **零广告** — 永远不会有广告、推广、弹窗
- 🌐 **跨平台** — 支持 Windows、macOS、Linux
- 🎮 **多服务支持** — Steam、GitHub、Google翻译、Discord 等
- 📡 **智能测速** — 自动选择最优 CDN 节点
- ⚡ **两种加速模式** — Hosts 修改 + 本地代理
- 🖥️ **Web 管理界面** — 现代化响应式 UI
- 🔓 **完全开源** — MIT 协议，代码完全透明

## 📦 支持的服务

| 服务 | 说明 |
|------|------|
| 🎮 Steam | 商店、社区、API、CDN |
| 🐙 GitHub | 网站、Raw文件、Gist、API |
| 🌐 Google翻译 | translate.googleapis.com |
| 🎯 Origin / EA | EA 平台 |
| 🎮 Ubisoft Connect | 育碧平台 |
| 📺 Twitch | 直播平台 |
| 🎵 Spotify | 音乐服务 |
| 🎮 Roblox | 游戏平台 |
| 🔧 Mod.io | 模组平台 |
| 📦 Nexus Mods | 模组平台 |
| 💬 Discord | 语音通讯 |

## 🚀 快速开始

### 方式一：直接运行

1. 从 [Releases](#) 下载对应平台的版本
2. **以管理员/root 权限运行**（需要修改 hosts 文件）
3. 程序会自动打开 Web 管理界面
4. 选择需要加速的服务，点击"开始加速"

### 方式二：从源码构建

```bash
# 克隆项目
git clone <repo-url>
cd steam_netfix

# 构建当前平台
dotnet publish -c Release -r <RID> --self-contained true -p:PublishSingleFile=true

# 示例：
# Windows x64
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# macOS ARM64 (Apple Silicon)
dotnet publish -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true

# Linux x64
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

或者使用构建脚本：

```bash
# Linux/macOS
chmod +x build.sh
./build.sh

# Windows
build.bat
```

## ⚙️ 命令行选项

```
SteamNetFix [选项]

选项:
  --port, -p <port>      Web UI 端口 (默认: 2606)
  --proxy-port <port>    代理端口 (默认: 2607)
  --no-proxy             禁用代理服务器
  --auto-start           启动时自动开启加速
  --help, -h             显示帮助
```

## 🔧 工作原理

### Hosts 加速模式

程序会修改系统的 hosts 文件，将目标域名指向最优的 CDN IP 地址：

```
# SteamNetFix 自动生成的 hosts 条目
23.52.164.132    store.steampowered.com
23.52.164.132    steamcommunity.com
140.82.121.4     github.com
185.199.108.133  raw.githubusercontent.com
...
```

### 代理加速模式

程序会在本地启动一个 HTTP/HTTPS 代理服务器，通过以下方式优化网络：

1. **DNS 优化** — 将域名解析到最优 IP
2. **连接优化** — TCP 连接优化
3. **路由优化** — 选择最快的网络路径

## 🛡️ 权限说明

由于需要修改系统 hosts 文件，程序需要管理员/root 权限：

- **Windows**: 右键 → "以管理员身份运行"
- **macOS/Linux**: `sudo ./SteamNetFix`

程序会自动检测权限状态，权限不足时会提示但不会报错。

## 📁 文件位置

- **配置文件**: `%APPDATA%/SteamNetFix/config.json` (Windows) 或 `~/.config/SteamNetFix/config.json` (Linux/macOS)
- **Hosts 文件**: 
  - Windows: `C:\Windows\System32\drivers\etc\hosts`
  - macOS/Linux: `/etc/hosts`

## 🆚 与 Watt Toolkit 对比

| 特性 | SteamNetFix | Watt Toolkit |
|------|-------------|--------------|
| 广告 | ❌ 无 | ✅ 有广告和推广 |
| 开源 | ✅ MIT 协议 | ⚠️ 部分开源 |
| 功能 | 网络加速 | 多功能工具箱 |
| 平台 | Win/Mac/Linux | Win/Mac/Linux |
| 依赖 | .NET Runtime | .NET Runtime |
| 体积 | ~18MB | ~50MB+ |

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

## 📄 许可证

MIT License

## 💡 致谢

感谢所有为网络加速技术做出贡献的开发者！

---

**无广告 · 开源 · 跨平台 · 专注网络加速**
