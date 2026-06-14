// Domain 项目内全局可见命名空间, 各文件无需重复 using。
global using CleanScope.Domain.Enums;
global using CleanScope.Domain.Entities;
global using CleanScope.Domain.Models;
// 别名: 根治与 System.IO.MatchType 的冲突 (别名优先于命名空间 using)。
global using MatchType = CleanScope.Domain.Enums.MatchType;
