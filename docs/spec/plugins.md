# 插件系统（SPEC/plugins）

> 决策依据：ADR-0003（goja 唯一机制；权限模型照 Emby：信任安装、完整宿主
> API、无权限确认弹窗）。v1 开放面：**Provider（刮削源）/ 元数据后处理 /
> 事件钩子**。

## 1. 安装与形态 〔定稿〕

- 插件 = `plugins/<name>/` 目录：`plugin.json`（name、version、**api_version**、
  entry、config_schema）+ JS 文件（entry 为 goja 入口）。
- 管理后台插件页：列表、启/停用、重载、配置编辑（按 config_schema 自动渲染
  表单，存 PG）。装新插件 = 放目录 + 页面重载（或 zip 上传解压）。
- 信任安装：无权限申报、无确认弹窗；README 提示"插件可访问服务器全部
  宿主 API，只装可信来源"——照 Emby 口径。

## 2. 隔离边界 〔定稿〕

- 每插件一个 goja Runtime（单线程语义），插件内未捕获异常只杀死该插件
  并标记 failed（管理页可见），**不拖死服务器**。
- goja 天然无文件系统/进程能力（解释器事实，非权限设计）；宿主能力全量
  经显式 API 面给出（下节）。
- 宿主调用有全局超时与调用深度保护；插件阻塞不阻塞事件总线。

## 3. 宿主 API 签名 〔草案→20 册定稿〕

```ts
// 注册面（插件 entry 顶层调用）
registerProvider({
  id: string,
  canHandle: (type: string, name: string) => boolean,
  search: (query: RemoteSearchQuery) => RemoteSearchResult[],  // 形状照 Emby /Items/RemoteSearch
  get: (itemId: string, providerIds: object) => MetadataPatch,  // 缺省字段才生效；locked 字段宿主层拦截
})
on(event: string, handler: (payload: object) => void)
// v1 事件：itemAdded / itemUpdated / itemRemoved / playbackStart /
//          playbackStop / scanCompleted / taskFailed

// 能力面（全量开放，无逐项授权）
http.fetch(url: string, opts?: {method, headers, body, timeoutMs}): Response
log: { info(...a), warn(...a), error(...a) }        // slog 附 plugin=<name>
storage: { get(key), set(key, value) }               // 插件命名空间 KV，落 PG
config: { get(): object }                            // config_schema 的用户配置
```

- `MetadataPatch` 只允许填空：宿主在分发前比对 `locked_fields`，锁定字段
  直接丢弃（ADR-0009 语义在宿主层强制）。
- 事件为进程内 eventbus 广播（core.md §3），无跨进程投递。

## 4. 内置示例与生态路线 〔定稿〕

- 仓库自带 `plugins/examples/`：最小 Provider 示例 + 事件钩子示例
  （同时是 CI 冒烟负载）。
- 路线：AniDB / TVDB / 豆瓣 / 弹弹play 社区池等后续全部以插件接入，
  核心不内置。

## 5. 验收

- goja 单测夹具：注册 Provider → 假事件驱动 search/get → 断言 MetadataPatch
  且锁定字段被拦截。
- 插件抛未捕获异常 → 服务器存活、插件标记 failed、管理页可见。
- storage 命名空间隔离（插件 A 读不到插件 B 的键）。