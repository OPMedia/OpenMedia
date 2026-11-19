# Git archaeology & removable-baggage audit — OpenMedia 2026-11-19

**Scope:** read-only. No source file modified. Only this report was created.
**Pinned state:** `HEAD = eda846eefa86afdb01fc47a044d925ba84dd9b0f` (2026-11-19 10:02:25 +0800, `fix(perf): fixture 补上 clean_name 回填`), `git status --short` clean. HEAD moved twice during the audit (`35b5038…` → `eda846e…`); re-run the count commands against this hash if you need exact reproducibility. `git rev-list --count HEAD` = 15302 at pin (15005 Emby-era + 297 fork).

**Evidence conventions:** `sha:path:N` means line N of that file at that commit; commands are verbatim. "Reachable" means a grep-evidenced DI/registration/route path from `MediaBrowser.Server/Program.cs` → `ApplicationHost` → consumer, not a theoretical reference.

---

## 1. Timeline and architecture pivots

### 1.1 Boundary commits

| # | Date | Commit | Fact |
|---|------|--------|------|
| 1 | 2012-07-12 | `33c10e658aa0610c1940d3b5c18c06177e7a37a0` | First commit, "Initial check-in", author Luke Pulverenti. |
| 2 | **2018-09-19 23:03:50 -0400** | `1d7c2ab4bfebdae31f89fdaabd3b68782ccf495a` | **Last Emby commit**, message `3.5.3`. `git rev-list --count` = 15005. |
| 3 | **2026-10-13 21:55:37 +0800** | `5b67127909d78008e14f5f9191f1fdf5bc9cda1f` | **First fork commit**, author changes to `OpenMedia Dev`, message `docs: .NET 10 + Kestrel + PostgreSQL 升级方案 v2（决策确认）`. Parent is exactly (2): 8-year, 0-commit gap. |
| 4 | 2026-10-13 22:29:08 | `941f4c7e0d9ce4fe5ece040062bb6af9e3a64b8f` | C01: 6 closed-source DLLs decompiled with ilspycmd 11.1 into `src/` (~44k lines); BDInfo/DvdLib converted too. |
| 5 | 2026-10-19 11:33:05 | `62b4ef3a59da3d7c91a54104f8dedea872ede83f` | `feat(web): 接入 Serata 前端…` — **the Go ADRs/specs first enter git here**, 6 days *after* C# modernization started. |
| 6 | 2026-11-02 09:18:06 | `9e3d0b28e62dac822d95bcfdc53322cf9e86c5b6` | `docs: remove repository documentation and add agent guide`: 49 files, +192/−6152; all 17 ADRs, 12 specs and 20 design/measurement docs deleted; `AGENTS.md` added (`AGENTS.md:5-14` now declares C#/.NET 10 as source of truth). |
| 7 | 2026-11-18 → 11-19 | `1003e7176c…`, `d5e80ed223…` | Dead `Emby.Drawing.Net` deleted (793 lines); warning/async cleanup across SharpCifs, LiveTV, DLNA etc. |

Command used: `git log --reverse --format='%H|%ad|%an|%s' --date=iso | grep -n -m1 'OpenMedia Dev'` → line 15006 (1-based), i.e. commit #15006 is the first fork commit.

### 1.2 The ADR-0001 "reversal" — the single most important finding

**There is no commit that reverses ADR-0001, because ADR-0001 was never executed.** It is a *stale parallel design document* that was imported into git after the opposite decision had already been made and implemented.

- ADR-0001 text (`9e3d0b28…^:docs/adr/0001-go-greenfield-rewrite.md:1-14`): "**全新重写，Go**，不 fork、不现代化旧代码、不选 C#（ASP.NET Core 8）"; considered option "Fork 3.5.2 渐进现代化（升 .NET 8）…'彻底重构'不成立".
- `docs/spec/core.md:9-24` at the same commit doubles down: Go ≥1.24, `net/http`, pgxpool, sqlc, goja, and **non-goals: 音乐库、照片库、Live TV/DVR、DLNA、Sync…**.
- But the first fork commit's plan (`5b671279…:docs/upgrade-net10-plan.md:1-18,58-79`) is titled "OpenMedia（Emby 3.5.3）→ .NET 10 + PostgreSQL 升级方案", marks "版本：v2（决策已确认，开始执行）", pins baseline `1d7c2ab4bf`, and targets `MediaBrowser.Server`, Kestrel, PostgreSQL, `MediaBrowser.WebDashboard` "原样保留" and `Emby.Dlna / Emby.Notifications / Emby.Drawing` — the exact opposite architecture, *including* the Go spec's "non-goals".
- **Why** (plan §3, `5b671279…:docs/upgrade-net10-plan.md:83-92`): "结论：反编译，不重写" — (1) ilspycmd verified on 6 DLLs (~44k lines), (2) "API 精确兼容…反编译源码 = 零行为漂移", (3) Jellyfin port rejected as 7 years diverged, direct FFmpeg rewrite rejected as no-gain. C01's commit body confirms execution of that rationale.
- **No Go code ever existed in any ref-visible history:** `git rev-list --all --objects | grep -E 'go\.mod|\.go$'` → **empty output**. Caveat: unreachable/dangling objects were not scanned; `git fsck --unreachable` would settle that, but there is no reachable Go artifact to remove.
- ADR-0001 was introduced by a prose-free bulk web commit (`62b4ef3a…`, 315 files / +16743 lines) and deleted 14 days later. **Treat ADRs 0001–0011 as an abandoned design proposal, not as project history.** The surviving `web/` frontend is the only part of the Serata plan that is live — and it is a separate client (`web/README.md:3-9`), not a Go backend.

### 1.3 Recovered ADR inventory (from `9e3d0b28…^`, all deleted in `9e3d0b28…`)

| ADR | Title (short) | Intent / evidentiary value |
|---|---|---|
| 0001 | Go greenfield, no C# fork | Superseded before it was committed; §1.2. |
| 0002 | PostgreSQL only | Survives in code: `C16/C28` removed SQLite (`8a08e9c406…`). |
| 0003 | goja-only plugin system | **Not implemented**; plugin track explicitly cancelled (`16275dfd…`: "删插件轨道（用户拍板不做）"). `InstallationManager.GetAvailablePackages` is now a stub returning empty (`Updates/InstallationManager.cs:163-172`). |
| 0004 | Anchor Emby 4.8 OpenAPI | Repo actually anchors 4.1.1.0 (`d7…` D7, `openapi.json`); the intent (DTO/Id shape compatibility) survives. |
| 0005 | Monorepo server/ + web/ + docs/ | Only `web/` survives on disk. |
| 0006 | Metadata arbitration | Partially implemented in provider manager, not audited here. |
| 0007 | Schema style | PG schema present (`emby_pgsql_dump_schema_optimized.sql`). |
| 0008 | File lifecycle | Go-designed, not verifiable in C# fork. |
| 0009 | Emby migration | Go-designed; C# fork instead migrates by PG schema init. |
| 0010 | Direct-stream-first tiers | Goal survives in streaming code; mechanism is inherited Emby profiles. |
| 0011 | Web UI Emby parity | Live in `web/`; real-Emby corrections recorded in 0015. |
| 0012 | Web home hero (712 lines) | Superseded by later UI iterations. |
| 0013 | Settings honesty | **Directly relevant**: ~50 web settings were write-only; decision "假控件一律删掉不标注即将支持" (`ADR-0013:19-40`). |
| 0014 | Web component/motion stack (287 lines) | Live: Reka UI + motion-v in `web/package.json`. |
| 0015 | Real Emby as contract source (137 lines) | Live process; 3 mock-vs-real field defects documented. |
| 0016 | — | **Missing** (no file in tree; number skipped). |
| 0017 | Perf calibration / CI / `web/dist` gap | Source of Dockerfile's "web/dist 未接线" comment; partly stale (§2, dashboard row). |
| 0018 | Activity-log type cache + PG connection budget | Implemented (`0663ff14…`: local PG `max_connections` 250, matches `docker-compose.yml`). |

Other deleted docs worth knowing: `phase3-design.md` (DVR gate removal, TVDB dead source, LiveTV fixture), `phase4-design.md` (root-GUID incident, ByName write side effects), `provider-validation.md` (TMDb/MusicBrainz/Omdb verification), `upgrade-net10-report.md`. All recoverable with `git show 9e3d0b28…^:<path>`.

---

## 2. Bucket classification

**(A) 有意为之** = live purpose, deliberate/current · **(B) 历史遗留** = inherited, never revisited, low value · **(C) 为解决已不存在的问题而引入** = constraint is gone.

| Subsystem | Bucket | Evidence |
|---|---|---|
| `Emby.Dlna` profile/device model (`src/MediaBrowser.Model/MediaBrowser.Model.Dlna`, 4410 cs lines) | **A** | `ApplicationHost.cs:1040-1041` registers `IDlnaManager`; `BaseStreamingService.cs:482,523` uses `DlnaManager.GetProfile`/`GetDefaultProfile` for *all* HLS/progressive playback; 11 Dlna endpoints PASS in `tests/results/api-matrix.md:107-121`. Deleting it breaks playback, not just UPnP. |
| DLNA SSDP/PlayTo + `RSSDP` | **A (with B core)** | `DlnaEntryPoint.cs:129-176,178-250` starts SSDP handler, publisher and PlayTo from `EnableServer`/`EnablePlayTo`; registered via `ApplicationHost.cs:1816`. `Emby.Dlna/Ssdp/DeviceDiscovery.cs` implements `IDeviceDiscovery` registered at `ApplicationHost.cs:1058`. Original Go spec declared DLNA a non-goal, but the C# fork adopted it — contract-live, not dead. |
| `Mono.Nat` | **C** | Only consumer is `EntryPoints/ExternalPortForwarding.cs:15,78,93,223`; toggle `EnableUPnP` default **true** (`ServerConfiguration.cs:12,162`), set by `StartupWizardService.cs:124` from "EnableAutomaticPortMapping". Built for direct-home-router exposure; deployment is Docker + reverse-proxy style (`Dockerfile:116,126-127`, `docker-compose.yml`). |
| `dashboard-ui` (641 files, 12.9 MB, 7460 JS/HTML/CSS lines) | **A (bridge)** | Root redirect target is hard-coded `"web/index.html"` at `ApplicationHost.cs:993-996` and used at `ServiceRoutingMiddleware.cs:122,177-184`; `/web/{ResourceName*}` route at `DashboardService.cs:65`; plugin config-page HTML rewriting at `DashboardService.cs:170-209`. `tests/results/api-matrix.md:556` expects `GET /web/index.html` 200. Vue UI now has its own route `/app/**` (`WebAppService.cs:18,83-145`, commit `3db82412…`) but root still points at the legacy UI. |
| `web/` Vue 3 client (301 files, 3.7 MB, 16,484 source lines excl. node_modules/dist) | **A** | `web/README.md:3-9`, `WebAppService.cs:18`. Caveat: Docker copies build output to `/opt/openmedia/web` (`Dockerfile:109`) while `WebAppService` reads `$OPENMEDIA_WEBAPP_PATH` or `{ProgramSystemPath}/webapp` (`WebAppService.cs:83-95`); no env var is set in `Dockerfile`/`docker-compose.yml`, so **the Vue UI is not reachable in the shipped image without manual configuration**. |
| ImageMagick backend (817 cs lines) | **A (fallback)** | `ImageEncoderHelper.cs:27-48`: Skia first, ImageMagick second, `NullImageEncoder` only with `-enablegdi`. Removing it eliminates the fallback when `new SkiaEncoder(...)` throws. |
| `Emby.Drawing.Net` | **C — already removed** | `1003e7176c…` deleted 9 files / 793 lines; no reference in `MediaBrowser.sln` or any csproj at HEAD. Its flag `-enablegdi` survives. |
| `UniversalDetector` (9093 cs lines) | **A** | `TextEncoding.cs:58,252` calls `TextEncodingDetect`/`CharsetDetector`; live callers: `SubtitleEncoder.cs:157,547` (SRT/ASS from arbitrary encodings) and `WebSocketConnection.cs:95`. UTF-8-only would corrupt legacy subtitle files; not viable. |
| `LiveTv` (11412 cs lines) | **A** | `ApplicationHost.cs:1049,1426` registers `ILiveTvManager` + `ILiveTvService/ITunerHost/IListingsProvider`; 36 LiveTV rows in `tests/results/api-matrix.md`; deleted phase3 design records an end-to-end XmlTV+M3U recording verified (`424d4ca1de…`); 11-19 commits still refactor it (`d229df02c2…`). The Go "non-goal" is obsolete relative to the actual fork. |
| `BDInfo` (6794) + `DvdLib` (693) | **B** | `ApplicationHost.cs:945` registers `BdInfoExaminer` as `IBlurayExaminer`; consumed by `FFProbeVideoInfo`/`FFProbeProvider` and `MediaEncoder.cs:1029,1075` only for ISO/DVD-Video title/VOB discovery. Unchanged since Emby except SDK conversion (`941f4c7…`). No test-matrix coverage for disc paths. |
| `MediaBrowser.Providers/TV/TheTVDB` (4008) | **C** | External API `api.thetvdb.com` dead (STATUS=000 per deleted `provider-validation.md`, A4); defaults flipped to disabled at `ServerConfiguration.cs:193-195,214-223` with comment "TVDB v3 外部停服…死源默认禁用". Still reached if a user re-enables a provider that cannot work. |
| Other providers (TMDb 1838, Music 3179, Omdb 973, FanArt 613) | **A** | `provider-validation.md` records live end-to-end verification (TMDb movie/series, MusicBrainz, Omdb); `OmdbImageProvider` was repaired to avoid dead `img.omdbapi.com`. |
| `Updates` + `GithubUpdater` (575 + 371 lines) | **C** | `CanSelfUpdate` is hard-coded `false` (`ApplicationHost.cs:144-149`); `CheckForApplicationUpdate` still queries `Emby.Releases` on GitHub (`ApplicationHost.cs:2357-2365`); `SystemUpdateTask.cs:51-88` runs at startup + every 24h, then returns at the `CanSelfUpdate` check; `InstallationManager` package catalogue is a no-op stub (`:168-183`). Fork ships via Docker (`Dockerfile:126-127`). |
| Telemetry / RSS stubs (UsageEntryPoint 133, UsageReporter 129, News 349) | **C — dead shells** | `f9534c5487…` stubbed outbound calls; `UsageEntryPoint.cs:76,97` and `NewsEntryPoint.cs:54-55` say "已移除"; `UsageReporter.cs:20` still contains `https://www.mb3admin.com/admin/`, `NewsEntryPoint.cs:92` still contains the Emby blog RSS URL. Classes remain registered/discoverable. |
| `MediaBrowser.XbmcMetadata` (3695) + `MediaBrowser.LocalMetadata` (3386) | **A** | Assemblies injected into composition at `ApplicationHost.cs:1819,1825`; NFO/XML sidecars are a live user-visible feature of the C# fork (Go spec's "媒体文件整理与重命名" non-goal did not win). |
| `Emby.Notifications` (917) | **A** | `ApplicationHost.cs:1055-1056,1434,1822`; 4 notification endpoints PASS (`tests/results/api-matrix.md` "域：Notifications"). |
| `Emby.Photos` (250) | **B** | Registered at `ApplicationHost.cs:1807` via `PhotoProvider` (`Emby.Photos/PhotoProvider.cs:21`), but `openapi.json` mentions "Photos" once and the API matrix has 0 photo rows. Go spec explicitly listed 照片库 as a non-goal; fork inherited it without follow-up. |

---

## 3. Code that proves its own obsolescence

| Kind | Location | What it admits |
|---|---|---|
| `[Obsolete]` | `src/MediaBrowser.Model/.../ApiMemberAttribute.cs:22,25` | "This is not used for API documentation and is otherwise unused as well." |
| `[Obsolete]` | `src/MediaBrowser.Model/.../IActivityRepository.cs:24,28` | Sync bridges "仅为兼容未迁移插件保留" — no plugins are installed/shipped (ADR-0003 cancelled, package catalogue stub). |
| `[Obsolete]` | `src/MediaBrowser.Common/.../BaseExtensions.cs:21` | "Use LibraryManager.GetNewItemId"; legacy ID helper still compiled. |
| `#if SILVERLIGHT` | `SharpCifs/Util/Sharpen/MD5Managed.cs:15-18` | Dead branch: the class is commented out in the `#else` path; Silverlight never shipped here. |
| `#if TRAVISCI` ×10 + collection | `Emby.Server.Implementations/Networking/IPNetwork/IPNetwork.cs:188,673,744,946,1054,1108,1417,1515,1650`, `IPNetworkCollection.cs:33` | Test-harness shims for a CI service the repo no longer uses (`.github/workflows/ci.yml` instead). |
| `#if NETSTANDARD2_0` | `LiveTv/Listings/SchedulesDirect.cs:671-676,710-715` | "On windows 7 under .net core" workaround; all projects are `net10.0` (`Directory.Build.props`), so both blocks compile away. |
| `#if BETA` | `ApplicationHost.cs:250-253` | Beta release-level switch; build defines never include `BETA`; always returns `Release`. |
| Comment | `ManagedFileSystem.cs:27-28` | "On Linux with **mono**, this needs to be true or symbolic links are ignored" — drives `enableSeparateFileAndDirectoryQueries`, passed literally `true` at `Program.cs:92`; no Mono runtime remains. |
| Dead flag | `ImageEncoderHelper.cs:27,48` | `-enablegdi` is the only place the string appears in the tree; after `Emby.Drawing.Net` was deleted it no longer enables GDI — it just selects `NullImageEncoder` (no images at all). |
| Write-only config | `ServerConfiguration.cs:40-50,102,116` | `CameraUploadUpgraded`, `CollectionsUpgraded`, `SubtitlePermissionsUpgraded`, `CollapseVideoFolders`, `EnableOriginalTrackTitles`, `LogAllQueryTimes`, `SchemaVersion`, `CodecsUsed` — each referenced only in its own declaration (grep count = 1 file). |
| Migration code for impossible states | `ServerConfigurationManager.cs:196-250` | `SetOptimalValues()` flips eight one-way defaults and marks `CameraUploadUpgraded`/`CollectionsUpgraded` on every fresh config; camera upload was a dead Emby Sync feature, removed in C14 (`d4d05b5dd…`). |
| Dead capability exposed to clients | `ApplicationHost.cs:2348-2389` + `MediaBrowser.Api/PackageService.cs:162` | `CheckForApplicationUpdate` fetches upstream Emby release metadata even though `CanSelfUpdate == false`. |
| Stubbed external calls | `InstallationManager.cs:168-183`, `UsageEntryPoint.cs:76,97`, `NewsEntryPoint.cs:54-55` | Explicit "OpenMedia（去 Emby 化）…已移除/恒返回空列表" comments; the surrounding classes, timers and interfaces remain. |
| Legacy TODO | `MetadataService.cs:798` | "If the new metadata from above has some blank data, this can cause old data to get filled…" — inherited Emby merge semantics, unresolved. |

`#if` census: `grep -rh --include='*.cs' '^\s*#if' … | sed … | sort | uniq -c` → DEBUG 53, TRAVISCI 10, NETSTANDARD2_0 2, SILVERLIGHT 1, BETA 1. No `MONO`, `__MonoCS__`, `WINDOWS_UWP` or Xamarin `#if` remain (only namespace names like `MediaBrowser.Server.Mono` survive).

---

## 4. Baggage quantification

Count method: `find <dir> -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -print0 | xargs -0 cat | wc -l`; JS/HTML/CSS counted separately for `dashboard-ui`/`web`. "Reachable" verified by the cited registration/route, not by file presence.

| Subsystem | Lines (files) | Reachable from entry point? | What breaks if removed | Recommendation |
|---|---:|---|---|---|
| `IO/SharpCifs` (vendored SMB1) | **38,022** (223 cs) | Yes — `ManagedFileSystem.cs:46,962-1047` (~28 `IsEnabledForPath` branches); constructor at `Program.cs:92`. | Non-Windows library paths beginning `smb://` or `\\UNC` (explicitly handled in `LibraryMonitor.cs:304-310`) stop resolving. Windows paths unaffected (`SharpCifsFileSystem.cs:22-25`). | **Remove / extract to opt-in plugin.** SMB1-only (`SmbComNegotiate.cs:24` offers only `NT LM 0.12`; no SMB2/3 string anywhere); vendored by `2fc71fb022…` (2017-04-01 "stub out cifs support"). Migrate users to OS-mounted SMB. |
| `dashboard-ui` | 7,460 code lines / **641 files / 12.9 MB** (bower_components: 362 files, 2327 lines, ~6 MB) | Yes — root redirect `ApplicationHost.cs:996`; `/web/**` `DashboardService.cs:65`; plugin config pages `:170-209`. | `/` 404, `/web/index.html` and all legacy assets 404, plugin config-page rewriting fails, API matrix P0 case fails. | **Keep now; remove after `/app` becomes the root redirect.** Its own Dockerfile calls it the still-live root (`Dockerfile:8-14`). |
| `BDInfo` + `DvdLib` | **7,487** (33 cs) | Yes — `ApplicationHost.cs:945`, `FFProbeVideoInfo.cs`, `MediaEncoder.cs:1029,1075`. | ISO/Blu-ray chapter/title and DVD VOB discovery regress. | **Extract to plugin** if disc ISOs are not a supported v1 use case; otherwise keep deliberately with tests. |
| `TV/TheTVDB` providers | **4,008** (6 cs) | Registered but default-disabled (`ServerConfiguration.cs:193-195,214-223`). | Users who re-enable TVDB get nothing (API dead); TMDb-supplied `Tvdb` IDs and locally cached series XML still used by `MissingEpisodeProvider` / FanArt. | **Remove fetchers, keep ID passthrough and local-XML readers.** |
| `RSSDP` + `Mono.Nat` | 3,465 + **1,594** (37 cs) | RSSDP: `DlnaEntryPoint.cs:186,244`, `ApplicationHost.cs:1058`. Mono.Nat: `ExternalPortForwarding.cs:93`. | Removing RSSDP kills DLNA discovery **and** generic `IDeviceDiscovery`; removing Mono.Nat kills only UPnP/PMP port mapping. | **Keep RSSDP**; **remove Mono.Nat** (or gate behind explicit opt-in; `EnableUPnP` is currently default-on). |
| `LiveTv` | 11,412 (23 cs) | Yes — `ApplicationHost.cs:1049,1426`. | 36 tested API endpoints and DVR recording chain disappear. | **Keep** (A); the Go spec's non-goal was reversed in practice. |
| `UniversalDetector` | 9,093 (33 cs) | Yes — `TextEncoding.cs:58,252` → `SubtitleEncoder.cs:157,547`, `WebSocketConnection.cs:95`. | Legacy-encoded subtitles and websocket text frames break. | **Keep**; optionally replace with a maintained charset library, not deletion. |
| `src/MediaBrowser.Model/…Dlna` + `Emby.Dlna` profiles | 4,410 + part of 16,251 | Yes — `BaseStreamingService.cs:482,523`. | Playback profile selection for every client breaks. | **Keep** (A). |
| `Emby.Dlna` SSDP/PlayTo remainder | ~16,251 total (89 cs) | Yes — `ApplicationHost.cs:1816`, `DlnaEntryPoint.cs:129-176`. | DLNA server/PlayTo endpoints 404. | **Keep unless product explicitly drops DLNA** (11 endpoints currently tested). |
| `Updates` + `Common.Updates` + `SystemUpdateTask` | 575 + 371 + ~50 | Yes (startup/24h task) but no-op by design | `/System/Info` fields and update-check endpoint shapes change; removes an upstream Emby network call. | **Remove or disable the check task** (C). |
| Telemetry/news stubs | 133 + 129 + 349 = 611 | Discoverable but behaviourally stubbed | Nothing functional; only class/route shape. | **Delete shells** (C). |
| `XbmcMetadata` / `LocalMetadata` | 3,695 / 3,386 (42 cs) | Yes — `ApplicationHost.cs:1819,1825`. | NFO/XML read+write features regress. | **Keep** (A). |
| `Notifications` | 917 (5 cs) | Yes — `ApplicationHost.cs:1055,1434,1822`. | 4 tested endpoints regress. | **Keep** (A). |
| `Emby.Photos` | 250 (2 cs) | Yes — `ApplicationHost.cs:1807`. | Photo metadata provider disappears; no API-matrix coverage. | **Remove or extract** if photo library is confirmed out of scope (needs product decision). |
| `Emby.Drawing.ImageMagick` | 817 (6 cs) | Fallback — `ImageEncoderHelper.cs:38-45`. | No image processing when Skia constructor fails. | **Keep** until Skia is proven universal on all target images. |
| Dead config flags + migration flags | ~15 properties / ~55 lines | Read in `ServerConfigurationManager.cs:196-250` only | Fresh-install config shape changes (XML compatibility only). | **Remove** after one config-version bump. |

**Aggregate removable/extractable (conservative):** SharpCifs 38,022 + TVDB 4,008 + BDInfo/DvdLib 7,487 + Mono.Nat 1,594 + Updates/GithubUpdater 946 + telemetry/news 611 ≈ **52,668 C# lines**, plus 12.9 MB of legacy dashboard assets (conditional on re-pointing `/`).

---

## 5. What could not be determined

| Question | Why unresolved | What would settle it |
|---|---|---|
| Was there ever a Go `server/` tree in an *unreachable* commit? | `git rev-list --all --objects` only covers refs. No Go blob is reachable from any ref. | `git fsck --unreachable --no-reflogs` + blob scan, or the original design session/branch (ADR-0005 mentions `server/`, no such path exists at any ref). |
| Who decided Go→C# and when, originally? | ADR-0001 has no author/date metadata; the file entered git in an unrelated bulk commit. The *confirmed* C# decision is dated 2026-10-13 in the plan. | Authorship of the pre-import docs (outside git), or the deleted branch `backup-migration-20261013-210711` named in plan line 17/54 — not present in `git branch -a`. |
| Actual production usage of SMB1 paths, ISO libraries, LiveTV, DLNA, Photos | Git/cgrep show capability, not telemetry. | Production logs/config (`system.xml` library paths, `MediaInfo` VideoType counts, DLNA client sessions). |
| Whether removing TVDB fetchers breaks cached series XML readers | Code reads `GetSeriesDataPath` from local cache; provenance of existing caches is deployment-specific. | Inventory a real `ProgramData/metadata/library/series` tree. |
| Whether `/app` is reachable in Docker as shipped | `WebAppService` expects `ProgramSystemPath/webapp` or env override; Dockerfile copies to `/opt/openmedia/web`. ProgramSystemPath is `appFolderPath` (`BaseApplicationPaths.cs:19`), i.e. `/opt/openmedia/server`. | Run the image and curl `/app/`; no Docker daemon was available in this audit. |

**Bottom line:** the largest genuine baggage is not the C# fork itself — it is (a) the abandoned Go design documents that impersonate architecture decisions, (b) a 38k-line SMB1 client reachable only through a path-prefix trick, (c) an update subsystem that cannot update anything, and (d) a legacy dashboard that is still the root page while its replacement is only half-wired.
