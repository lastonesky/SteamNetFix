# .NET 单文件发布体积优化教程

以 SteamNetFix 项目为例，从 **92MB** 优化到 **18MB**，减少 **80%**。

## 1. 问题分析

当你使用 `--self-contained true` 构建 .NET 应用时，整个 .NET 运行时会被打包进可执行文件。

### 查看体积构成

```bash
# 构建后查看运行时DLL
ls -lh bin/Release/net8.0/win-x64/*.dll | sort -k5 -h -r | head -15
```

典型构成（以 SteamNetFix 为例）：

| 组成 | 大小 | 说明 |
|------|------|------|
| 项目自身代码 | 96 KB | 你写的代码，只占 0.1% |
| System.* 运行时库 | 57 MB | 基础类库、XML、JSON、加密等 |
| ASP.NET Core 框架 | 21 MB | Web 服务器、MVC、认证等 |
| 原生运行时 (coreclr/jit) | 6.5 MB | CLR 虚拟机和 JIT 编译器 |
| 其他 | ~7.5 MB | 诊断、调试符号等 |
| **总计** | **92 MB** | 324 个 DLL |

```bash
# 统计运行时DLL数量
ls bin/Release/net8.0/win-x64/*.dll | wc -l
# 输出: 324

# 各部分大小
du -ch bin/Release/net8.0/win-x64/Microsoft.AspNetCore.*.dll | tail -1  # 21MB
du -ch bin/Release/net8.0/win-x64/System.*.dll | tail -1                # 57MB
```

## 2. 优化方案

### 2.1 IL Trimming（裁剪未使用的代码）

IL Trimming 会分析程序的依赖图，移除未被引用的程序集、类型和方法。

在 `.csproj` 中添加：

```xml
<PropertyGroup>
    <!-- 启用裁剪 -->
    <PublishTrimmed>true</PublishTrimmed>
    
    <!-- 裁剪模式 -->
    <TrimMode>partial</TrimMode>
</PropertyGroup>
```

**TrimMode 选项：**

| 模式 | 效果 | 风险 |
|------|------|------|
| `partial` | 只裁剪未标记 `[AssemblyMetadata("IsTrimmable", "True")]` 的程序集 | 低，推荐 |
| `full` | 激进裁剪，移除所有未直接引用的代码 | 高，可能运行时崩溃 |

构建并对比：

```bash
# 不裁剪
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/original
ls -lh publish/original/SteamNetFix.exe
# 92 MB

# partial 裁剪
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/trimmed
ls -lh publish/trimmed/SteamNetFix.exe
# 31 MB  (-66%)
```

### 2.2 单文件压缩

.NET 7+ 支持在单文件发布时启用压缩，启动时自动解压：

```xml
<PropertyGroup>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
</PropertyGroup>
```

效果：

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/compressed
ls -lh publish/compressed/SteamNetFix.exe
# 18 MB  (-80%)
```

### 2.3 完整配置

最终 `.csproj` 配置：

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    
    <!-- 单文件发布 -->
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    
    <!-- IL 裁剪 -->
    <PublishTrimmed>true</PublishTrimmed>
    <TrimMode>partial</TrimMode>
    <SuppressTrimAnalysisWarnings>true</SuppressTrimAnalysisWarnings>
    
    <!-- 单文件压缩 -->
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
  </PropertyGroup>

</Project>
```

## 3. 效果对比

| 构建方式 | 大小 | 减少 | 启动速度 | 安全性 |
|----------|------|------|----------|--------|
| 无优化 | 92 MB | — | 快 | 最安全 |
| + `TrimMode=partial` | 31 MB | 66% | 快 | 安全 |
| + `TrimMode=partial` + 压缩 | **18 MB** | **80%** | 略慢（需解压） | **安全 ✅** |
| + `TrimMode=full` | 12 MB | 87% | 略慢 | ⚠️ 可能运行时崩溃 |
| + `TrimMode=full` + 压缩 | 11 MB | 88% | 略慢 | ⚠️ 可能运行时崩溃 |

## 4. 各平台构建命令

```bash
# Windows x64
dotnet publish -c Release -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=true \
  -p:TrimMode=partial \
  -p:EnableCompressionInSingleFile=true

# Windows ARM64
dotnet publish -c Release -r win-arm64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=true \
  -p:TrimMode=partial \
  -p:EnableCompressionInSingleFile=true

# Linux x64
dotnet publish -c Release -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=true \
  -p:TrimMode=partial \
  -p:EnableCompressionInSingleFile=true

# macOS ARM64 (Apple Silicon)
dotnet publish -c Release -r osx-arm64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=true \
  -p:TrimMode=partial \
  -p:EnableCompressionInSingleFile=true
```

## 5. 注意事项

### TrimMode=full 的风险

ASP.NET Core 大量使用**反射**（如 JSON 序列化、依赖注入、路由匹配），`full` 模式可能裁掉这些"看似未使用"的代码。

常见问题：
- `System.Text.Json` 序列化/反序列化失败
- 依赖注入解析服务失败
- 路由匹配不到 Controller

**解决方案：** 使用 `partial` 模式，它会保留标记了可裁剪的程序集中被反射引用的代码。

### 如果 partial 还是太大

可以组合使用以下策略：

1. **移除不需要的 NuGet 包** — 每个包都带依赖
2. **用 Minimal API 替代 MVC** — 减少 ASP.NET Core 依赖
3. **用 `HttpListener` 替代 ASP.NET Core** — 完全去掉 Web 框架（适合简单 API）
4. **考虑 Native AOT** — .NET 8 支持，编译为原生代码，但限制较多

### Native AOT（高级选项）

```xml
<PublishAot>true</PublishAot>
```

- 编译为原生机器码，无需运行时
- 体积可进一步缩小到 ~5-10MB
- 但不支持动态加载、反射等功能，需要大量适配

## 6. 调试裁剪问题

如果裁剪后运行时出错：

```bash
# 查看裁剪分析日志
dotnet publish -c Release -r win-x64 \
  --self-contained true \
  -p:PublishTrimmed=true \
  -p:TrimmerRootAssembly=SteamNetFix \
  -v detailed 2>&1 | grep -i "trim"
```

可以在代码中添加裁剪保护：

```csharp
// 保留某个类型不被裁剪
[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MyClass))]
```

## 总结

```
原始构建:       92 MB  ████████████████████████████████████████
+ partial trim: 31 MB  ████████████▌
+ 压缩:         18 MB  ███████▌                    ← 推荐
+ full trim:    12 MB  █████
```

**推荐配置：** `TrimMode=partial` + `EnableCompressionInSingleFile=true`

平衡了体积（-80%）和运行时稳定性。
