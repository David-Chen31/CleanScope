// Core 项目内全局可见的 Domain 命名空间 (与 Infrastructure 同套打法)。
global using CleanScope.Domain.Abstractions;
global using CleanScope.Domain.Entities;
global using CleanScope.Domain.Enums;
global using CleanScope.Domain.Models;
// 别名: 根治领域枚举与 BCL 同名类型 (System.IO.MatchType / System.Xml NodeType) 的冲突。
global using MatchType = CleanScope.Domain.Enums.MatchType;
global using NodeType = CleanScope.Domain.Enums.NodeType;
