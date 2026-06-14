using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CleanScope.Infrastructure.Storage;

// 手写 SQL 映射辅助 (不用 EF, 决议8)。读取扩展 + 值转换 + JSON 列序列化。

/// <summary>SqliteDataReader 按列名读取 (含 DBNull / 枚举 / 时间 / JSON 处理)。</summary>
internal static class SqlReadExtensions
{
    public static long Int64(this SqliteDataReader r, string c) => r.GetInt64(r.GetOrdinal(c));
    public static long? Int64N(this SqliteDataReader r, string c)
    { var i = r.GetOrdinal(c); return r.IsDBNull(i) ? null : r.GetInt64(i); }

    public static int Int32(this SqliteDataReader r, string c) => (int)r.GetInt64(r.GetOrdinal(c));

    public static string Str(this SqliteDataReader r, string c) => r.GetString(r.GetOrdinal(c));
    public static string? StrN(this SqliteDataReader r, string c)
    { var i = r.GetOrdinal(c); return r.IsDBNull(i) ? null : r.GetString(i); }

    public static bool Bool(this SqliteDataReader r, string c) => r.GetInt64(r.GetOrdinal(c)) != 0;
    public static bool? BoolN(this SqliteDataReader r, string c)
    { var i = r.GetOrdinal(c); return r.IsDBNull(i) ? null : r.GetInt64(i) != 0; }

    public static double? DoubleN(this SqliteDataReader r, string c)
    { var i = r.GetOrdinal(c); return r.IsDBNull(i) ? null : r.GetDouble(i); }

    public static DateTime Date(this SqliteDataReader r, string c) =>
        DateTime.Parse(r.Str(c), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    public static DateTime? DateN(this SqliteDataReader r, string c)
    { var s = r.StrN(c); return s is null ? null : DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); }

    public static T EnumOf<T>(this SqliteDataReader r, string c) where T : struct, Enum =>
        Enum.Parse<T>(r.Str(c));
    public static T? EnumOfN<T>(this SqliteDataReader r, string c) where T : struct, Enum
    { var s = r.StrN(c); return s is null ? null : Enum.Parse<T>(s); }

    /// <summary>JSON 数组列 → List。空/缺失返回空列表。</summary>
    public static IReadOnlyList<T> JsonList<T>(this SqliteDataReader r, string c)
    {
        var s = r.StrN(c);
        if (string.IsNullOrWhiteSpace(s)) return Array.Empty<T>();
        return JsonSerializer.Deserialize<List<T>>(s) ?? new List<T>();
    }
}

/// <summary>值 → SQLite 参数的转换 (null→DBNull, bool→0/1, 枚举→文本, 时间→ISO8601, 列表→JSON)。</summary>
internal static class SqlValue
{
    public static object N(object? v) => v ?? DBNull.Value;
    public static int B(bool v) => v ? 1 : 0;
    public static object BN(bool? v) => v.HasValue ? (v.Value ? 1 : 0) : DBNull.Value;
    public static string Iso(DateTime v) => v.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    public static object IsoN(DateTime? v) => v.HasValue ? Iso(v.Value) : DBNull.Value;
    public static object EnumN<T>(T? v) where T : struct, Enum => v.HasValue ? v.Value.ToString() : (object)DBNull.Value;
    public static string Json<T>(T v) => JsonSerializer.Serialize(v);

    /// <summary>绑定一个参数 (已做 null→DBNull)。</summary>
    public static void Bind(this SqliteCommand cmd, string name, object? value) =>
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
