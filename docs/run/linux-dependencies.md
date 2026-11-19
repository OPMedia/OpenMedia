# Linux 运行依赖清单

面向：下载 `openmedia-*-linux-*.tar.gz` 自己跑的用户，以及维护 Dockerfile 的人。

## 一句话

**产物只依赖 glibc 核心库**，不需要装 Skia / ImageMagick 的系统包 —— 它们作为原生库随包分发
（`libSkiaSharp.so`、`Magick.Native-Q16-x64.dll.so`），实测只链接 glibc：

```
ldd libSkiaSharp.so                    → libc libm libpthread libdl
ldd Magick.Native-Q16-x64.dll.so       → libc libm libpthread libresolv
```

但**托管层**还有几样是必须在系统里的，下面逐项列出并说明为什么。

## 必需

| 依赖 | 为什么 | 缺了会怎样 |
|---|---|---|
| glibc 2.31+ | .NET 运行时 | 进程起不来 |
| libssl3 / libcrypto3 | .NET 的 TLS（HTTPS、TMDb/TVDB 这些外网请求） | 外网请求全部失败 |
| libicu（约 30MB） | .NET 的全球化（日期格式、排序、字符串比较） | 启动即崩，提示找不到 ICU；或设 `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` 换取"能跑但排序/日期不对" |
| zlib | 压缩 | 解压/图片编码异常 |

**ffmpeg / ffprobe**：**不随包分发**，需要自己提供（`ffmpeg` 在 PATH 里，或在设置里指定路径）。
建议用 [jellyfin-ffmpeg](https://github.com/jellyfin/jellyfin-ffmpeg) —— 我们的 HDR 色调映射
依赖它特有的 `tonemapx` 滤镜；通用发行版 ffmpeg 也能跑，但没有 `tonemapx`，
HDR→SDR 会走 `tonemap` 兜底，画质略差。

## 字幕渲染相关（很多发行版默认没有）

| 依赖 | 为什么 |
|---|---|
| `fontconfig` | libass 烧录 ASS/SSA 时按**字体名**查找字体 |
| 至少一套字体（如 `fonts-dejavu-core`） | 没有字体时，字幕/文字会渲染成方块 |

Debian/Ubuntu：
```bash
apt-get install -y fontconfig fonts-dejavu-core
```
Arch：`pacman -S fontconfig ttf-dejavu`
Alpine：`apk add fontconfig ttf-dejavu`

自带的字体放在 `ProgramData/fonts/`，服务端会把它作为 `fontsdir` 传给 ffmpeg。

## 不需要（但旧文档/旧镜像里可能有）

| 依赖 | 说明 |
|---|---|
| `libgdiplus` | `System.Drawing` 的 Linux 实现。**全仓已无一处 `using System.Drawing`**，理论上可移除。Docker 镜像里暂时留着（省得某个间接依赖炸），确认无影响后可删，省约 40MB |
| `libfontconfig` 的 dev 包 | 我们用 `SkiaSharp.NativeAssets.Linux.NoDependencies`，它自带静态 fontconfig，不依赖系统的 |
| mono / mono-devel | 历史遗留，早已不需要 |

## Docker 镜像

镜像里已经装好：`ca-certificates curl tzdata fontconfig fonts-dejavu-core libgdiplus`，
外加 jellyfin-ffmpeg 8.1.3-2 便携包（amd64/arm64，带 sha256 校验）。

镜像的 `HEALTHCHECK` 打 `/health/live`（不查数据库），所以数据库没起来时容器不会被判死。
编排里判断"能用"应该探 `/health/ready`。
