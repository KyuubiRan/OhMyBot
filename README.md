# OhMyBot

.Net 高性能 多功能 机器人

目前已实现 Telegram 与 QQ（基于 NapCat / OneBot v11）机器人，后续支持更多

## 功能

- [x] 库游社(包括社区、游戏)自动/手动签到 & 自动签到消息推送
- [x] 米游社(国服米游社任务 + 国服/国际服游戏签到)自动/手动签到 & 自动签到消息推送
    - 手动游戏签到会弹出游戏勾选面板，勾选要签到的游戏后点「签到」；绑定多个账号时可一键「全部签到」
- [ ] 鹰游社


以及一些其他零碎小功能

具体功能请使用 /help 查看

## 需求

- .Net 10.0
- PostgreSQL 15.0+
- Redis 7.0+ (可选，不使用 Redis 则降级为内存缓存)
- RabbitMQ
- Docker（可选，仅 QQ 网关需要，用于跑 NapCat；见 `OhMyBot.QQGateway/docker/`）

**注:** *不支持 NativeAOT*

## 配置

本项目为多服务架构，各服务分别有独立配置文件。将对应目录下的 `appsettings.template.json` 复制为 `appsettings.json` 后填写：

- `OhMyBot.Core` —— 核心服务（gRPC、数据库、定时签到、通知发布）
- `OhMyBot.TelegramGateway` —— Telegram 网关
- `OhMyBot.QQGateway` —— QQ 网关（可选，基于 OneBot v11）

### Core（`OhMyBot.Core/appsettings.json`）

- `ConnectionStrings:Postgres | string` \[必填\] PostgreSQL 连接字符串
- `Redis:Configuration | string` \[可选\] Redis 连接配置（StackExchange.Redis 格式，如 `localhost:6379`）；留空则降级为进程内缓存（单实例可用，多实例部署需配置 Redis 以共享缓存）
- `Encryption:Key | string` \[必填\] 加密存储 Cookie/Token 用的密钥，需为 32 字节的 Base64 字符串
- `RabbitMQ` \[必填\] 通知消息队列，用于向各网关推送签到结果
    - `HostName | string` 主机地址，默认 `localhost`
    - `Port | int` 端口，默认 `5672`
    - `UserName | string` / `Password | string` 账号密码，默认 `guest`
    - `VirtualHost | string` 虚拟主机，默认 `/`
    - `NotificationExchange | string` 通知交换机，默认 `ohmybot.notifications`
- `Kestrel:Endpoints:Grpc:Url | string` gRPC 监听地址（供网关连接），默认 `http://localhost:5100`
- `ScheduledTasks` 各平台自动签到定时任务（`AiRouterAutoSign` / `KuroAutoSign` / `MihoyoAutoSign`）
    - `Enabled | bool` 是否启用
    - `Cron | string` Cron 表达式（UTC 时区）
- 其余缓存/路由项（`IdentityCache`、`UserProfileCache`、`CallbackActions`、`Routes`、`AiRouter`）均有默认值，一般无需改动

### Telegram 网关（`OhMyBot.TelegramGateway/appsettings.json`）

- `BotInstanceId | string` 实例标识，默认 `telegram-default`
- `Telegram:BotToken | string` \[必填\] 机器人 Token
- `Telegram:HttpProxy | string` \[可选\] HTTP 代理地址（如 `http://127.0.0.1:7890`），留空则不使用代理
- `Telegram:DropPendingUpdates | bool` \[可选\] 启动时是否丢弃离线期间积压的更新，默认 `true`
- `Telegram:CommandPrefixes | string[]` \[可选\] 命令前缀，默认 `["/", "!", "."]`
- `Core:GrpcAddress | string` \[必填\] Core 服务的 gRPC 地址，默认 `http://localhost:5100`
- `RabbitMQ` \[必填\] 与 Core 保持一致，用于接收通知（额外含 `NotificationQueue`、`NotificationRoutingKey`）

### QQ 网关（可选，`OhMyBot.QQGateway/appsettings.json`）

- `BotInstanceId | string` 实例标识，默认 `qq-default`
- `OneBot:Endpoint | string` \[必填\] OneBot v11 WebSocket 地址，如 `ws://localhost:3001`
- `QQ:CommandPrefixes | string[]` \[可选\] 命令前缀，默认 `["/", "!", "."]`
- `Core:GrpcAddress | string` \[必填\] Core 服务的 gRPC 地址
- `RabbitMQ` \[必填\] 与 Core 保持一致，用于接收通知

QQ 网关需要一个 OneBot v11 实现（NapCat）。项目在 `OhMyBot.QQGateway/docker/` 附带了**仅含 NapCat** 的
docker-compose（数据存 Docker 命名卷、固定设备 MAC/hostname 以免容器重建后重复扫码），编译后会拷到网关
输出目录，可直接起：

```bash
cd OhMyBot.QQGateway/docker   # 或编译后到网关输出目录的 docker/ 下
NAPCAT_UID=$(id -u) NAPCAT_GID=$(id -g) docker compose up -d
```

首次在 http://localhost:6099 扫码登录；网关通过 `ws://localhost:3001` 连接。QQ 无官方可点击按钮，
交互式命令（签到面板、订阅管理等）改为「**编号文本菜单 + 回复序号**」：Core 把面板渲染成带编号的纯文本，
用户回复该消息并发送序号（私聊也可直接发数字）来选择。

> 用户权限由 Core 统一管理，通过 `/setpriv` 命令设置。