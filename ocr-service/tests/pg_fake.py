"""Giả lập PostgreSQL bằng SQLite cho test: cùng câu SQL, chỉ đổi %s thành ?. Không thay được việc thử trên PostgreSQL thật."""
import sqlite3

DDL = """
CREATE TABLE bill_raw_samples (id TEXT PRIMARY KEY, source TEXT NOT NULL, package_name TEXT NOT NULL, notification_title TEXT,
  notification_body TEXT NOT NULL, received_at TEXT NOT NULL, user_id_hash TEXT, content_hash TEXT NOT NULL UNIQUE);
CREATE TABLE bill_labeled_samples (id TEXT PRIMARY KEY, raw_sample_id TEXT NOT NULL UNIQUE, is_financial BOOLEAN NOT NULL, transaction_type TEXT,
  amount_cents INTEGER, merchant_name TEXT, category_slug TEXT, labeled_by TEXT NOT NULL, labeled_at TEXT, is_gold BOOLEAN);
CREATE TABLE bill_user_merchant_category (id TEXT PRIMARY KEY, user_id_hash TEXT NOT NULL, merchant_key TEXT NOT NULL, display_name TEXT,
  category_slug TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, UNIQUE (user_id_hash, merchant_key));
CREATE TABLE bill_brands (id TEXT PRIMARY KEY, brand_key TEXT NOT NULL UNIQUE, category_slug TEXT NOT NULL, display_name TEXT NOT NULL,
  whole_word BOOLEAN NOT NULL DEFAULT 0, is_active BOOLEAN NOT NULL DEFAULT 1, source TEXT NOT NULL DEFAULT 'seed', created_at TEXT, updated_at TEXT);
CREATE TABLE bill_keywords (id TEXT PRIMARY KEY, kind TEXT NOT NULL, category_slug TEXT NOT NULL, keyword TEXT NOT NULL,
  category_priority INTEGER NOT NULL DEFAULT 100, is_active BOOLEAN NOT NULL DEFAULT 1, source TEXT NOT NULL DEFAULT 'seed', created_at TEXT,
  UNIQUE (kind, category_slug, keyword));
CREATE TABLE bill_training_jobs (id TEXT PRIMARY KEY, stage TEXT NOT NULL, status TEXT NOT NULL, params TEXT, sample_count INTEGER,
  started_at TEXT, finished_at TEXT, error_message TEXT, created_at TEXT);
CREATE TABLE bill_model_versions (id TEXT PRIMARY KEY, stage TEXT NOT NULL, version TEXT NOT NULL, artifact_path TEXT NOT NULL, status TEXT NOT NULL,
  metrics TEXT, training_job_id TEXT, trained_at TEXT, notes TEXT, created_at TEXT, updated_at TEXT, UNIQUE (stage, version));
CREATE TABLE bill_evaluation_runs (id TEXT PRIMARY KEY, model_version_id TEXT NOT NULL, split TEXT NOT NULL, sample_count INTEGER NOT NULL,
  accuracy REAL NOT NULL, macro_f1 REAL NOT NULL, metrics TEXT, notes TEXT, created_at TEXT);
CREATE TABLE bill_evaluation_category_metrics (id TEXT PRIMARY KEY, evaluation_run_id TEXT NOT NULL, label TEXT NOT NULL, "precision" REAL NOT NULL,
  recall REAL NOT NULL, f1 REAL NOT NULL, support INTEGER NOT NULL, UNIQUE (evaluation_run_id, label));
CREATE TABLE chat_expenses (id TEXT PRIMARY KEY, user_id_hash TEXT NOT NULL, amount_vnd INTEGER NOT NULL, category_slug TEXT NOT NULL,
  description TEXT, merchant TEXT, created_at TEXT NOT NULL, kind TEXT NOT NULL DEFAULT 'expense');
CREATE TABLE chat_unclear_messages (id TEXT PRIMARY KEY, user_id_hash TEXT NOT NULL, text TEXT NOT NULL, text_hash TEXT NOT NULL UNIQUE, reason TEXT NOT NULL,
  rasa_intent TEXT, rasa_confidence REAL, rules_intent TEXT, final_intent TEXT, predicted_category TEXT, corrected_category TEXT,
  seen_count INTEGER NOT NULL DEFAULT 1, status TEXT NOT NULL DEFAULT 'new', first_seen_at TEXT, last_seen_at TEXT);
CREATE TABLE bill_unknown_scans (id TEXT PRIMARY KEY, user_id_hash TEXT NOT NULL, merchant_read TEXT, merchant_key TEXT NOT NULL DEFAULT '',
  predicted_category TEXT, predicted_source TEXT, confidence REAL, total_amount INTEGER, lines TEXT NOT NULL, content_hash TEXT NOT NULL UNIQUE,
  seen_count INTEGER NOT NULL DEFAULT 1, status TEXT NOT NULL DEFAULT 'new', first_seen_at TEXT, last_seen_at TEXT);
"""


class _Cur:
    def __init__(self, cur):
        self._c = cur

    def execute(self, sql, params=()):
        self._c.execute(sql.replace("%s", "?"), params)
        return self

    @property
    def rowcount(self):
        return self._c.rowcount

    def fetchone(self):
        return self._c.fetchone()

    def fetchall(self):
        return self._c.fetchall()


class _Conn:
    def __init__(self, path):
        self._c = sqlite3.connect(path)

    def __enter__(self):
        self._c.__enter__()
        return self

    def __exit__(self, *exc):
        return self._c.__exit__(*exc)

    def cursor(self):
        return _Cur(self._c.cursor())

    def close(self):
        self._c.close()


def make_connect(path):
    boot = sqlite3.connect(path)
    boot.executescript(DDL)
    boot.close()
    return lambda: _Conn(path)
