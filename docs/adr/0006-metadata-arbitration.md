# 元数据采用多源仲裁模型，AI 是同级 Provider

元数据来源不止 TMDB：TMDB 条目常被社区改坏（错译、错误合并、把官方多季合并成一季），动漫场景尤甚。决定：元数据管线为**多源仲裁**——TMDB、Bangumi、AniDB 等权威源各自提供原始数据（RawProvider，永久存档），**AI 仲裁器**参考多源（含官方分季信息、文件名与媒体信息）裁决出本地权威条目（Canonical）。AI 刮削与 TMDB 是同级数据源，不是 TMDB 的后处理器；否则起步时库就被 TMDB 的错误污染。

## Considered Options

- 单源直采（TMDB 直填）：实现最简，但错误元数据成为既成事实，事后清洗成本远高于仲裁。
- TMDB 为主 + AI 事后纠偏：仍然把 TMDB 当真相，AI 只能修不致错的表象，修不了结构（分季/排序）。

## Consequences

- 数据模型：`provider_payload`（原料，按源分表存 JSONB）与 `item` 规范化字段（Canonical）分离；仲裁记录（参与者、裁决字段、理由）留档可追溯。
- AI 需要 LLM API（OpenAI 兼容 endpoint + key，用户自填），**默认关闭，配置即用**——未配置时管线退化为"多源规则仲裁"（字段级优先级规则），服务器仍完整可用。
- 动漫双序号：`season_number/episode_number`（TMDB 序）与 `display_season/display_episode`（权威序，支持小数，如 OVA 12.5）并存，后者供"下一集"与客户端展示。
- v1 内核三源：TMDB、Bangumi、AI；AniDB/TVDB/豆瓣等经 Plugin（ADR-0003 goja）接入。
