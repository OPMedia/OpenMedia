#!/usr/bin/env python3
"""依赖漏洞门禁。

读 `dotnet list <sln> package --vulnerable --include-transitive --format json` 的输出，
有任何已知漏洞就以非 0 退出。

为什么要脚本而不是在 workflow 里 grep：
  1. 之前那版是 bash 的 `grep -q '>'`，却跑在 Windows runner 的 pwsh 里 —— 直接语法错误。
  2. 用文本匹配判断"有没有漏洞"依赖输出语言和表格格式；JSON 是稳定契约。
  3. 抽成脚本可以本地跑，不需要推 CI 才知道对不对。

用法：
    dotnet list MediaBrowser.sln package --vulnerable --include-transitive --format json > audit.json
    python3 scripts/ci/audit-vulnerabilities.py audit.json

退出码：0 = 无已知漏洞；1 = 有；2 = 输入无法解析。
"""

from __future__ import annotations

import json
import sys


def collect(data: dict) -> list[tuple[str, str, str, str, str]]:
    """返回 (项目, 框架, 包, 版本, 严重级别/公告) 列表。"""
    hits: list[tuple[str, str, str, str, str]] = []

    for project in data.get("projects") or []:
        project_path = project.get("path") or "<unknown>"

        for framework in project.get("frameworks") or []:
            framework_name = framework.get("framework") or "<unknown>"

            for kind in ("topLevelPackages", "transitivePackages"):
                for package in framework.get(kind) or []:
                    for vuln in package.get("vulnerabilities") or []:
                        hits.append(
                            (
                                project_path,
                                framework_name,
                                package.get("id") or "<unknown>",
                                package.get("resolvedVersion") or "?",
                                vuln.get("severity") or "unknown",
                                vuln.get("advisoryurl") or vuln.get("advisoryUrl") or "",
                            )
                        )

    return hits


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print("用法: audit-vulnerabilities.py <audit.json>", file=sys.stderr)
        return 2

    try:
        with open(argv[1], encoding="utf-8") as handle:
            data = json.load(handle)
    except (OSError, json.JSONDecodeError) as error:
        print(f"::error::无法解析审计输出: {error}", file=sys.stderr)
        return 2

    hits = collect(data)

    if not hits:
        print("没有已知漏洞的依赖包。")
        return 0

    # 同一条公告会在多个项目里重复出现，去重后输出，避免刷屏
    seen: set[tuple[str, str, str, str, str]] = set()
    for project, framework, package, version, severity, advisory in hits:
        key = (package, version, severity, advisory, framework)
        if key in seen:
            continue
        seen.add(key)
        print(
            f"::error file={project}::{package} {version} [{framework}] "
            f"{severity} {advisory}".rstrip()
        )

    print(f"\n共 {len(hits)} 处命中（去重后 {len(seen)} 条包/公告组合）。", file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
