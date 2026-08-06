# NapCat（QQ 网关的 OneBot 协议端）

仅含 NapCat 一个服务（不带数据库/中间件）。编译时本目录随 `docker-compose.template.yml` 与
`napcat-config/onebot11.json` 种子一起拷到网关输出目录，可直接在输出目录里跑。

## 启动

```bash
cp docker-compose.template.yml docker-compose.yml   # 首次；已存在就别覆盖
NAPCAT_UID=$(id -u) NAPCAT_GID=$(id -g) docker compose up -d
```

`docker-compose.yml` 是本机实配，和 `appsettings.json` 同规矩：不进 git、不进 publish 产物，
发布也不会覆盖它。按部署环境不同的调整（端口绑定、镜像版本等）改在实配里，模板保持通用；
模板有更新时自己对一遍差异。

> OneBot 正向 WebSocket 不带鉴权，能连上 3001 的人就能操控该 QQ 账号。模板为方便本地开发
> 直接暴露端口，部署到可被他人访问的网络时请在实配里收窄绑定地址或加防火墙规则。

- 首次登录：打开 http://localhost:6099 （WebUI）扫码。
- 网关连接：`ws://localhost:3001`（正向 WebSocket，已由 `onebot11.json` 种子预置开启）。
- 停止：`docker compose down`（保留卷/登录态）。
- 彻底清空：`docker compose down -v`（删除卷，需重新扫码）。

## 数据放在命名卷里（不落输出目录）

登录态与配置都存 Docker 命名卷 `ohmybot-napcat_napcat-config`、`ohmybot-napcat_napcat-qq`，
由 Docker 管理、在项目/输出目录之外：

- `docker compose down`/`up`、`dotnet clean` 重新编译都**不会丢**登录态。
- 不管从源码目录还是输出目录启动，用的都是同一份卷。
- 查看：`docker volume ls | grep ohmybot-napcat`；清除：`docker compose down -v`。

`onebot11.json` 种子由一次性 `napcat-seed` 服务写入 config 卷（已存在则跳过）。改了种子想生效
需 `down -v` 重置，或直接在 WebUI 里改。

## 为什么固定了 hostname / MAC

QQ 用"设备指纹"识别登录设备，其中包含容器 MAC 与 hostname。`docker compose down` 会删除
容器，`up` 时若不固定，Docker 会分配新的 MAC → 被判为新设备 → 需要重新扫码。因此 compose 里
固定了 `hostname` 与 `mac_address`。

> service 级 `mac_address` 需要较新的 Docker（Compose Spec / Engine 25+）。若掉线仍频繁，
> 可考虑把镜像从 `latest` 固定到某个已验证稳定的版本。
