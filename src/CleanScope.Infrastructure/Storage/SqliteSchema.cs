namespace CleanScope.Infrastructure.Storage;

// 数据库 DDL —— 对应「数据模型设计.md §4」的 11 张表。
// 幂等 (IF NOT EXISTS), InitializeAsync 可重复执行。
// 关键安全约束: risk_assessment.evidence_chain 非空 (SR-5, CHECK length>2 防 "[]")。
public static class SqliteSchema
{
    public const string CreateScript = """
        PRAGMA foreign_keys = ON;

        CREATE TABLE IF NOT EXISTS scan_task (
          id           INTEGER PRIMARY KEY,
          target_path  TEXT NOT NULL,
          mode         TEXT NOT NULL,
          status       TEXT NOT NULL,
          started_at   TEXT NOT NULL,
          finished_at  TEXT,
          total_size   INTEGER,
          file_count   INTEGER,
          app_version  TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS file_node (
          id                INTEGER PRIMARY KEY,
          task_id           INTEGER NOT NULL REFERENCES scan_task(id),
          parent_id         INTEGER REFERENCES file_node(id),
          path              TEXT NOT NULL,
          real_path         TEXT,
          name              TEXT NOT NULL,
          is_directory      INTEGER NOT NULL,
          is_reparse_point  INTEGER NOT NULL DEFAULT 0,
          size              INTEGER NOT NULL,
          node_type         TEXT,
          mtime             TEXT,
          atime             TEXT,
          access_state      TEXT NOT NULL,
          preliminary_class TEXT,
          created_at        TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_filenode_task_size ON file_node(task_id, size DESC);
        CREATE INDEX IF NOT EXISTS ix_filenode_parent    ON file_node(parent_id);

        CREATE TABLE IF NOT EXISTS file_metadata (
          file_id            INTEGER PRIMARY KEY REFERENCES file_node(id),
          extension          TEXT,
          description        TEXT,
          product_name       TEXT,
          company_name       TEXT,
          file_version       TEXT,
          is_signed          INTEGER,
          signer             TEXT,
          sha256             TEXT,
          in_use             INTEGER,
          occupying_process  TEXT
        );

        CREATE TABLE IF NOT EXISTS evidence (
          id          INTEGER PRIMARY KEY,
          file_id     INTEGER NOT NULL REFERENCES file_node(id),
          kind        TEXT NOT NULL,
          value       TEXT NOT NULL,
          source      TEXT,
          is_fact     INTEGER NOT NULL,
          weight      REAL,
          created_at  TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_evidence_file ON evidence(file_id);

        CREATE TABLE IF NOT EXISTS rule_match (
          id                 INTEGER PRIMARY KEY,
          file_id            INTEGER NOT NULL REFERENCES file_node(id),
          rule_id            TEXT NOT NULL,
          category           TEXT,
          risk_level         TEXT,
          direct_delete      INTEGER,
          is_system_critical INTEGER,
          recommended_action TEXT,
          confidence         REAL,
          priority           INTEGER,
          authoritative      INTEGER NOT NULL DEFAULT 1
        );
        CREATE INDEX IF NOT EXISTS ix_rulematch_file ON rule_match(file_id);

        CREATE TABLE IF NOT EXISTS attribution_candidate (
          id                      INTEGER PRIMARY KEY,
          file_id                 INTEGER NOT NULL REFERENCES file_node(id),
          app_name                TEXT NOT NULL,
          confidence              REAL NOT NULL,
          rank                    INTEGER,
          supporting_evidence_ids TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_attr_file ON attribution_candidate(file_id);

        CREATE TABLE IF NOT EXISTS risk_assessment (
          id                  INTEGER PRIMARY KEY,
          file_id             INTEGER NOT NULL UNIQUE REFERENCES file_node(id),
          level               TEXT NOT NULL,
          score               INTEGER NOT NULL,
          factors             TEXT,
          evidence_chain      TEXT NOT NULL,
          can_delete_directly INTEGER NOT NULL,
          confidence          REAL,
          created_at          TEXT NOT NULL,
          CHECK (length(evidence_chain) > 2)
        );

        CREATE TABLE IF NOT EXISTS ai_explanation (
          id                        INTEGER PRIMARY KEY,
          file_id                   INTEGER NOT NULL UNIQUE REFERENCES file_node(id),
          what_is_it                TEXT,
          owner_app                 TEXT,
          risk_level                TEXT,
          can_delete_directly       INTEGER,
          recommended_action        TEXT,
          reasoning                 TEXT,
          confidence                REAL,
          user_friendly_explanation TEXT,
          validated                 INTEGER NOT NULL DEFAULT 0,
          model_used                TEXT,
          is_cloud                  INTEGER NOT NULL DEFAULT 0,
          created_at                TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS user_decision (
          id         INTEGER PRIMARY KEY,
          file_id    INTEGER NOT NULL REFERENCES file_node(id),
          decision   TEXT NOT NULL,
          note       TEXT,
          decided_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS ignore_entry (
          id              INTEGER PRIMARY KEY,
          path_or_pattern TEXT NOT NULL,
          match_type      TEXT NOT NULL,
          reason          TEXT,
          created_at      TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS action_log (
          id                   INTEGER PRIMARY KEY,
          file_id              INTEGER REFERENCES file_node(id),
          target_path          TEXT,
          action               TEXT NOT NULL,
          before_state         TEXT,
          recycle_bin_location TEXT,
          recoverable          INTEGER NOT NULL,
          operator             TEXT NOT NULL,
          result               TEXT NOT NULL,
          reject_reason        TEXT,
          app_version          TEXT NOT NULL,
          timestamp            TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_actionlog_time ON action_log(timestamp DESC);

        CREATE TABLE IF NOT EXISTS ai_insight (
          path        TEXT PRIMARY KEY,
          origin      TEXT,
          purpose     TEXT,
          created_at  TEXT NOT NULL
        );
        """;

    /// <summary>业务表名 (供测试/校验)。</summary>
    public static readonly string[] TableNames =
    {
        "scan_task", "file_node", "file_metadata", "evidence", "rule_match",
        "attribution_candidate", "risk_assessment", "ai_explanation",
        "user_decision", "ignore_entry", "action_log", "ai_insight"
    };
}
