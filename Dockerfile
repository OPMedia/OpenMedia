# ============================================================
# OpenMedia 单镜像（amd64 + arm64）
#
# 对应 docs/spec/core.md 的要求：
#   - amd64/arm64 单镜像
#   - 官方静态 ffmpeg 打进镜像（版本锁死）
#   - 后端托管 web 静态产物（同源）
#
# ⚠️ 已知缺口（docs/adr/0017-ci-perf-gate.md 有完整记录）：
#   spec 写了"后端托管 web 静态产物"，但**实现是零**——
#   MediaBrowser.Server.csproj 里没有任何引用 web/dist 的逻辑（已 grep 确认），
#   新前端目前只能靠 Vite 代理访问。
#   所以本 Dockerfile 只负责把 web/dist 拷进镜像的备用路径 /opt/openmedia/web，
#   **并明确标注"待人接线"**。不要假装它已经能用：
#   镜像起来后，根路径 / 仍然是旧 dashboard-ui，不是新前端。
#   接线需要后端加静态文件托管（UseStaticFiles / 嵌入式资源 / SPA fallback），
#   那是 src/ 的改动，不在本次 Dockerfile 范围内。
#
# 构建命令（GitHub runner 或任何有 docker 的机器；本机无 docker）：
#   docker buildx build --platform linux/amd64,linux/arm64 -t openmedia:test --load .
#   # 只验本架构：
#   docker build -t openmedia:test .
#
# 验证步骤（GitHub runner 上）：
#   docker run --rm -p 8096:8096 openmedia:test &
#   curl -fsS http://127.0.0.1:8096/emby/system/info/public     # 后端活着
#   docker run --rm --entrypoint /opt/openmedia/ffmpeg/ffmpeg openmedia:test -version
#   docker run --rm --entrypoint ls openmedia:test -l /opt/openmedia/web
#     # 期望看到 index.html + assets/（备用路径，说明后端还没接线）
# ============================================================

# ---- 阶段 1：构建 web 静态产物（Node 24 + pnpm，与 CI job 3 同口径）----
FROM node:24-bookworm-slim AS web-build
RUN npm install -g pnpm@10
WORKDIR /src/web
COPY web/package.json web/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
COPY web/ ./
RUN pnpm type-check && pnpm build-only
# 产物：/src/web/dist

# ---- 阶段 2：取官方静态 ffmpeg（版本锁死，amd64 / arm64 分架构）----
# 版本锁死理由（docs/spec/core.md 要求）：
#   转码行为对 ffmpeg 版本极敏感，同版本才能复现问题。
#   升级版本是显式决策，改这两行 + 改 FFMpegInstallInfo.Version，不要靠"latest"。
# 为什么不直接 FROM 官方 ffmpeg 镜像：官方镜像没有 amd64/arm64 合一的单 tag，
#   buildx 多平台下混用 base 会退化成单架构。所以用 downloader 阶段按
#   TARGETARCH 拉对应的静态构建（静态 = 不依赖镜像内 glibc 版本）。
ARG FFMPEG_VERSION=7.1.1
ARG TARGETARCH
RUN apt-get update && apt-get install -y --no-install-recommends curl xz-utils ca-certificates \
    && rm -rf /var/lib/apt/lists/*
# 静态构建来源（都不依赖发行版库，glibc/musl 均可跑）：
#   amd64 -> johnvansickle static build（glibc 静态）
#   arm64 -> johnvansickle static build（同源，锁同版本）
# 版本锁死在 FFMPEG_VERSION，改版本 = 改这一处 ARG + 文档。
RUN set -eux; \
    case "${TARGETARCH}" in \
      amd64) FF_ARCH=amd64 ;; \
      arm64) FF_ARCH=arm64 ;; \
      *) echo "unsupported TARGETARCH=${TARGETARCH}" >&2; exit 1 ;; \
    esac; \
    mkdir -p /out; \
    curl -fsSL "https://johnvansickle.com/ffmpeg/releases/ffmpeg-${FFMPEG_VERSION}-${FF_ARCH}-static.tar.xz" \
      | tar -xJ -C /out --strip-components=1 \
        "ffmpeg-${FFMPEG_VERSION}-${FF_ARCH}-static/ffmpeg" \
        "ffmpeg-${FFMPEG_VERSION}-${FF_ARCH}-static/ffprobe"; \
    chmod +x /out/ffmpeg /out/ffprobe; \
    /out/ffmpeg -version | head -1

# ---- 阶段 3：.NET 发布（Release，锁 .NET 10）----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src
COPY . .
RUN dotnet publish MediaBrowser.Server/MediaBrowser.Server.csproj \
      -c Release -o /publish --nologo

# ---- 阶段 4：运行时镜像 ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# 运行期依赖：ffmpeg 静态构建还需要 libstdc++ 之外的少量库（curl/xz 已在 fetch 阶段用完）
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates libgdiplus tzdata \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /opt/openmedia

# 3a. .NET 应用本体
COPY --from=dotnet-build /publish/ /opt/openmedia/server/

# 3b. 静态 ffmpeg + ffprobe。
#     查找优先级（Emby.Server.Implementations/FFMpeg/FFMpegLoader.cs:49）：
#       1. ProgramSystemPath/ffmpeg   <- 用这个（与 MediaBrowser.Server.dll 同级，零配置）
#       2. -ffmpeg / -ffprobe 启动参数
#       3. ProgramDataPath/ffmpeg/{version}/
#     Linux 分支下 FFMpegFilename = ffmpeg（无 .exe），所以这里不能加后缀。
#     代码里 Version 硬编码 20170308，那是下载用的版本号而非路径要求；
#     打在 ProgramSystemPath 正好绕开它，否则它会去
#     ProgramDataPath/ffmpeg/20170308/ 找一个 2017 年的旧版。
ARG FFMPEG_VERSION=7.1.1
COPY --from=ffmpeg-fetch /out/ffmpeg  /opt/openmedia/server/ffmpeg
COPY --from=ffmpeg-fetch /out/ffprobe /opt/openmedia/server/ffprobe
RUN chmod +x /opt/openmedia/server/ffmpeg /opt/openmedia/server/ffprobe \
    && /opt/openmedia/server/ffmpeg -version | head -1

# 3c. web 静态产物 —— **备用路径，后端尚未接线，见文件头缺口说明**
#     接线前这个目录只是躺着，根路径 / 仍是旧 dashboard-ui。
#     接线后（后端加静态文件托管）应改为 COPY 到 server/wwwroot 之类。
COPY --from=web-build /src/web/dist/ /opt/openmedia/web/

# 数据目录（PG 连接串走 OPENMEDIA_PGCONN 环境变量，见下方 ENV）
RUN mkdir -p /opt/openmedia/data
VOLUME ["/opt/openmedia/data"]

# 默认指向容器编排里的 postgres 服务名；本机 docker compose 里是 postgres
ENV OPENMEDIA_PGCONN="Host=postgres;Port=5432;Database=openmedia;Username=postgres;Password=123456" \
    DOTNET_RUNNING_IN_CONTAINER=true \
    ASPNETCORE_URLS=http://+:8096

EXPOSE 8096

# 健康检查（与 CI/本机的健康口径一致：/emby/system/info/public）
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=5 \
  CMD curl -fsS http://127.0.0.1:8096/emby/system/info/public || exit 1

ENTRYPOINT ["dotnet", "/opt/openmedia/server/MediaBrowser.Server.dll"]
CMD ["-p", "8096", "-programdata", "/opt/openmedia/data"]
