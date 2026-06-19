# CleanScope

[![CI](https://github.com/David-Chen31/CleanScope/actions/workflows/ci.yml/badge.svg)](https://github.com/David-Chen31/CleanScope/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/David-Chen31/CleanScope)](https://github.com/David-Chen31/CleanScope/releases/latest)
[![License: MIT](https://img.shields.io/github/license/David-Chen31/CleanScope)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6)
![Tests](https://img.shields.io/badge/tests-309%20passing-2ea44f)

**English** | [中文](#中文)

> The Windows C-drive cleaner that **explains every file before it deletes** — Recycle Bin only, never permanent. *(AI optional, on-demand, advisory.)*

## Screenshots

<!-- 把截图/GIF 放到 assets/ 后取消下面注释即可显示。建议: 概览、资源管理器目录树、空间地图 treemap，外加一段 ~15s 演示 GIF。 -->
<!--
![Overview](assets/overview.png)
![Explorer tree](assets/explorer.png)
![Space treemap](assets/treemap.png)
-->

> 📸 *Screenshots / demo GIF coming soon — suggested recording: scan → space map → explorer tree → right-click "Move to Recycle Bin". 截图与演示 GIF 即将补充。*

CleanScope scans your disk and explains every file/directory by its origin, ownership, dependencies and risk, gives graded recommendations, and **leaves the final delete decision entirely to you**. It prioritizes answering "what is this, who owns it, what happens if I delete it"; when you confirm a cleanup, it **only moves cleanable items (A/B) to the Recycle Bin (recoverable)** — never permanent deletion, never touching system-critical files.

## Four Product Tenets (design red lines)

1. **AI never deletes important C-drive files** — AI only explains/investigates; it can never trigger deletion.
2. **Explain first** — surface a file's origin / dependencies / risk rather than acting on it directly.
3. **The user makes the final delete decision** (deletion requires an explicit click + two-step confirmation).
4. The product must be **safe, explainable, testable, extensible**.

Deletion model (S-E): the single disk-mutating safety gate **only admits items in the "cleanable" bucket (A/B) that are non-system-critical, non-container and not in use**, and deletion **only moves them to the Recycle Bin (recoverable)** — there is **no permanent-delete API anywhere in the codebase** (the one Recycle-Bin delete is isolated in a single file, with a static test positively asserting "Recycle Bin only, never permanent", gated in CI). At its core it remains an analyzer: scan → evidence → rules → attribution → risk → decision → report.

## How CleanScope compares

| | **CleanScope** | CCleaner | WizTree | BleachBit |
|---|:---:|:---:|:---:|:---:|
| Explains each file's origin & purpose | ✅ | ⚠️ categories only | ❌ size only | ⚠️ |
| Can it permanently delete? | **❌ Recycle Bin only** | ✅ | — (viewer) | ✅ |
| Risk grading A–E + system-critical blacklist | ✅ | ⚠️ | ❌ | ⚠️ |
| Whole-disk tree (WizTree-style) | ✅ | ❌ | ✅ | ❌ |
| AI explanations (on-demand, advisory, never deletes) | ✅ | ❌ | ❌ | ❌ |
| Local-only by default (no telemetry) | ✅ | ❌ | ✅ | ✅ |
| Open source | ✅ MIT | ❌ | ❌ | ✅ |

> WizTree is a fast size *viewer*, not a cleaner; CCleaner/BleachBit do delete permanently. CleanScope's niche is **safe + explainable**: it tells you what each thing is and physically can't permanently delete.

---

## Requirements

- **Windows 10/11** (some capabilities rely on Win32: registry, Restart Manager in-use detection, Authenticode signature reading)
- **.NET 8 SDK** (`dotnet --version` ≥ 8.0)
- The WPF desktop app needs the Windows Desktop Runtime (installed together with the .NET 8 SDK)

## Build

```bash
git clone https://github.com/David-Chen31/CleanScope.git
cd CleanScope
dotnet build CleanScope.sln -c Release
```

---

## Usage 1: Command Line (CLI)

The fastest way to see the core value: scan a path, output graded risk and a Markdown report. **Read-only throughout — deletes nothing.**

```bash
# Scan the user cache dir, print graded stats and Top 10
dotnet run --project src/CleanScope.App.Console -- scan "%LocalAppData%"

# Scan and export a Markdown report
dotnet run --project src/CleanScope.App.Console -- scan "C:\SomeFolder" --report report.md --top 200

# Enable AI explanations (desensitized before leaving the machine; requires a key, see below)
dotnet run --project src/CleanScope.App.Console -- scan "%LocalAppData%" --ai --report report.md
```

Options:

| Option | Description |
|---|---|
| `scan <path>` | Root path to scan (required) |
| `--report <file>` | Export a Markdown report to the given file |
| `--top <N>` | Keep the top N largest items (default 100) |
| `--admin` | Admin mode, widens scan coverage (run the terminal as administrator) |
| `--sanitize` | Desensitize usernames in report paths (for sharing) |
| `--ai` | Enable AI explanations (needs a key; skipped automatically if unconfigured — pure local rules/risk) |
| `--rules <dir>` | Rule-pack directory (defaults to `rules/` next to the output dir or repo root) |

Exit codes: `0` success / `2` usage error / `3` rule load failure / `4` path error / `1` other error.

---

## Usage 2: Desktop App (WPF)

```bash
dotnet run --project src/CleanScope.App.Wpf
```

Main pages:

1. **Overview / Scan** — C-drive capacity, pick a path and scan; afterwards shows root aggregate usage, cleanable estimate (A+B), high-risk count, Top 10.
2. **Explorer (whole-disk tree)** — browse the entire disk like a directory tree: expand/collapse, size + share bars, sorted by size, per-node origin/purpose/risk. Right-click a row to copy path, copy purpose, open location, or move to the Recycle Bin.
3. **Space Map** — treemap (area = size, color = risk) to drill into "where did my space go".
4. **File List** — path / size / ownership / risk / recommendation, grouped into four buckets (✅ Cleanable / ⚠ Caution / 🛑 Keep / 🗂 Container).
5. **By Software** — aggregate "who took my space and how much is cleanable" per owning app.
6. **Detail** — properties, risk assessment, **evidence chain (facts vs AI guesses, visually distinguished)**, attribution candidates, and **on-demand** AI explanation (only when you click); **cleanable items (A/B) offer "🗑 Move to Recycle Bin (recoverable)"** — two-step confirm + gate re-check + audit-first, Recycle Bin only; high-risk (D/E) / container / system-critical have no delete entry, only the reason.
7. **Report / Ignore List** — export Markdown reports; manage the global ignore list (local only).

The desktop app creates a local SQLite database at `%LocalAppData%\CleanScope\cleanscope.db` for the **audit log** and **ignore list** (local only, never uploaded).

---

## Risk Levels at a Glance

| Level | Meaning |
|---|---|
| **A** | Safe to clean (e.g. user temp files, thumbnail cache) |
| **B** | Clean via the official method (e.g. browser cache, via the app's own cleanup) |
| **C** | Confirm before acting (the default bucket; personal data or insufficient info) |
| **D** | Not recommended (matches the system-critical blacklist, forced ≥ D) |
| **E** | Cannot determine, do not delete (fail-safe worst-case fallback) |

The rule and risk engines are **authoritative**: AI can never lower a risk level (the validator takes `max(AI, engine)`).

---

## Configure AI (optional, off by default)

CleanScope runs fully without AI (pure local rules + risk explanations). To enable cloud AI explanations:

1. Copy the template and fill in real values:

   ```bash
   cp appsettings.ai.example.json appsettings.ai.local.json
   ```

   ```jsonc
   {
     "baseUrl": "https://your-relay/v1",
     "apiKey": "sk-...",
     "model": "deepseek-chat",
     "cloudEnabled": true        // must be true to actually go to the cloud
   }
   ```

2. Or override via environment variables (higher priority than the file):

   ```
   CLEANSCOPE_AI_BASEURL   CLEANSCOPE_AI_KEY   CLEANSCOPE_AI_MODEL   CLEANSCOPE_AI_CLOUD=1
   ```

> 🔒 **`appsettings.ai.local.json` is excluded by `.gitignore`; the key is never committed.** Only the key-free `appsettings.ai.example.json` template is checked in.

**On-demand only (zero default token cost)**: even with AI configured, scanning and browsing make **no** cloud calls. AI fires only when you explicitly click — "✨ Explain with AI" on a file detail, "🧭 Generate AI advice" on the overview, or right-click "Identify with AI" in the Explorer. Deterministic rules / risk / name-heuristics cover the rest for free, so token-averse users pay nothing (and those who don't configure AI run fully local).

**Privacy boundary**: AI only goes to the cloud after desensitization (username → `%USER%`, filename → `%FILE%`), and **file contents are never uploaded**; with the cloud off, everything stays local with no remote calls. The desensitization gateway is the only outbound channel.

---

## Rule Packs

Classification knowledge lives as **declarative data** in [`rules/`](rules/) (12 packs, 60 rules), not hardcoded. Extend recognition by editing JSON, no code changes needed. System-critical directories are forced into a non-deletable blacklist in `00-system-critical.json`.

---

## Tests & CI

```bash
dotnet test CleanScope.sln
```

- 309 tests, including **safety red-line tests** (only A/B cleanable items are deletable and only to the Recycle Bin; C-E/container/blacklist/in-use/symlink must be rejected; no permanent-delete API — the Recycle-Bin delete is isolated in one file with a positive assertion; AI never lowers risk or triggers deletion; desensitized before cloud; audit written before execution …).
- Architecture dependency tests (NetArchTest) guard the layering: Core/Domain don't depend on WPF, AI doesn't reference Safety, SQLite stays in Infrastructure.
- These are **hard CI gates** ([.github/workflows/ci.yml](.github/workflows/ci.yml), windows-latest): any failure blocks merge/release.

---

## Project Structure

```
src/
  CleanScope.Domain          Domain entities/enums/contracts (zero deps, innermost)
  CleanScope.Core            Decision chain: scan/evidence/rules/attribution/risk/decision
  CleanScope.Safety          Safety gate (only disk-mutating path) + executor (Recycle Bin only, no permanent-delete code)
  CleanScope.Ai              AI sidecar: desensitize → explain → validate (advisory only)
  CleanScope.Infrastructure  Win32 access, SQLite storage, rule loading (net8.0-windows)
  CleanScope.Reporting       Markdown report export
  CleanScope.Application     Use-case orchestration (wires the decision chain via abstractions)
  CleanScope.App.Console     CLI host + composition root
  CleanScope.App.Wpf         WPF desktop (MVVM) + composition root
tests/                       xUnit tests (incl. safety red-lines and architecture guards)
rules/                       Declarative rule packs (*.json)
```

Architecture: Clean Architecture + "AI sidecar + single safety gate". AI is advisory only; the only disk-mutating path is the Safety gate.

---

## Safety Summary

- **No permanent-delete API exists in the codebase**: the only deletion is "move to Recycle Bin (recoverable)", isolated in a single file, with a static test positively asserting it uses only the Recycle-Bin API.
- The only disk-mutating path is the safety gate: it admits only "cleanable" (A/B) items that are non-system-critical, non-container and not in use; C-E/container/blacklist/in-use are all rejected.
- Deletion needs an explicit user click + two-step confirmation, with an audit written before execution (log first, then act).
- AI cannot bypass the rule engine, cannot lower risk, cannot trigger deletion; when uncertain it outputs "cannot determine, do not delete".
- File contents are never uploaded; cloud is reached only after desensitization; with cloud off everything stays local.
- Every action writes its audit before executing; if the audit write fails, the action aborts.

---

# 中文

[English](#cleanscope) | **中文**

> AI 辅助的 Windows C 盘清理分析工具 —— **先解释清楚，删除只进回收站**。

CleanScope 扫描你的磁盘，按来源、归属、依赖和风险把文件/目录讲清楚，给出分级建议，
**把最终删除决定权完全交还给你**。它优先解释“这是什么、属于谁、删了会怎样”；当你确认清理时，
**只把可清理项 (A/B) 移入回收站 (可还原)**，绝不永久删除、绝不碰系统关键文件。

## 四条产品宪法（设计红线）

1. **AI 不直接删除 C 盘重要文件** —— AI 只做解释/调查建议，永不触发删除。
2. **优先解释**文件的来源 / 依赖 / 风险，而非直接处理。
3. **用户做最终删除决定**（删除需显式点击 + 两步确认）。
4. 产品必须**安全、可解释、可测试、可扩展**。

删除模型（S-E）：唯一可改盘的安全闸门**仅对“可清理”桶 (A/B)、且非系统关键/非容器/未被占用的项**放行，
且删除**只移入回收站 (可恢复)**——代码库里**根本不存在永久删除 API**（唯一的回收站删除集中在单一文件，
静态测试正向断言“仅回收站、绝不永久删除”，随 CI 门禁）。核心仍是分析器：扫描 → 取证 → 规则 → 归因 → 风险 → 决策 → 报告。

## 横向对比

| | **CleanScope** | CCleaner | WizTree | BleachBit |
|---|:---:|:---:|:---:|:---:|
| 解释每个文件的来源与用途 | ✅ | ⚠️ 仅按类别 | ❌ 仅大小 | ⚠️ |
| 会永久删除吗？ | **❌ 仅进回收站** | ✅ | —（仅查看） | ✅ |
| 风险分级 A–E + 系统关键黑名单 | ✅ | ⚠️ | ❌ | ⚠️ |
| 整盘目录树（WizTree 式） | ✅ | ❌ | ✅ | ❌ |
| AI 解释（按需、仅建议、绝不删除） | ✅ | ❌ | ❌ | ❌ |
| 默认全程本地（无遥测） | ✅ | ❌ | ✅ | ✅ |
| 开源 | ✅ MIT | ❌ | ❌ | ✅ |

> WizTree 是快速的体积*查看器*，不做清理；CCleaner/BleachBit 会永久删除。CleanScope 的定位是**安全 + 可解释**：告诉你每样东西是什么，且物理上无法永久删除。

---

## 环境要求

- **Windows 10/11**（部分能力依赖 Win32：注册表、Restart Manager 占用检测、Authenticode 签名读取）
- **.NET 8 SDK**（`dotnet --version` ≥ 8.0）
- 运行 WPF 桌面端需 Windows 桌面运行时（随 .NET 8 SDK 安装即可）

## 构建

```bash
git clone https://github.com/David-Chen31/CleanScope.git
cd CleanScope
dotnet build CleanScope.sln -c Release
```

---

## 使用方式一：命令行（CLI）

最快验证核心价值的方式：扫描一个路径，输出风险分级与 Markdown 报告。**全程只读，不删除任何文件。**

```bash
# 扫描用户缓存目录，打印分级统计与 Top10
dotnet run --project src/CleanScope.App.Console -- scan "%LocalAppData%"

# 扫描并导出 Markdown 报告
dotnet run --project src/CleanScope.App.Console -- scan "C:\SomeFolder" --report report.md --top 200

# 启用 AI 解释（脱敏后出云，需先配置密钥，见下文）
dotnet run --project src/CleanScope.App.Console -- scan "%LocalAppData%" --ai --report report.md
```

参数：

| 参数 | 说明 |
|---|---|
| `scan <path>` | 要扫描的根路径（必填） |
| `--report <file>` | 导出 Markdown 报告到指定文件 |
| `--top <N>` | 保留占用最大的前 N 项（默认 100） |
| `--admin` | 管理员模式，扩大扫描覆盖（建议以管理员身份运行终端） |
| `--sanitize` | 报告中对路径里的用户名脱敏（便于分享/外发） |
| `--ai` | 启用 AI 解释（需可用密钥；未配置则自动跳过，纯本地规则/风险） |
| `--rules <dir>` | 指定规则包目录（默认用输出目录旁或仓库根的 `rules/`） |

退出码：`0` 成功 / `2` 用法错误 / `3` 规则加载失败 / `4` 路径错误 / `1` 其它错误。

---

## 使用方式二：桌面应用（WPF）

```bash
dotnet run --project src/CleanScope.App.Wpf
```

主要页面：

1. **概览 / 扫描** —— C 盘容量、选择路径开始扫描；完成后展示根聚合占用、可清理估算（A+B）、高风险数、占用 Top10。
2. **资源管理器（整盘目录树）** —— 像目录树一样浏览整个磁盘：可展开/折叠、显示大小与占比、按大小排序、逐节点标来源/用途/风险。右键可复制路径、复制用途、打开位置、移入回收站。
3. **空间地图** —— treemap 矩形树图，面积=占用、颜色=风险，可下钻定位“空间去哪了”。
4. **文件清单** —— 路径 / 大小 / 归属 / 风险 / 建议，按四桶（✅可清理 / ⚠谨慎 / 🛑勿动 / 🗂容器）分类。
5. **按软件** —— 按归属软件聚合“谁占了我的空间 + 各能清多少”，可展开看名下文件。
6. **文件详情** —— 属性、风险评估、**证据链（事实证据 vs AI 推测，视觉区分）**、归因候选、**按需** AI 解释（点击才请求）；
   **可清理项 (A/B) 提供「🗑 移入回收站（可还原）」**——两步确认 + 闸门复核 + 先写审计，仅进回收站、可还原；
   高风险（D/E）/ 容器 / 系统关键无删除入口，仅提示原因。
7. **报告 / 忽略名单** —— 导出 Markdown 报告；管理全局忽略名单（增删，仅本地存储）。

桌面端会在 `%LocalAppData%\CleanScope\cleanscope.db` 建一个本地 SQLite 库，存放**审计日志**与**忽略名单**
（仅本地，绝不上云）。

---

## 风险分级速查

| 级别 | 含义 |
|---|---|
| **A** | 可安全清理（如用户临时文件、缩略图缓存） |
| **B** | 建议用官方方式清理（如浏览器缓存，走应用自带清理） |
| **C** | 需确认后处理（默认落点；个人数据、信息不足时） |
| **D** | 不建议删除（命中系统关键黑名单，强制 ≥D） |
| **E** | 无法判断，不建议删除（fail-safe 最坏情况兜底） |

规则引擎与风险引擎是**权威**：AI 永远不能调低风险等级（校验器以 `max(AI, 引擎)` 取更高者）。

---

## 配置 AI（可选，默认关闭）

CleanScope 不配置 AI 也能完整运行（纯本地规则 + 风险解释）。若要启用云端 AI 解释：

1. 复制模板并填入真实值：

   ```bash
   cp appsettings.ai.example.json appsettings.ai.local.json
   ```

   ```jsonc
   {
     "baseUrl": "https://your-relay/v1",
     "apiKey": "sk-...",
     "model": "deepseek-chat",
     "cloudEnabled": true        // 必须为 true 才真正出云
   }
   ```

2. 或用环境变量覆盖（优先级高于文件）：

   ```
   CLEANSCOPE_AI_BASEURL   CLEANSCOPE_AI_KEY   CLEANSCOPE_AI_MODEL   CLEANSCOPE_AI_CLOUD=1
   ```

> 🔒 **`appsettings.ai.local.json` 已被 `.gitignore` 排除，密钥绝不入库。** 只有不含密钥的
> `appsettings.ai.example.json` 模板被提交。

**按需触发（默认零 token 开销）**：即便配置了 AI，扫描与浏览也**不**产生任何云端调用。AI 仅在你明确点击时触发——
文件详情的「✨ 用 AI 解释」、概览的「🧭 生成 AI 清理建议」、资源管理器右键「用 AI 识别」。其余由确定性的
规则 / 风险 / 目录名启发免费覆盖，不想花 token 的用户零开销（不配置 AI 则全程本地）。

**隐私边界**：AI 只在脱敏后出云（用户名 → `%USER%`、文件名 → `%FILE%`），**永不上传文件内容**；
关闭云端时全程本地、不发起任何远程调用。脱敏网关是唯一出云通道。

---

## 规则包

分类知识以**声明式数据**形式存放在 [`rules/`](rules/)（12 个包、60 条规则），而非硬编码。
可直接增改 JSON 扩展识别能力，无需改代码。系统关键目录在 `00-system-critical.json` 中强制为不可删的黑名单。

---

## 测试与 CI

```bash
dotnet test CleanScope.sln
```

- 309 个测试，含 **安全红线测试**（仅 A/B 可清理项可删且只进回收站、C-E/容器/黑名单/占用/symlink 必拒 /
  无永久删除 API（回收站删除集中单一文件并正向断言）/ AI 不放低风险、不触发删除 / 脱敏出云 / 审计先写后执行 …）。
- 架构依赖测试（NetArchTest）守护分层：Core/Domain 不依赖 WPF、AI 不引用 Safety、SQLite 仅在 Infrastructure。
- 这些测试是 **CI 硬门禁**（[.github/workflows/ci.yml](.github/workflows/ci.yml)，windows-latest）：任一失败即阻断合并/发布。

---

## 项目结构

```
src/
  CleanScope.Domain          领域实体/枚举/接口契约（零依赖，最内层）
  CleanScope.Core            裁决链：扫描/证据/规则/归因/风险/决策
  CleanScope.Safety          安全闸门（唯一可改盘路径）+ 执行器（删除仅移入回收站，无永久删除代码）
  CleanScope.Ai              AI 旁路：脱敏 → 解释 → 校验（仅建议）
  CleanScope.Infrastructure  Win32 访问、SQLite 存储、规则加载（net8.0-windows）
  CleanScope.Reporting       Markdown 报告导出
  CleanScope.Application     用例编排（经抽象串起裁决链）
  CleanScope.App.Console     CLI 宿主 + 组合根
  CleanScope.App.Wpf         WPF 桌面端（MVVM）+ 组合根
tests/                       xUnit 测试（含安全红线与架构守护）
rules/                       声明式规则包（*.json）
```

架构：Clean Architecture + “AI 旁路 + 单一安全闸门”。AI 只给建议，唯一能改盘的是 Safety 闸门。

---

## 安全承诺小结

- **代码库中不存在永久删除 API**：唯一的删除是“移入回收站（可还原）”，集中在单一文件，静态测试正向断言其只用回收站接口。
- 唯一可改盘路径是安全闸门：仅放行“可清理”桶 (A/B) 且非系统关键/非容器/未被占用的项；C-E/容器/黑名单/占用一律拒。
- 删除需用户显式点击 + 两步确认，执行前先写审计（先日志后执行）。
- AI 不能绕过规则引擎、不能调低风险、不触发删除；无法判断时输出“无法判断，不建议删除”。
- 不上传文件内容；脱敏后才出云；关闭云端则全程本地。
- 所有操作先写审计后执行；审计写失败即中止。
