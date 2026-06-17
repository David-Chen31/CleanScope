# CleanScope

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

---

## 环境要求

- **Windows 10/11**（部分能力依赖 Win32：注册表、Restart Manager 占用检测、Authenticode 签名读取）
- **.NET 8 SDK**（`dotnet --version` ≥ 8.0）
- 运行 WPF 桌面端需 Windows 桌面运行时（随 .NET 8 SDK 安装即可）

## 构建

```bash
git clone <repo-url> CleanScope
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

六个页面（侧栏 5 个导航 + 详情）：

1. **概览 / 扫描** —— C 盘容量、选择路径开始扫描；完成后展示根聚合占用、可清理估算（A+B）、高风险数、占用 Top10。
2. **空间地图** —— treemap 矩形树图，面积=占用、颜色=风险，可下钻定位“空间去哪了”。
3. **文件清单** —— 路径 / 大小 / 归属 / 风险 / 建议，按四桶（✅可清理 / ⚠谨慎 / 🛑勿动 / 🗂容器）分类；
   清单本身不放删除按钮，删除入口集中在详情页。
4. **按软件** —— 按归属软件聚合“谁占了我的空间 + 各能清多少”，可展开看名下文件。
5. **文件详情** —— 属性、风险评估、**证据链（事实证据 vs AI 推测，视觉区分）**、归因候选、AI 解释/调查；
   **可清理项 (A/B) 提供「🗑 移入回收站（可还原）」**——两步确认 + 闸门复核 + 先写审计，仅进回收站、可还原；
   高风险（D/E）/ 容器 / 系统关键无删除入口，仅提示原因。命令型缓存给「运行/复制官方命令」，安装目录给「打开卸载程序」。
6. **报告 / 忽略名单** —— 导出 Markdown 报告；管理全局忽略名单（增删，仅本地存储）。

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

**隐私边界**：AI 只在脱敏后出云（用户名 → `%USER%`、文件名 → `%FILE%`），**永不上传文件内容**；
关闭云端时全程本地、不发起任何远程调用。脱敏网关是唯一出云通道。

---

## 规则包

分类知识以**声明式数据**形式存放在 [`rules/`](rules/)（11 个包、52 条规则），而非硬编码。
可直接增改 JSON 扩展识别能力，无需改代码。系统关键目录在 `00-system-critical.json` 中强制为不可删的黑名单。

---

## 测试与 CI

```bash
dotnet test CleanScope.sln
```

- 233 个测试，含 **安全红线测试**（仅 A/B 可清理项可删且只进回收站、C-E/容器/黑名单/占用/symlink 必拒 /
  无永久删除 API（回收站删除集中单一文件并正向断言）/ AI 不放低风险、不触发删除 / 脱敏出云 / 审计先写后执行 …）。
- 架构依赖测试（NetArchTest）守护分层：Core/Domain 不依赖 WPF、AI 不引用 Safety、SQLite 仅在 Infrastructure。
- 这些测试是 **CI 硬门禁**（[.github/workflows/ci.yml](.github/workflows/ci.yml)，windows-latest）：任一失败即阻断合并/发布。
- 详见 [安全测试门禁.md](安全测试门禁.md)。

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

架构：Clean Architecture + “AI 旁路 + 单一安全闸门”。AI 只给建议，唯一能改盘的是 Safety 闸门
（MVP 永不放行）。

## 设计文档

需求/架构/安全/数据模型等冻结文档见仓库根目录：
[需求冻结文档.md](需求冻结文档.md)、[架构设计.md](架构设计.md)、[安全设计.md](安全设计.md)、
[风险分级细则.md](风险分级细则.md)、[数据模型设计.md](数据模型设计.md)、[模块划分.md](模块划分.md)。

---

## 安全承诺小结

- **代码库中不存在永久删除 API**：唯一的删除是“移入回收站（可还原）”，集中在单一文件，静态测试正向断言其只用回收站接口。
- 唯一可改盘路径是安全闸门：仅放行“可清理”桶 (A/B) 且非系统关键/非容器/未被占用的项；C-E/容器/黑名单/占用一律拒。
- 删除需用户显式点击 + 两步确认，执行前先写审计（先日志后执行）。
- AI 不能绕过规则引擎、不能调低风险、不触发删除；无法判断时输出“无法判断，不建议删除”。
- 不上传文件内容；脱敏后才出云；关闭云端则全程本地。
- 所有操作先写审计后执行；审计写失败即中止。
