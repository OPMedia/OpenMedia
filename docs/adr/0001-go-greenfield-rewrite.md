# Go 全新重写，不继承 Emby 的 C# 代码

要在 Emby 闭源后的最后一个开源版本（3.5.2.0，GPLv2）之上做一个对标 Emby 的高性能视频服务器。决定：**全新重写，Go**，不 fork、不现代化旧代码、不选 C#（ASP.NET Core 8）。

理由：目标负载是数千客户端并发 Range 拉流，Go 的 goroutine + `net/http.ServeContent` 的 `sendfile(2)` 零拷贝路径贴合该场景；单二进制是 NAS/Docker 交付的最优形态；`GOMEMLIMIT` 给容器内存上界提供确定性保证；旧 C# 是 EF6/Mono 时代架构，"彻底重构"的初衷与保留它矛盾。

## Considered Options

- Fork 3.5.2 渐进现代化（升 .NET 8）：最快能跑，但永远背着 2018 年架构，"彻底重构"不成立。
- C# ASP.NET Core 全新重写：可逐行参考旧代码，但 runtime 基础内存更大，且事件订阅 + IDisposable 的泄漏面不符合长驻服务器的要求。

## Consequences

- Emby 行为参照物是 **4.8 stable OpenAPI 契约**（见 ADR-0004），不是 3.5.2 源码——源码只用来查语义，不用来移植。
- 全部刮削/转码/会话逻辑从零实现，工期长，SPEC 必须对"不做什么"极度克制。
