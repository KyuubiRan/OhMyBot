# OhMyBot

.NET 10 多平台机器人核心、宿主与 Gateway。

本仓库只包含平台无关的 Core、可执行 Host、跨进程 Contracts、Telegram/QQ Gateway、OneBot V11 协议库和插件公共 API。具体集成插件已经拆分到独立仓库：

- 公开插件：<https://github.com/KyuubiRan/OhMyBot.Plugins>
- 私有插件：`KyuubiRan/OhMyBot.PrivatePlugins`（需要仓库权限）

## 项目结构

- `OhMyBot.Core`：用户、身份、权限、路由、命令、数据库和平台渲染。
- `OhMyBot.Core.Host`：gRPC、配置、数据库迁移、管理控制台和插件运行时。
- `OhMyBot.Plugin.Abstractions` / `OhMyBot.Plugin.Commanding`：Host 与插件共享的稳定 API。
- `OhMyBot.Contracts`：gRPC、DTO 和消息事件契约。
- `OhMyBot.TelegramGateway` / `OhMyBot.QQGateway`：平台接入层。
- `OhMyBot.OneBotV11`：QQ Gateway 使用的 OneBot V11 协议库。
- `OhMyBot.Tests`：Core、Host、Gateway 和协议测试。

## 构建与测试

```bash
dotnet build OhMyBot.slnx
dotnet test OhMyBot.Tests/OhMyBot.Tests.csproj
```

项目输出统一位于：

```text
build/<ProjectName>/bin/<Configuration>/<TargetFramework>/
build/<ProjectName>/obj/<Configuration>/<TargetFramework>/
```

## 启动 Core Host

把 `OhMyBot.Core.Host/appsettings.template.json` 复制为 `appsettings.json` 并填写配置，然后执行：

```bash
dotnet run --project OhMyBot.Core.Host/OhMyBot.Core.Host.csproj
```

Host 以自己的构建输出目录作为 content root。配置、`routes/route.json`、`.plugin-cache` 和 `Plugins/` 都按 Debug/Release 隔离在 Host 输出中。

连接已经运行的管理控制台：

```bash
dotnet run --project OhMyBot.Core.Host/OhMyBot.Core.Host.csproj -- --remote-console
```

## 插件开发

建议使用相邻目录布局：

```text
OhMyBot/
├── OhMyBot/
├── OhMyBot.Plugins/
└── OhMyBot.PrivatePlugins/
```

插件仓库默认通过 `../OhMyBot` 引用公共项目。插件构建完成后会自动部署到：

```text
OhMyBot/build/OhMyBot.Core.Host/bin/<Configuration>/net10.0/Plugins/<PluginName>/
```

已有 `pluginsettings.json` 不会被覆盖。使用 `plugin list`、`plugin status <id>`、`plugin reload <id|all>`、`plugin disable <id>` 和 `plugin enable <id>` 管理插件。

可以在 Host 的 `appsettings.json` 中持久化配置不应加载的插件：

```json
{
  "PluginRuntime": {
    "DisabledPluginIds": [
      "com.example.plugin"
    ]
  }
}
```

禁用的插件仍会显示为 `Disabled`，但不会创建实例、注册组件或执行插件 migration。控制台的 `enable/disable` 指令会立即加载或卸载插件，并把状态原子写入 `plugin-runtime-state.json`；该状态文件在下次启动时覆盖模板配置。直接修改 `appsettings.json` 中的列表则需要重启 Host。

## 运行依赖

- .NET 10
- PostgreSQL 15+
- Redis 7+（可选，留空时使用进程内缓存）
- RabbitMQ
- NapCat / OneBot v11（仅 QQ Gateway）

不支持 NativeAOT。
