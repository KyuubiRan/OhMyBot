# OhMyBot

.Net 高性能 多功能 机器人

目前已实现 Telegram 机器人，后续支持更多

## 功能

- [x] 库游社(包括社区、游戏)自动/手动签到 & 自动签到消息推送
- [x] 图片转换(jpg/png/webp) 支持一键转 Telegram Sticker 支持长宽参数
- [x] Roll点 `5d20+(3d6-1d6)*2 = 5d20[6,14,18,10,5] + (3d6[2,1,3] - 1d6[6]) × 2 = 53`
- [x] ~~暂时~~没什么用的签到功能

以及一些其他零碎小功能

具体功能请使用 /help 查看

## 需求

- .Net 10.0
- PostgreSQL 15.0+
- Redis 7.0+ (可选，不使用 Redis 则降级为内存缓存)

**注:** *不支持 NativeAOT*

## 配置

具体请查看 `appsettings.json`

### Telegram

- Bot
    - `Token | string`  \[必填\] 机器人 Token
    - `OwnerId | string` \[必填\] 主人 ID，用于最高权限命令
    - `CommandPrefixes | string[]` 命令前缀，默认为 `/` 和 `!`
    - `DefaultUserPrivilege | UserPrivilege` \[可选\] 默认用户权限，默认为 `None`，即无权限访问
    - `EnableProxy | bool` \[可选\] 是否启用代理，默认为 `false`
    - `HttpProxy` \[可选\] 代理配置
        - `Host | string` \[可选\] 代理主机地址，默认为 `http://127.0.0.1`
        - `Port | int` \[可选\] 代理端口，默认为 `7890`

- ConnectionStrings
    - `Database | string` PostgreSQL 连接字符串
    - `Redis | string` \[可选\] Redis 连接字符串，不使用 Redis 则降级为内存缓存