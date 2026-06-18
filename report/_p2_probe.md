# CleanScope 扫描报告

- 扫描目标: `C:\`
- 扫描时间: 2026-06-18 07:05:54 ~ 2026-06-18 07:07:51
- 应用版本: 0.1.0　|　扫描模式: Normal
- 文件/目录数: 1157390
- 总占用: 166.47 GB

> ⚠️ 本报告仅供参考。删除决策始终由你做出; 应用内删除仅移入回收站 (可还原)。

## 风险统计

| 等级 | 数量 | 占用(去重) |
|---|---|---|
| 🗂 容器(仅浏览) | 9 | 27.21 GB |
| A 通常可清理 | 0 | 0 B |
| B 走官方方式 | 16 | 9.03 GB |
| C 谨慎 | 150 | 76.79 GB |
| D 高风险 | 25 | 53.45 GB |
| E 无法判断 | 0 | 0 B |

可清理估算 (A+B, 去重, 仍建议确认): **9.03 GB**

> 占用为**去重独占大小**(每个字节只归属最深的被分析目录), 故各级之和不超过磁盘实际占用; TopN 仍按目录聚合大小展示。

## 按软件占用 (谁占了我的空间)

| 软件/来源 | 项数 | 占用(去重) | 其中可清理(A/B) |
|---|---|---|---|
| Windows 系统 | 24 | 52.61 GB | 0 B |
| Microsoft | 18 | 10.86 GB | 1.86 GB |
| 腾讯系列 (QQ/微信等) | 12 | 5.28 GB | 0 B |
| 联想 (Lenovo) | 10 | 5.19 GB | 0 B |
| Visual Studio Code | 5 | 4.7 GB | 1.08 GB |
| Microsoft Office 家庭和学生版 2021 - zh-cn | 8 | 4.57 GB | 0 B |
| 通义灵码 (Lingma) | 13 | 3.81 GB | 125.97 MB |
| Docker Desktop | 6 | 3.44 GB | 1.5 GB |
| NVIDIA | 5 | 3.37 GB | 0 B |
| Visual Studio 生成工具 2022 | 9 | 3.35 GB | 0 B |
| Legion Zone | 3 | 3.09 GB | 0 B |
| JetBrains 系列 | 3 | 2.47 GB | 0 B |
| Adobe | 2 | 2.24 GB | 0 B |
| Notion | 6 | 2.11 GB | 431.85 KB |
| Miniconda (Python) | 2 | 2.08 GB | 818.13 MB |
| TDAppDesktop | 4 | 1.88 GB | 0 B |
| Windows Kits | 4 | 1.84 GB | 0 B |
| Windows 应用商店应用 | 1 | 1.82 GB | 0 B |
| s3_web | 4 | 1.76 GB | 0 B |
| NuGet (.NET) | 2 | 1.49 GB | 1.49 GB |
| 飞书 (Lark) | 2 | 1.42 GB | 0 B |
| 用户级安装的程序 | 1 | 1.39 GB | 0 B |
| QQEX | 3 | 1.36 GB | 0 B |
| 联想浏览器(原厂认证，极速体验) | 1 | 1.32 GB | 0 B |
| iSlide | 6 | 1.22 GB | 0 B |
| Rust / rustup | 6 | 1.2 GB | 0 B |
| AnkiProgramFiles | 1 | 1.17 GB | 0 B |
| 联想电脑管家（原厂驱动和官方服务） | 1 | 1.15 GB | 0 B |
| VS Code Remote (WSL) | 2 | 1.12 GB | 0 B |
| 迅雷 (Thunder) | 3 | 1.09 GB | 0 B |

> 占用为去重独占大小; “可清理”为该软件名下 A/B 项 (仍建议确认/官方方式)。

## 可清理类别 (A/B, 按类别聚合)

| 类别 | 项数 | 可回收(去重) | 建议清理方式 |
|---|---|---|---|
| 可重建缓存(按目录名推断) | 6 | 2.35 GB | 建议用官方方式清理 (命令/设置) |
| 镜像/安装包 | 2 | 1.5 GB | 确认不再需要后清理 |
| NuGet 全局包 | 1 | 1.49 GB | 用 dotnet nuget locals all --clear |
| C/C++ IntelliSense 缓存 | 2 | 1.37 GB | 可清理；重新打开项目会自动重建 |
| Cargo 缓存 | 3 | 923.06 MB | 可清理 .cargo\registry\cache 与 src，cargo 会按需重新下载 |
| conda 包缓存 | 1 | 818.13 MB | 用 conda clean --all |
| Gradle 缓存 | 1 | 632.84 MB | 可清理；或迁移 GRADLE_USER_HOME |

> 以上仅给出每类可回收空间与官方清理方式; 删除由你决定, 应用内删除仅移入回收站 (可还原)。

## 占用大头 (前 20, 按真实占用/叶子贡献排序)

| # | 路径 | 真实占用(去重) | 聚合大小 | 风险 | 建议 |
|---|---|---|---|---|---|
| 1 | `C:\Windows\WinSxS` | 13.22 GB | 14.44 GB | D 高风险 | 仅用 DISM /StartComponentCleanup，禁止手动删除 |
| 2 | `C:\pagefile.sys` | 11.39 GB | 11.39 GB | D 高风险 | 经 系统>高级>虚拟内存 调整，勿直删 |
| 3 | `C:\Users\28170\AppData\Local` | 10.74 GB | 22.41 GB | 🗂 容器 | 展开按子目录查看，勿整体处理 |
| 4 | `C:\Users\28170\AppData\Roaming` | 5.32 GB | 27.19 GB | 🗂 容器 | 展开按子目录查看，勿整体处理 |
| 5 | `C:\Windows\System32` | 5.26 GB | 10.48 GB | D 高风险 | 严禁删除 |
| 6 | `C:\Windows\Installer` | 4.9 GB | 4.9 GB | D 高风险 | 不要手动删除；通过软件官方卸载/修复处理 |
| 7 | `C:\Program Files` | 3.5 GB | 18.11 GB | 🗂 容器 | 展开按子目录查看，勿整体处理 |
| 8 | `C:\ProgramData\Microsoft\MapData\mapscache\base` | 3.3 GB | 3.3 GB | C 谨慎 | 谨慎处理: 建议先备份或确认用途 |
| 9 | `C:\Users\28170` | 2.34 GB | 64 GB | 🗂 容器 | 展开按子目录查看，勿整体处理 |
| 10 | `C:\Windows\System32\DriverStore\FileRepository\nvlt.inf_amd64_014a6c420cc5bf89` | 1.94 GB | 1.94 GB | D 高风险 | 严禁删除；用设备管理器/pnputil 管理 |
| 11 | `C:\Program Files (x86)` | 1.88 GB | 18.17 GB | 🗂 容器 | 展开按子目录查看，勿整体处理 |
| 12 | `C:\ProgramData` | 1.83 GB | 12.51 GB | 🗂 容器 | 展开按子目录查看，勿整体处理 |
| 13 | `C:\Users\28170\AppData\Local\Packages` | 1.82 GB | 1.82 GB | C 谨慎 | 谨慎处理: 建议先备份或确认用途 |
| 14 | `C:\Users\28170\AppData\Roaming\Notion\Partitions\notion\Service Worker\CacheStorage\614a0024a405b02cc875d3e091267a8eb895f9ee` | 1.81 GB | 1.81 GB | C 谨慎 | 谨慎处理: 建议先备份或确认用途 |
| 15 | `C:\Program Files\Microsoft Office\root\Office16` | 1.65 GB | 2.34 GB | C 谨慎 | 谨慎处理: 建议先备份或确认用途 |
| 16 | `C:\Users\28170\AppData\Roaming\Code\WebStorage` | 1.57 GB | 1.57 GB | C 谨慎 | 谨慎处理: 建议先备份或确认用途 |
| 17 | `C:\Users\28170\.nuget\packages` | 1.49 GB | 1.49 GB | B 走官方方式 | 用 dotnet nuget locals all --clear |
| 18 | `C:\Windows\assembly\NativeImages_v4.0.30319_32` | 1.48 GB | 1.48 GB | D 高风险 | 严禁删除/修改 |
| 19 | `C:\Windows` | 1.47 GB | 41.21 GB | D 高风险 | 严禁删除/修改 |
| 20 | `C:\Program Files\Common Files\Adobe` | 1.44 GB | 1.44 GB | C 谨慎 | 谨慎处理: 建议先备份或确认用途 |

> “真实占用”= 去重独占大小 (不含已单列的子目录); “聚合大小”= 含全部子孙。

## ⚠️ 高风险提醒 (不建议删除)

- **[D]** `C:\Windows` — 严禁删除/修改
- **[D]** `C:\Windows\WinSxS` — 仅用 DISM /StartComponentCleanup，禁止手动删除
- **[D]** `C:\pagefile.sys` — 经 系统>高级>虚拟内存 调整，勿直删
- **[D]** `C:\Windows\System32` — 严禁删除
- **[D]** `C:\Windows\Installer` — 不要手动删除；通过软件官方卸载/修复处理
- **[D]** `C:\Windows\System32\DriverStore` — 严禁删除；用设备管理器/pnputil 管理
- **[D]** `C:\Windows\System32\DriverStore\FileRepository` — 严禁删除；用设备管理器/pnputil 管理
- **[D]** `C:\Windows\assembly` — 严禁删除/修改
- **[D]** `C:\Windows\System32\DriverStore\FileRepository\nvlt.inf_amd64_014a6c420cc5bf89` — 严禁删除；用设备管理器/pnputil 管理
- **[D]** `C:\Windows\assembly\NativeImages_v4.0.30319_32` — 严禁删除/修改
- **[D]** `C:\Windows\SysWOW64` — 严禁删除
- **[D]** `C:\Windows\assembly\NativeImages_v4.0.30319_64` — 严禁删除/修改
- **[D]** `C:\Windows\System32\DriverStore\FileRepository\u0397945.inf_amd64_16a50ea7e60ebe3c` — 严禁删除；用设备管理器/pnputil 管理
- **[D]** `C:\Windows\System32\DriverStore\FileRepository\u0397945.inf_amd64_16a50ea7e60ebe3c\B397614` — 严禁删除；用设备管理器/pnputil 管理
- **[D]** `C:\Windows\Fonts` — 勿删；经字体设置管理
- **[D]** `C:\Windows\Temp` — 严禁删除/修改
- **[D]** `C:\Windows\SystemApps` — 严禁删除/修改
- **[D]** `C:\ProgramData\Package Cache` — 用对应官方安装器(如 VS Installer)管理，勿直删
- **[D]** `C:\Windows\servicing` — 严禁删除/修改
- **[D]** `C:\Windows\Microsoft.NET` — 严禁删除/修改
- **[D]** `C:\Windows\servicing\Sessions` — 严禁删除/修改
- **[D]** `C:\Windows\Temp\Whesvc` — 严禁删除/修改
- **[D]** `C:\Windows\System32\Microsoft-Edge-WebView` — 严禁删除
- **[D]** `C:\Windows\WinSxS\amd64_microsoft-edge-webview_31bf3856ad364e35_10.0.26100.8655_none_2e9ef7333ad05fb3` — 仅用 DISM /StartComponentCleanup，禁止手动删除
- **[D]** `C:\Windows\WinSxS\amd64_microsoft-edge-webview_31bf3856ad364e35_10.0.26100.8457_none_2eb4697b3ac05b13` — 仅用 DISM /StartComponentCleanup，禁止手动删除

## 分级明细

| 路径 | 大小 | 归属 | 风险 | 建议 | 说明 |
|---|---|---|---|---|---|
| `C:\ProgramData\Microsoft\MapData\mapscache` | 3.79 GB | Microsoft | B 走官方方式 | 建议用官方方式清理 (命令/设置) | 目录名表明为可重建缓存/临时, 删后会自动重建, 建议用官方方式清理 |
| `C:\Users\28170\.lingma\vscode\sharedClientCache` | 2.9 GB | 通义灵码 (Lingma) | B 走官方方式 | 建议用官方方式清理 (命令/设置) | 目录名表明为可重建缓存/临时, 删后会自动重建, 建议用官方方式清理 |
| `C:\Users\28170\AppData\Roaming\Notion\Partitions\notion\Service Worker` | 1.81 GB | Notion | B 走官方方式 | 建议用官方方式清理 (命令/设置) | 目录名表明为可重建缓存/临时, 删后会自动重建, 建议用官方方式清理 |
| `C:\Users\28170\AppData\Roaming\Notion\Partitions\notion\Service Worker\CacheStorage` | 1.81 GB | Notion | B 走官方方式 | 建议用官方方式清理 (命令/设置) | 目录名表明为可重建缓存/临时, 删后会自动重建, 建议用官方方式清理 |
| `C:\Users\28170\.nuget\packages` | 1.49 GB | NuGet (.NET) | B 走官方方式 | 用 dotnet nuget locals all --clear | 有官方清理方式; 规则 nuget-packages |
| `C:\Users\28170\AppData\Local\Microsoft\vscode-cpptools` | 1.37 GB | Microsoft | B 走官方方式 | 可清理；重新打开项目会自动重建 | 有官方清理方式; 规则 vscode-cpptools-cache |
| `C:\Users\28170\AppData\Roaming\Code\CachedExtensionVSIXs` | 1.08 GB | Visual Studio Code | B 走官方方式 | 建议用官方方式清理 (命令/设置) | 目录名表明为可重建缓存/临时, 删后会自动重建, 建议用官方方式清理 |
| `C:\Users\28170\.cargo\registry` | 923.06 MB | Rust / Cargo | B 走官方方式 | 可清理 .cargo\registry\cache 与 src，cargo 会按需重新下载 | 有官方清理方式; 规则 cargo-registry |
| `C:\Users\28170\Miniconda3\pkgs` | 818.13 MB | Miniconda (Python) | B 走官方方式 | 用 conda clean --all | 有官方清理方式; 规则 conda-pkgs-anaconda |
| `C:\Program Files\Docker\Docker\resources\wsl\docker-wsl-cli.iso` | 816.89 MB | Docker Desktop | B 走官方方式 | 确认不再需要后清理 | 有官方清理方式; 规则 installer-archive |
| `C:\Users\28170\.cargo\registry\src\index.crates.io-1949cf8c6b5b557f` | 743.96 MB | Rust / Cargo | B 走官方方式 | 可清理 .cargo\registry\cache 与 src，cargo 会按需重新下载 | 有官方清理方式; 规则 cargo-registry |
| `C:\Users\28170\.cargo\registry\src` | 743.96 MB | Rust / Cargo | B 走官方方式 | 可清理 .cargo\registry\cache 与 src，cargo 会按需重新下载 | 有官方清理方式; 规则 cargo-registry |
| `C:\Program Files\Docker\Docker\resources\docker-desktop.iso` | 715.76 MB | Docker Desktop | B 走官方方式 | 确认不再需要后清理 | 有官方清理方式; 规则 installer-archive |
| `C:\ProgramData\MySQL\MySQL Installer for Windows\Product Cache` | 676.67 MB | MySQL | B 走官方方式 | 建议用官方方式清理 (命令/设置) | 目录名表明为可重建缓存/临时, 删后会自动重建, 建议用官方方式清理 |
| `C:\Users\28170\AppData\Local\Microsoft\vscode-cpptools\ipch` | 661.44 MB | Microsoft | B 走官方方式 | 可清理；重新打开项目会自动重建 | 有官方清理方式; 规则 vscode-cpptools-cache |
| `C:\Users\28170\.gradle\caches` | 632.84 MB | Gradle | B 走官方方式 | 可清理；或迁移 GRADLE_USER_HOME | 有官方清理方式; 规则 gradle-caches |
| `C:\` | 166.47 GB | 磁盘根目录 | C 谨慎 | 展开按子目录查看，勿整体处理 | 整个磁盘分区的根目录, 内含系统与所有数据; 请展开按子目录判断, 不要整体处理 |
| `C:\Users` | 64.15 GB | 所有用户 | C 谨慎 | 展开按子目录查看，勿整体处理 | 本机所有用户的主目录; 请展开按子目录判断, 不要整体处理 |
| `C:\Users\28170` | 64 GB | 用户主目录 | C 谨慎 | 展开按子目录查看，勿整体处理 | 你的用户主目录: 文档 / 桌面 / 下载, 及各软件的个人数据; 请展开按子目录判断, 不要整体处理 |
| `C:\Users\28170\AppData` | 50.13 GB | 应用数据根 | C 谨慎 | 展开按子目录查看，勿整体处理 | 各软件的配置、数据与缓存的总目录; 请展开按子目录判断, 不要整体处理 |
| `C:\Users\28170\AppData\Roaming` | 27.19 GB | 应用配置·漫游 | C 谨慎 | 展开按子目录查看，勿整体处理 | 用户应用程序的配置与个性化数据 (随账户在域内漫游); 请展开按子目录判断, 不要整体处理 |
| `C:\Users\28170\AppData\Local` | 22.41 GB | 应用数据·本机 | C 谨慎 | 展开按子目录查看，勿整体处理 | 本机应用程序的数据与缓存 (不随账户漫游); 请展开按子目录判断, 不要整体处理 |
| `C:\Program Files (x86)` | 18.17 GB | 程序·32位 | C 谨慎 | 展开按子目录查看，勿整体处理 | 32 位程序的安装目录; 请展开按子目录判断, 不要整体处理 |
| `C:\Program Files` | 18.11 GB | 程序·64位 | C 谨慎 | 展开按子目录查看，勿整体处理 | 64 位程序的安装目录; 请展开按子目录判断, 不要整体处理 |
| `C:\ProgramData` | 12.51 GB | 共享应用数据 | C 谨慎 | 展开按子目录查看，勿整体处理 | 所有用户共享的应用程序数据; 请展开按子目录判断, 不要整体处理 |
| `C:\Program Files (x86)\Lenovo` | 5.95 GB | 联想 (Lenovo) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Roaming\Tencent` | 5.28 GB | 腾讯系列 (QQ/微信等) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\ProgramData\Lenovo` | 4.79 GB | 联想 (Lenovo) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Roaming\Code` | 4.7 GB | Visual Studio Code | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Program Files\Microsoft Office` | 4.57 GB | Microsoft Office 家庭和学生版 2021 - zh-cn | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files\Microsoft Office\root` | 4.56 GB | Microsoft Office 家庭和学生版 2021 - zh-cn | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\ProgramData\Microsoft` | 4.38 GB | Microsoft | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Local\Microsoft` | 4.14 GB | Microsoft | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Program Files\Docker` | 4.12 GB | Docker | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\.lingma` | 3.81 GB | 通义灵码 (Lingma) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 通义灵码 (Lingma), 暂无清理方式, 谨慎处理 |
| `C:\ProgramData\Microsoft\MapData` | 3.79 GB | Microsoft | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Microsoft Visual Studio` | 3.48 GB | Visual Studio | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files\Docker\Docker` | 3.44 GB | Docker Desktop | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Microsoft Visual Studio\2022` | 3.35 GB | Visual Studio | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools` | 3.35 GB | Visual Studio 生成工具 2022 | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\ProgramData\Microsoft\MapData\mapscache\base` | 3.3 GB | Microsoft | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Microsoft` | 3.17 GB | Microsoft | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Lenovo\LegionZone` | 3.09 GB | Legion Zone | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\.lingma\vscode` | 3.08 GB | 通义灵码 (Lingma) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 通义灵码 (Lingma), 暂无清理方式, 谨慎处理 |
| `C:\Program Files\Docker\Docker\resources` | 3.03 GB | Docker Desktop | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\.lingma\vscode\sharedClientCache\index` | 2.77 GB | 通义灵码 (Lingma) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 通义灵码 (Lingma), 暂无清理方式, 谨慎处理 |
| `C:\Program Files\NVIDIA Corporation` | 2.6 GB | NVIDIA | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Local\JetBrains` | 2.47 GB | JetBrains 系列 | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Program Files\Microsoft Office\root\Office16` | 2.34 GB | Microsoft Office 家庭和学生版 2021 - zh-cn | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\ProgramData\Lenovo\devicecenter` | 2.2 GB | 联想 (Lenovo) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Roaming\Notion` | 2.11 GB | Notion | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC` | 2.1 GB | Visual Studio 生成工具 2022 | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Roaming\Notion\Partitions\notion` | 2.09 GB | Notion | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\Notion\Partitions` | 2.09 GB | Notion | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\Miniconda3` | 2.08 GB | Miniconda (Python) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 Miniconda (Python), 暂无清理方式, 谨慎处理 |
| `C:\Program Files\Microsoft Office\root\vfs` | 2.06 GB | Microsoft Office 家庭和学生版 2021 - zh-cn | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools` | 1.99 GB | Visual Studio 生成工具 2022 | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Roaming\TDAppDesktop` | 1.88 GB | TDAppDesktop | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\14.44.35207` | 1.84 GB | Visual Studio 生成工具 2022 | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC` | 1.84 GB | Visual Studio 生成工具 2022 | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Windows Kits` | 1.84 GB | Windows Kits | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Windows Kits\10` | 1.83 GB | Windows Kits | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Roaming\Tencent\xwechat` | 1.82 GB | 腾讯系列 (QQ/微信等) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Local\Packages` | 1.82 GB | Windows 应用商店应用 | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 用途: UWP/商店应用的本地数据与缓存; 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\Notion\Partitions\notion\Service Worker\CacheStorage\614a0024a405b02cc875d3e091267a8eb895f9ee` | 1.81 GB | Notion | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\s3_web\cos` | 1.76 GB | s3_web | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\s3_web` | 1.76 GB | s3_web | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Program Files\Common Files` | 1.64 GB | 共享组件 (多程序) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 用途: 多个程序共用的库/运行时; 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Roaming\s3_web\cos\res` | 1.64 GB | s3_web | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\Code\User` | 1.6 GB | Visual Studio Code | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\Code\WebStorage` | 1.57 GB | Visual Studio Code | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\14.44.35207\lib` | 1.5 GB | Visual Studio 生成工具 2022 | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\.nuget` | 1.49 GB | NuGet (.NET) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 NuGet (.NET), 暂无清理方式, 谨慎处理 |
| `C:\Program Files\Common Files\Adobe` | 1.44 GB | Adobe | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\ProgramData\Lenovo\devicecenter\LenovoDrivers\Drivers` | 1.43 GB | 联想 (Lenovo) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\ProgramData\Lenovo\devicecenter\LenovoDrivers` | 1.43 GB | 联想 (Lenovo) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Roaming\LarkShell` | 1.42 GB | 飞书 (Lark) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\Tencent\QQ` | 1.41 GB | 腾讯系列 (QQ/微信等) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Local\Programs` | 1.39 GB | 用户级安装的程序 | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 用途: 免管理员安装到用户目录的程序; 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\QQEX` | 1.36 GB | QQEX | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\Tencent\QQ\libcef` | 1.32 GB | 腾讯系列 (QQ/微信等) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Program Files (x86)\Lenovo\SLBrowser` | 1.32 GB | 联想浏览器(原厂认证，极速体验) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Roaming\Code\User\workspaceStorage` | 1.31 GB | Visual Studio Code | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Local\JetBrains\IntelliJIdea2024.2` | 1.31 GB | JetBrains 系列 | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Program Files (x86)\Microsoft\EdgeCore` | 1.3 GB | Microsoft | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\ProgramData\Lenovo\LeAppStore` | 1.29 GB | 联想 (Lenovo) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Lenovo\LegionZone\2.0.26.6085` | 1.26 GB | Legion Zone | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Roaming\iSlide\iSlide Tools` | 1.22 GB | iSlide | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\iSlide` | 1.22 GB | iSlide | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Program Files\Microsoft Office\root\vfs\ProgramFilesCommonX64` | 1.22 GB | Microsoft Office 家庭和学生版 2021 - zh-cn | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Roaming\iSlide\iSlide Tools\Browser\DotNetBrowser` | 1.22 GB | iSlide | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\iSlide\iSlide Tools\Browser` | 1.22 GB | iSlide | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\iSlide\iSlide Tools\Browser\DotNetBrowser\2.27.0` | 1.22 GB | iSlide | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Program Files\Microsoft Office\root\vfs\ProgramFilesCommonX64\Microsoft Shared` | 1.22 GB | Microsoft Office 家庭和学生版 2021 - zh-cn | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\.rustup` | 1.2 GB | Rust / rustup | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 Rust / rustup, 暂无清理方式, 谨慎处理 |
| `C:\Users\28170\.rustup\toolchains` | 1.2 GB | Rust / rustup | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 Rust / rustup, 暂无清理方式, 谨慎处理 |
| `C:\Users\28170\.rustup\toolchains\stable-x86_64-pc-windows-msvc` | 1.2 GB | Rust / rustup | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 Rust / rustup, 暂无清理方式, 谨慎处理 |
| `C:\Program Files (x86)\Lenovo\PCManager` | 1.17 GB | 联想 (Lenovo) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Local\AnkiProgramFiles` | 1.17 GB | AnkiProgramFiles | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Program Files (x86)\Lenovo\PCManager\5.1.190.5202` | 1.15 GB | 联想电脑管家（原厂驱动和官方服务） | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\vscode-remote-wsl` | 1.12 GB | VS Code Remote (WSL) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 VS Code Remote (WSL), 暂无清理方式, 谨慎处理 |
| `C:\Users\28170\vscode-remote-wsl\stable` | 1.12 GB | VS Code Remote (WSL) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 VS Code Remote (WSL), 暂无清理方式, 谨慎处理 |
| `C:\Users\28170\AppData\Roaming\iSlide\iSlide Tools\Browser\DotNetBrowser\2.27.0\chromium` | 1.12 GB | iSlide | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Program Files (x86)\Lenovo\LegionZone\2.0.23.1161` | 1.1 GB | Legion Zone | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Thunder Network` | 1.09 GB | 迅雷 (Thunder) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Thunder Network\Thunder` | 1.09 GB | 迅雷 (Thunder) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Thunder Network\Thunder\Program` | 1.07 GB | 迅雷 (Thunder) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\.cargo` | 1.07 GB | Rust / Cargo | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 Rust / Cargo, 暂无清理方式, 谨慎处理 |
| `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7` | 1.03 GB | Visual Studio 生成工具 2022 | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\.lingma\vscode\sharedClientCache\index\meta` | 1 GB | 通义灵码 (Lingma) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 通义灵码 (Lingma), 暂无清理方式, 谨慎处理 |
| `C:\Users\28170\.lingma\vscode\sharedClientCache\index\meta\v7\index.db` | 1 GB | 通义灵码 (Lingma) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 被运行中进程占用 |
| `C:\Users\28170\.lingma\vscode\sharedClientCache\index\meta\v7` | 1 GB | 通义灵码 (Lingma) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 通义灵码 (Lingma), 暂无清理方式, 谨慎处理 |
| `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE` | 1023.69 MB | Visual Studio 生成工具 2022 | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files\Microsoft Office\root\vfs\ProgramFilesCommonX64\Microsoft Shared\OFFICE16` | 1011.15 MB | Microsoft Office 家庭和学生版 2021 - zh-cn | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Local\Microsoft\Windows` | 995.09 MB | Microsoft | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 用途: Windows 系统文件; 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\Tencent\WeChat` | 983.54 MB | 腾讯系列 (QQ/微信等) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Program Files\NVIDIA Corporation\Nsight Systems 2024.4.2` | 981.22 MB | NVIDIA | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Roaming\Tencent\WeChat\XPlugin` | 976.57 MB | 腾讯系列 (QQ/微信等) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\Tencent\WeChat\XPlugin\Plugins` | 975.36 MB | 腾讯系列 (QQ/微信等) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\TDAppDesktop\WebApp` | 931.82 MB | TDAppDesktop | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\TDAppDesktop\WebApp\pwa_resources_realtime_3.9.0` | 931.82 MB | TDAppDesktop | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Program Files\Docker\Docker\resources\wsl` | 925.56 MB | Docker Desktop | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Roaming\TDAppDesktop\WebApp\pwa_resources_realtime_3.9.0\docs.gtimg.com` | 923.44 MB | TDAppDesktop | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\.lingma\vscode\sharedClientCache\index\chat` | 922.84 MB | 通义灵码 (Lingma) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 通义灵码 (Lingma), 暂无清理方式, 谨慎处理 |
| `C:\Users\28170\.lingma\vscode\sharedClientCache\index\chat\v4` | 922.84 MB | 通义灵码 (Lingma) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 通义灵码 (Lingma), 暂无清理方式, 谨慎处理 |
| `C:\Program Files\NVIDIA Corporation\Nsight Compute 2024.3.0` | 915.1 MB | NVIDIA | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files\dotnet` | 894.37 MB | dotnet | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Roaming\Tencent\xwechat\radium` | 878.3 MB | 腾讯系列 (QQ/微信等) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\QQEX\users\144115213539930700` | 866.5 MB | QQEX | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\QQEX\users` | 866.5 MB | QQEX | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Program Files (x86)\Microsoft\Edge\Application` | 853.13 MB | Microsoft Edge | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Microsoft\Edge` | 853.13 MB | Microsoft | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Microsoft\Edge\Application\149.0.4022.69` | 843.29 MB | Microsoft Edge | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Microsoft\EdgeWebView` | 838.79 MB | Microsoft | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Microsoft\EdgeWebView\Application` | 838.79 MB | Microsoft | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Microsoft\EdgeWebView\Application\149.0.4022.69` | 838 MB | Microsoft | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\.lingma\vscode\sharedClientCache\index\chat\v4\MaintEval_0f55e9617a320c51ee7f38aa5744f7f0` | 833.58 MB | 通义灵码 (Lingma) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 通义灵码 (Lingma), 暂无清理方式, 谨慎处理 |
| `C:\Users\28170\.lingma\vscode\sharedClientCache\index\chat\v4\MaintEval_0f55e9617a320c51ee7f38aa5744f7f0\store` | 833.58 MB | 通义灵码 (Lingma) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 通义灵码 (Lingma), 暂无清理方式, 谨慎处理 |
| `C:\Program Files\WSL` | 826.77 MB | 适用于 Linux 的 Windows 子系统 (WSL) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Roaming\Adobe` | 826.27 MB | Adobe | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\Tencent\WeChat\XPlugin\Plugins\RadiumWMPF` | 802.72 MB | 腾讯系列 (QQ/微信等) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Program Files (x86)\NVIDIA Corporation` | 784.74 MB | NVIDIA | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\.gradle` | 783.76 MB | Gradle | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 Gradle, 暂无清理方式, 谨慎处理 |
| `C:\Users\28170\AppData\Roaming\s3_web\cos\res\data` | 775.51 MB | s3_web | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Local\JetBrains\PyCharmCE2023.3` | 774.06 MB | JetBrains 系列 | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Program Files (x86)\Windows Kits\10\Lib\10.0.26100.0` | 767.32 MB | Windows Kits | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Windows Kits\10\Lib` | 767.32 MB | Windows Kits | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\ProgramData\Lenovo\LDF` | 739.77 MB | 联想 (Lenovo) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\ProgramData\Lenovo\LDF\Symbols` | 739.77 MB | 联想 (Lenovo) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Local\Microsoft\Windows\Fonts` | 739.62 MB | Microsoft | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 用途: 系统字体 (经字体设置管理); 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\ProgramData\Lenovo\LDF\Symbols\ntkrnlmp.pdb` | 717.45 MB | 联想 (Lenovo) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\NVIDIA Corporation\Nsight Visual Studio Edition 2024.3` | 711.74 MB | NVIDIA | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files\Microsoft Office\root\Office16\sdxs` | 709.26 MB | Microsoft Office 家庭和学生版 2021 - zh-cn | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Local\Microsoft\Office` | 702.77 MB | Microsoft | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\Seewo` | 698.78 MB | Seewo | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\Seewo\EasiNote5` | 698.76 MB | Seewo | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Local\PowerToys` | 694.58 MB | PowerToys | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Program Files\Docker\cli-plugins` | 690.11 MB | Docker | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files\Docker\Docker\resources\cli-plugins` | 690.11 MB | Docker Desktop | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\.lingma\vscode\sharedClientCache\index\completion` | 687.29 MB | 通义灵码 (Lingma) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 通义灵码 (Lingma), 暂无清理方式, 谨慎处理 |
| `C:\Users\28170\.lingma\vscode\sharedClientCache\index\completion\v4` | 687.29 MB | 通义灵码 (Lingma) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 通义灵码 (Lingma), 暂无清理方式, 谨慎处理 |
| `C:\Users\28170\AppData\Roaming\Tencent\xwechat\xplugin` | 680.48 MB | 腾讯系列 (QQ/微信等) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\Tencent\xwechat\xplugin\Plugins` | 679.99 MB | 腾讯系列 (QQ/微信等) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\ProgramData\MySQL` | 678.2 MB | MySQL | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\ProgramData\MySQL\MySQL Installer for Windows` | 678.2 MB | MySQL | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Microsoft\EdgeCore\Optimized` | 664.33 MB | Microsoft | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Program Files (x86)\Microsoft\EdgeCore\149.0.4022.69` | 664.33 MB | Microsoft | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Roaming\QQ` | 663.88 MB | QQ | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\AppData\Roaming\Seewo\EasiNote5\Dependencies` | 642.43 MB | Seewo | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Users\28170\.rustup\toolchains\stable-x86_64-pc-windows-msvc\share` | 636.86 MB | Rust / rustup | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 Rust / rustup, 暂无清理方式, 谨慎处理 |
| `C:\Users\28170\.rustup\toolchains\stable-x86_64-pc-windows-msvc\share\doc` | 636.46 MB | Rust / rustup | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 Rust / rustup, 暂无清理方式, 谨慎处理 |
| `C:\Users\28170\.rustup\toolchains\stable-x86_64-pc-windows-msvc\share\doc\rust` | 636.33 MB | Rust / rustup | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 归属 Rust / rustup, 暂无清理方式, 谨慎处理 |
| `C:\Users\28170\AppData\Roaming\Tencent\WeMeet` | 626.46 MB | 腾讯系列 (QQ/微信等) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\14.44.35207\lib\x86` | 626.44 MB | Visual Studio 生成工具 2022 | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删 |
| `C:\Users\28170\AppData\Roaming\LarkShell\aha` | 623.58 MB | 飞书 (Lark) | C 谨慎 | 谨慎处理: 建议先备份或确认用途 | 应用数据/配置 (删后软件重置或丢登录态) |
| `C:\Windows` | 41.21 GB | Windows 系统 | D 高风险 | 严禁删除/修改 | 用途: Windows 系统文件; 命中系统关键黑名单; 规则 win-system-root |
| `C:\Windows\WinSxS` | 14.44 GB | Windows 系统 | D 高风险 | 仅用 DISM /StartComponentCleanup，禁止手动删除 | 用途: 组件存储 (WinSxS, 仅用 DISM 清理); 命中系统关键黑名单; 规则 win-winsxs |
| `C:\pagefile.sys` | 11.39 GB | Windows 系统 | D 高风险 | 经 系统>高级>虚拟内存 调整，勿直删 | 用途: 虚拟内存页面文件 (经 系统>高级>虚拟内存 调整); 命中系统关键黑名单; 规则 sys-pagefile |
| `C:\Windows\System32` | 10.48 GB | Windows 系统 | D 高风险 | 严禁删除 | 用途: 系统核心组件; 命中系统关键黑名单; 规则 win-system32 |
| `C:\Windows\Installer` | 4.9 GB | Windows 系统 | D 高风险 | 不要手动删除；通过软件官方卸载/修复处理 | 用途: MSI 安装缓存 (修复/卸载所需, 勿直删); 命中系统关键黑名单; 规则 win-installer-cache |
| `C:\Windows\System32\DriverStore` | 4.61 GB | Windows 系统 | D 高风险 | 严禁删除；用设备管理器/pnputil 管理 | 用途: 驱动程序仓库 (用设备管理器/pnputil 管理); 命中系统关键黑名单; 规则 win-driverstore |
| `C:\Windows\System32\DriverStore\FileRepository` | 4.61 GB | Windows 系统 | D 高风险 | 严禁删除；用设备管理器/pnputil 管理 | 用途: 驱动程序仓库 (用设备管理器/pnputil 管理); 命中系统关键黑名单; 规则 win-driverstore |
| `C:\Windows\assembly` | 3.18 GB | Windows 系统 | D 高风险 | 严禁删除/修改 | 用途: .NET 本机映像缓存; 命中系统关键黑名单; 规则 win-system-root |
| `C:\Windows\System32\DriverStore\FileRepository\nvlt.inf_amd64_014a6c420cc5bf89` | 1.94 GB | Windows 系统 | D 高风险 | 严禁删除；用设备管理器/pnputil 管理 | 用途: 驱动程序仓库 (用设备管理器/pnputil 管理); 命中系统关键黑名单; 规则 win-driverstore |
| `C:\Windows\assembly\NativeImages_v4.0.30319_32` | 1.48 GB | Windows 系统 | D 高风险 | 严禁删除/修改 | 用途: .NET 本机映像缓存; 命中系统关键黑名单; 规则 win-system-root |
| `C:\Windows\SysWOW64` | 1.35 GB | Windows 系统 | D 高风险 | 严禁删除 | 用途: 32 位系统组件; 命中系统关键黑名单; 规则 win-syswow64 |
| `C:\Windows\assembly\NativeImages_v4.0.30319_64` | 1.33 GB | Windows 系统 | D 高风险 | 严禁删除/修改 | 用途: .NET 本机映像缓存; 命中系统关键黑名单; 规则 win-system-root |
| `C:\Windows\System32\DriverStore\FileRepository\u0397945.inf_amd64_16a50ea7e60ebe3c` | 1.32 GB | Windows 系统 | D 高风险 | 严禁删除；用设备管理器/pnputil 管理 | 用途: 驱动程序仓库 (用设备管理器/pnputil 管理); 命中系统关键黑名单; 规则 win-driverstore |
| `C:\Windows\System32\DriverStore\FileRepository\u0397945.inf_amd64_16a50ea7e60ebe3c\B397614` | 1.32 GB | Windows 系统 | D 高风险 | 严禁删除；用设备管理器/pnputil 管理 | 用途: 驱动程序仓库 (用设备管理器/pnputil 管理); 命中系统关键黑名单; 规则 win-driverstore |
| `C:\Windows\Fonts` | 1.29 GB | Windows 系统 | D 高风险 | 勿删；经字体设置管理 | 用途: 系统字体 (经字体设置管理); 命中系统关键黑名单; 规则 win-fonts |
| `C:\Windows\Temp` | 1.28 GB | Windows 系统 | D 高风险 | 严禁删除/修改 | 用途: 系统临时文件 (通常可清理); 命中系统关键黑名单; 规则 win-system-root |
| `C:\Windows\SystemApps` | 1.25 GB | Windows 系统 | D 高风险 | 严禁删除/修改 | 用途: 系统内置应用; 命中系统关键黑名单; 规则 win-system-root |
| `C:\ProgramData\Package Cache` | 858.85 MB | Package Cache | D 高风险 | 用对应官方安装器(如 VS Installer)管理，勿直删 | 用途: 安装包缓存 (经官方安装器管理); 命中系统关键黑名单; 规则 programdata-package-cache |
| `C:\Windows\servicing` | 822.43 MB | Windows 系统 | D 高风险 | 严禁删除/修改 | 用途: Windows 更新与维护数据; 命中系统关键黑名单; 规则 win-system-root |
| `C:\Windows\Microsoft.NET` | 780.49 MB | Windows 系统 | D 高风险 | 严禁删除/修改 | 用途: .NET 运行时; 命中系统关键黑名单; 规则 win-system-root |
| `C:\Windows\servicing\Sessions` | 681.09 MB | Windows 系统 | D 高风险 | 严禁删除/修改 | 用途: Windows 更新与维护数据; 命中系统关键黑名单; 规则 win-system-root |
| `C:\Windows\Temp\Whesvc` | 659.5 MB | Windows 系统 | D 高风险 | 严禁删除/修改 | 用途: 系统临时文件 (通常可清理); 命中系统关键黑名单; 规则 win-system-root |
| `C:\Windows\System32\Microsoft-Edge-WebView` | 626.04 MB | Windows 系统 | D 高风险 | 严禁删除 | 用途: 系统核心组件; 命中系统关键黑名单; 规则 win-system32 |
| `C:\Windows\WinSxS\amd64_microsoft-edge-webview_31bf3856ad364e35_10.0.26100.8655_none_2e9ef7333ad05fb3` | 624.69 MB | Windows 系统 | D 高风险 | 仅用 DISM /StartComponentCleanup，禁止手动删除 | 用途: 组件存储 (WinSxS, 仅用 DISM 清理); 命中系统关键黑名单; 规则 win-winsxs |
| `C:\Windows\WinSxS\amd64_microsoft-edge-webview_31bf3856ad364e35_10.0.26100.8457_none_2eb4697b3ac05b13` | 624.16 MB | Windows 系统 | D 高风险 | 仅用 DISM /StartComponentCleanup，禁止手动删除 | 用途: 组件存储 (WinSxS, 仅用 DISM 清理); 命中系统关键黑名单; 规则 win-winsxs |

