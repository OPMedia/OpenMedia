# API 锚定 Emby 4.8 stable OpenAPI，DTO 全量照抄

兼容策略：Serata 的 REST 层与 DTO（BaseItemDto 等）**全量照抄 Emby 4.8 stable 的 OpenAPI 契约**，使现有 Emby 客户端生态（Infuse、Fileball、安卓 TV 客户端等）与 LinWeb 零改动接入；在此之上允许自有扩展端点（弹幕、房间）。锚点版本写死在 SPEC，Emby 出 4.9 stable 后以一次评审升级。

## Considered Options

- 自造 API 只服务自家前端：形状最优，但放弃整个客户端生态，开源产品的接入性归零。
- 锚定手头 4.1.1.0 OpenAPI：过旧，缺后期端点与字段。

## Consequences

- 废弃字段也照抄（客户端在依赖它们），禁止"顺手精简"。
- 兼容原则：**DTO 形状与 Id 形态照搬（数字 Id），端点行为允许自有语义**——接入的便利来自形状，不承诺与 Emby 逐字节一致。
- Emby 未公开/闭源后的行为以 OpenAPI + 真实 4.8 客户端抓包为准，分歧记录进 SPEC 兼容矩阵。
