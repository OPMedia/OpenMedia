# ============================================================
# OpenMedia 单镜像（amd64 + arm64）
#
# 对应 docs/spec/core.md 的要求：
#   - amd64/arm64 单镜像
#   - ffmpeg 打进镜像（版本锁死 + sha256 校验）
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
#   docker run --rm --entrypoint /opt/openmedia/server/ffmpeg openmedia:test -version
#   docker run --rm --entrypoint /opt/openmedia/server/ffmpeg openmedia:test -filters | grep -E 'tonemapx|zscale|libplacebo'
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

# ---- 阶段 2：取 jellyfin-ffmpeg GPL 便携包（版本锁死 + sha256，amd64 / arm64 分架构）----
# 选型依据（2026-11-19 实测，详见提交说明）：
#   - jellyfin-ffmpeg v8.1.3-2：实测 -hwaccels = cuda/vaapi/qsv/drm/opencl/vulkan/amf，
#     nvenc/qsv/vaapi/amf 编码器齐全；滤镜含 tonemap/tonemapx/zscale/tonemap_opencl/
#     tonemap_vaapi/libplacebo。它带 HDR 相关 patch（tonemapx、tonemap_cuda），
#     且 release tag 不可变、amd64/arm64 都有官方便携包，所以选它。
#   - BtbN/FFmpeg-Builds：能力基本齐全但无 tonemapx/tonemap_cuda；"latest" 标签可变
#     （不可变 tag 是 autobuild-*，资产名还带 commit hash）。
#   - 发行版自带 ffmpeg：依赖系统共享库，发行版版本不可控（Debian bookworm 只有 5.1）。
# 许可证：GPLv3（该构建带 --enable-gpl --enable-version3）。服务端仅以独立进程调用，
#   不与其链接；分发镜像的上游源码/补丁见 https://github.com/jellyfin/jellyfin-ffmpeg。
# 下载后必须过 sha256，校验失败（篡改/CDN 脏缓存/URL 变动）直接让 docker build 失败。
FROM debian:bookworm-slim AS ffmpeg-fetch
ARG FFMPEG_VERSION=8.1.3-2
ARG TARGETARCH
ARG FFMPEG_SHA256_AMD64=1df84d04c4322902dba81c290508cdf3d89524d210b9fe03154bea6c4e3bfbb6
ARG FFMPEG_SHA256_ARM64=6522585818059cc886363fb1dee86ac9b8579fec3b914337fb92974ef9eb9884
RUN apt-get update && apt-get install -y --no-install-recommends curl xz-utils ca-certificates \
    && rm -rf /var/lib/apt/lists/*
RUN set -eux; \
    case "${TARGETARCH}" in \
      amd64) FF_ARCH=linux64; FF_SHA="${FFMPEG_SHA256_AMD64}" ;; \
      arm64) FF_ARCH=linuxarm64; FF_SHA="${FFMPEG_SHA256_ARM64}" ;; \
      *) echo "unsupported TARGETARCH=${TARGETARCH}" >&2; exit 1 ;; \
    esac; \
    mkdir -p /out; \
    curl -fsSL -o /tmp/ffmpeg.tar.xz \
      "https://github.com/jellyfin/jellyfin-ffmpeg/releases/download/v${FFMPEG_VERSION}/jellyfin-ffmpeg_${FFMPEG_VERSION}_portable_${FF_ARCH}-gpl.tar.xz"; \
    printf '%s  %s\n' "${FF_SHA}" /tmp/ffmpeg.tar.xz | sha256sum -c -; \
    tar -xJf /tmp/ffmpeg.tar.xz -C /out ffmpeg ffprobe; \
    rm -f /tmp/ffmpeg.tar.xz; \
    curl -fsSL -o /out/jellyfin-ffmpeg.LICENSE.md \
      "https://raw.githubusercontent.com/jellyfin/jellyfin-ffmpeg/v${FFMPEG_VERSION}/LICENSE.md"; \
    chmod +x /out/ffmpeg /out/ffprobe; \
    /out/ffmpeg -version | head -1

# ---- 阶段 3：.NET 发布（Release，锁 .NET 10）----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src
COPY . .

# 凭据在构建期注入（见 BuildSecrets.targets）。不传就是空串，相关 provider 自动禁用；
# 值来自 CI secrets，永远不进 git。
ARG TVDB_API_KEY=""
ARG TMDB_API_KEY=""
ARG FANART_API_KEY=""

RUN dotnet publish MediaBrowser.Server/MediaBrowser.Server.csproj \
      -c Release -o /publish --nologo \
      -p:TvdbApiKey="$TVDB_API_KEY" \
      -p:TmdbApiKey="$TMDB_API_KEY" \
      -p:FanartApiKey="$FANART_API_KEY"

# ---- 阶段 4：运行时镜像 ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# 运行期依赖：jellyfin-ffmpeg 便携包只链接 glibc 基础库；curl 供下方 HEALTHCHECK 使用。
# curl/xz-utils/构建工具只存在于 fetch 阶段，不进入最终镜像。
# 运行期依赖清单（每一项都有对应理由，见 scripts/docker-deps.md）：
#   ca-certificates  TLS 校验
#   curl             HEALTHCHECK 用
#   tzdata           时区（EPG/日期显示）
#   fontconfig       字体解析 —— libass 烧录 ASS/SSA 字幕时靠它按名字找字体
#   fonts-dejavu-core 兜底字体 —— 镜像里没有字体时，字幕会渲染成方块
#   libgdiplus       目前**没有任何代码用 System.Drawing**（全仓 0 处 using），
#                    留着是为了兼容某些间接依赖；确认无影响后可移除（省 ~40MB）
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates curl tzdata fontconfig fonts-dejavu-core libgdiplus \
    && rm -rf /var/lib/apt/lists/*

LABEL org.opencontainers.image.licenses="GPL-3.0-or-later (bundled jellyfin-ffmpeg binary)" \
      org.opencontainers.image.source="https://github.com/jellyfin/jellyfin-ffmpeg"

WORKDIR /opt/openmedia

# 3a. .NET 应用本体
COPY --from=dotnet-build /publish/ /opt/openmedia/server/

# 3b. jellyfin-ffmpeg 便携包（ffmpeg + ffprobe）。
#     查找优先级（Emby.Server.Implementations/FFMpeg/FFMpegLoader.cs:49）：
#       1. ProgramSystemPath/ffmpeg   <- 用这个（与 MediaBrowser.Server.dll 同级，零配置）
#       2. -ffmpeg / -ffprobe 启动参数
#       3. ProgramDataPath/ffmpeg/{version}/
#     Linux 分支下 FFMpegFilename = ffmpeg（无 .exe），所以这里不能加后缀。
#     代码里 Version 硬编码 20170308，那是下载用的版本号而非路径要求；
#     打在 ProgramSystemPath 正好绕开它，否则它会去
#     ProgramDataPath/ffmpeg/20170308/ 找一个 2017 年的旧版。
COPY --from=ffmpeg-fetch /out/ffmpeg  /opt/openmedia/server/ffmpeg
COPY --from=ffmpeg-fetch /out/ffprobe /opt/openmedia/server/ffprobe
COPY --from=ffmpeg-fetch /out/jellyfin-ffmpeg.LICENSE.md /opt/openmedia/server/THIRD_PARTY_LICENSES/jellyfin-ffmpeg.LICENSE.md
RUN chmod +x /opt/openmedia/server/ffmpeg /opt/openmedia/server/ffprobe \
    && /opt/openmedia/server/ffmpeg -version | head -1 \
    && /opt/openmedia/server/ffmpeg -filters | grep -q tonemapx

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
