namespace CleanScope.Domain.Enums;

// 领域枚举 —— 与「数据模型设计.md §4/§5」的 TEXT 枚举列一一对应。
// 纯定义, 无逻辑 (T0.4)。

/// <summary>扫描权限模式 (scan_task.mode)。</summary>
public enum ScanMode { Normal, Admin }

/// <summary>扫描任务状态 (scan_task.status)。</summary>
public enum ScanStatus { Pending, Running, Completed, Interrupted, Failed }

/// <summary>文件/目录可访问状态 (file_node.access_state)。SR-10: 无权限记录而非猜测。</summary>
public enum AccessState { Accessible, NeedAdmin, Denied }

/// <summary>节点类型 (file_node.node_type)。</summary>
public enum NodeType { File, Directory, Installer, Cache, Log, Database, Dump, Archive, VirtualDisk, Unknown }

/// <summary>初步分类 (file_node.preliminary_class)。</summary>
public enum PreliminaryClass { System, App, UserData, Cache, Unknown }

/// <summary>风险等级 A–E (风险分级细则)。默认落点 C; 黑名单强制 D。</summary>
public enum RiskLevel { A, B, C, D, E }

/// <summary>证据种类 (evidence.kind)。AiInference 为 AI 推测, 其余为事实来源。</summary>
public enum EvidenceKind { PathRule, Metadata, Signature, InstalledApp, Registry, Process, PackageMgr, Extension, AiInference }

/// <summary>用户决策 (user_decision.decision)。</summary>
public enum DecisionType { Processed, Ignored, RemindLater }

/// <summary>规则/忽略匹配方式 (ignore_entry.match_type / 规则 match_type)。</summary>
public enum MatchType { Exact, Prefix, Glob }

/// <summary>操作类型 (action_log.action)。MVP 仅辅助操作; MoveToRecycleBin 自 Beta 起。</summary>
public enum ActionType { OpenDir, CopyPath, OpenSettings, ShowCommand, AddIgnore, ExportReport, MoveToRecycleBin }

/// <summary>操作结果 (action_log.result)。MVP 删除恒为 Rejected。</summary>
public enum ActionResult { Success, Rejected, Failed }

/// <summary>操作发起者 (action_log.operator)。</summary>
public enum Operator { User, System }

/// <summary>数据敏感度分级 (数据模型 §6)。供脱敏网关判定本地存储与上云策略。</summary>
public enum Sensitivity { P0_Public, P1_Pii, P2_Secret }
