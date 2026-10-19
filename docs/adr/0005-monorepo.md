# Monorepo 结构

仓库单仓三目录：`server/`（Go 后端）、`web/`（Vue 3 + TS 前端，LinWeb 播放核心以可更新方式引入）、`docs/`（SPEC 与 ADR）。CI 一条流水线：Go test → web build → 多架构 Docker 构建（amd64/arm64）。

## Considered Options

- 拆 server / web 双仓：issue、release、版本对齐都要人工维护，开源产品阶段收益为负。

## Consequences

- LinWeb 是独立上游仓库，引入方式必须支持拉取上游更新（git subtree），不能复制粘贴后失联。
