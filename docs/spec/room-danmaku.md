# 房间与弹幕（SPEC/room-danmaku）

> 术语见 [GLOSSARY.md](../../GLOSSARY.md)（Room/Anchor/Danmaku/RoomComment）。
> 房间协议为**草案**（待用户过目）；弹幕端点形状为**定稿**（有 LinWeb 实测消费端背书）。

## 1. 房间子系统（已锁定决策汇编）

- 私密空间：**无公开列表，纯邀请制**（定稿）。加入唯一途径 = 房间号（+可选密码）；
  邀请形态 = **房间号+密码文本**，网页端"加入房间"按钮输入即可（定稿）。
- 容量：默认 **5 人**，管理员可调上限；**每用户同时只能进 1 个房间**（可换房不可双开）。
- 同步语义：**锚点跟随最快成员，无群体等待**（定稿）——慢者掉队自追，无人拖拽全场。
- 控制权：默认全员可控制（播放/暂停/拖动），房主可切换"仅房主"模式。
- 评论区：500 条环形缓冲（内存，重启/销毁即清）；10 条/分钟/人；无弹幕、无语音。
- 房主：可主动转移；被动退出（断线超宽限）自动转移给最早加入的在线成员。
- 生命周期：全员离线（含宽限中）→ 立即销毁；**30 天不活跃自动删除**（定稿）。
- 反广告：房间评论速率异常（近 5 分钟 > 基线 5 倍且超绝对下限）→ 抽样送审核模型；
  广告占比 > 50% → **自动封禁**（踢出+禁止加入+审计记录，管理员可解封）。

## 2. Anchor 协议 〔草案〕

### 2.1 传输
- WebSocket 端点：`/room/ws?roomId=<房间号>&api_key=<token>`（WS 无法带自定义头，
  沿用 api_key 查询参数惯例，与 LinWeb 一致）。
- 首帧鉴权：服务器校验 token + 房间密码（密码未提供时若房间有密码则拒绝，
  客户端收到 `need_password` 后重连时携带）。
- 单用户单房间：服务端在 join 时踢掉该用户在其他房间的连接（旧房间按被动退出处理）。

### 2.2 帧定义（JSON，每帧一个对象）
客户端 → 服务器：
| type | 字段 | 语义 |
|---|---|---|
| `join` | `password?` | 进房请求（WS 连接后首帧） |
| `state` | `positionTicks, paused, rate, mediaSourceId` | 本地播放头汇报，**≥1 次/2s**，seek 后立即加发 |
| `action` | `kind: play/pause/seek, positionTicks?` | 显式控制（全员或仅房主模式生效者） |
| `comment` | `text` | 评论区发言（限速 10/min） |
| `leave` | — | 主动退出 |
| `set_mode` | `control: all/owner` | 房主切换控制模式 |
| `transfer` | `to: userId` | 房主转移 |

服务器 → 客户端：
| type | 字段 | 语义 |
|---|---|---|
| `snapshot` | `room{roomId,name,members[],control}, anchor, nowPlaying?` | 进房/重连全量状态 |
| `anchor` | `anchor` | 锚点更新（节流 ≥500ms 一次） |
| `comment` | `userId,userName,text,at` | 评论广播 |
| `member` | `event: join/leave/timeout, userId,userName` | 成员变动 |
| `need_password` / `denied{reason}` | — | 鉴权失败（封禁/已满/密码错） |
| `kicked{reason}` | — | 被封/被踢 |

### 2.3 Anchor 形式化
```
Anchor = { itemId, mediaSourceId, positionTicks, at, paused, rate, leaderUserId }
投影:  target = paused ? positionTicks
                       : positionTicks + (serverNow - at) * rate
```
- **leader 判定（最快者）**：取"最近 10s 内有 state 上报"的成员中
  `positionTicks` 最大者；其最新 `state` 即锚点。
- **控制权覆盖**：任何 `action` 帧立即以动作者为准重置锚点（含 leader 换人）。
- **无等待**：成员缓冲掉队不产生任何全局暂停；其 `state` 上报继续但**不参与
  leader 判定**（其 position 落后于锚点时自动失去 leader 资格，追上即恢复）。
- **时基**：服务器时间为唯一权威；客户端对 `target` 与本地播放头偏差
  >2.5s 时 seek、80ms~2.5s 时调 `playbackRate`（±8% 封顶）、<80ms 不动
  ——阈值沿用 LinWeb 实测调优值，写死为常量。

### 2.4 断线与生命周期
- 心跳：客户端 25s 一次 ping 帧；服务器 70s 未闻 → 被动退出（宽限 60s 内
  同身份重连直接恢复，免重输密码）。
- 销毁：无在线成员且无宽限中成员 → 立即销毁；30 天无任何活动 → 清理。
- 封禁：`status=banned` 的房间拒绝 join，管理员可解封。

### 2.5 验收（本册）
- Testcontainers/模拟客户端套件：双客户端进房 → leader seek 后 follower 5s 内
  收敛到 ±80ms；主动退出立即除名；断线 60s 宽限重连恢复身份；全员掉线即销毁；
  评论限速与 500 条滚动断言；封禁流程断言。命令随实现补入本节。

## 3. 弹幕端点 〔定稿·形状〕

### 3.0 集成源与来源分层 〔定稿〕
- 契约权威：[misaka_danmu_server](https://github.com/l429609201/misaka_danmu_server)
  与 [danmu_api](https://github.com/huangxd-/danmu_api)（两者均兼容弹弹play API 规范）。
  dandanplay 兼容端点形状以这两个项目的实际实现为准，实现前核对许可证。
- 弹幕来源：
  1. **自存储**：用户发送 + 导入，挂 Canonical 条目；导出共享只含这一层（CC0）。
  2. **伴生容器（可选安装）**：docker-compose 以可选组件提供 misaka_danmu_server
     与 danmu_api 的部署（`--profile danmaku` 风格开关），起 Serata 镜像时勾选即
     一并安装；平台弹幕（爱优腾芒/B站等）的抓取由 danmu_api 容器完成——
     **Serata 不做原生平台适配器**（那是 danmu_api 的维护面，接口随时会变，
     坏了等上游更新），Serata 只按弹弹play 兼容形状消费它们。
  3. **自填上游**：用户已部署的 misaka/danmu_api 或其他弹弹play 兼容实例，
     在弹幕设置里填地址接入，兼容共存。
- 上游清单（伴生/自填）在配置中为列表，可多个并存；`/comment` 响应 = 自存储
  + 全部上游合并：按 (text, 时间取整) 去重，响应体量上限可配；单上游故障
  静默跳过不拖死响应。
- **导出包只含自存储弹幕**——上游/平台弹幕的版权归平台与发送者，不得再分发。

### 3.1 弹弹play 兼容端点（LinWeb 现成消费端，形状不可更改）
- `GET /api/v2/search/episodes?anime=<标题>`
  → `{code, success, episodes:[{animeId, animeTitle, episodeId, episodeTitle}]}`
  （LinWeb `src/danmaku/index.js` 消费的实际形状）
- `GET /api/v2/comment/{episodeId}?withRelated=true&format=json`
  → `{count, comments:[{cid, p:"<time>,<mode>,<color>,<uid>", m:"<text>"}]}`
  （注意 p 字段序：弹弹play 是 时间,模式,颜色,uid —— 与 B 站 XML 不同，
  解析器各自独立，测试断言盯死字段序）
- 内部映射：服务器为每个 Item 维护**弹幕池条目**（item → episodeId 映射表），
  search/episodes 按 Canonical 标题+年份匹配库内条目，命中即返回映射 id。

### 3.2 Serata 自有端点
- `GET /Danmaku/{itemId}?format=json`：全量弹幕（含持久化历史）。
- `POST /Danmaku/{itemId}`：发送 `{timeMs, mode, color, text}`；登录用户；
  20 条/分钟/用户；无条目级上限（定稿）。
- `POST /Danmaku/{id}/Report`：举报；同一用户对同一弹幕唯一；≥3 人进人工审核。
- 弹幕管理/审核任务、导出任务见任务系统（notify/data-model 册）：
  每日增量导出 + 每 N 天（默认 15）全量导出并清理增量包，目标 = 本地目录/S3
  （配置化）；审核模型每日扫新增，删广告垃圾；用户不可删自己弹幕（定稿），
  管理员可删；被删数进看板。

### 3.3 验收（本册）
- 形状对照测试：以 LinWeb `src/danmaku/parse.js` 的真实解析器作为消费端，
  跑通 search→comment 全链路（字段序断言）。
- 限速/举报阈值/导出包内容的 Testcontainers 断言。