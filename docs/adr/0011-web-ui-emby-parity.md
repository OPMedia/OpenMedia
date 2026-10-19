# Web UI 以 Emby 网页端为基准对齐重写；LinWeb 只贡献播放核心

前端路线修正：浏览壳/管理后台**以 Emby 官方网页端的布局与交互为基准**、用 Vue 3 + TS 重写并严格对齐，不采用 LinWeb 的浏览壳设计。联调方法：用户持有真实 Emby 网页端，双端并排对照，做到与新 Emby 一致后再做后续自主调整（PC 与移动端同一流程）。LinWeb 的贡献收窄为：播放核心模块（demux/remux/subs/danmaku/player.js）与播放页形态。

## Considered Options

- 以 LinWeb 浏览壳为基线演进：是我们自己的设计，但生态用户（从 Emby 迁来）对 Emby 布局有肌肉记忆，且"对标 Emby"的产品在界面上理应先像再超越。

## Consequences

- 联调基准是**新近版本 Emby 网页端**的行为，不止 4.8 API 锚点。
- Emby 布局的已知缺陷在对齐完成、功能可验证之后再改，顺序不可倒置。