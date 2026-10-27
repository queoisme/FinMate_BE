"""ml models and merge bill tables

Giai đoạn 3 của ERD v5_1: tạo ``ml_models`` và GỘP các bảng ``bill_*`` trùng chức năng vào bộ ``ml_*`` chung. Hàng giữ nguyên ``id`` nên mọi khóa ngoại còn đúng.

  training_jobs + bill_training_jobs                     -> ml_training_jobs          (cột ``stage`` thành ``model_id`` trỏ ml_models)
  model_versions + bill_model_versions                   -> ml_model_versions
  evaluation_runs / _category_metrics / _predictions     -> ml_evaluation_* (+ bill_evaluation_runs / _category_metrics)
  labeled_samples + bill_labeled_samples                 -> ml_labeled_samples
  ml_raw_samples (đổi tên ở 0021) + bill_raw_samples     -> ml_raw_samples            (mẫu hóa đơn giữ ``source = 'bill_scan'``)

Thống nhất giá trị: trạng thái phiên bản ``archived`` -> ``retired``; trạng thái job ``finished`` (bill) -> ``succeeded``; loại giao dịch của bill ``expense`` ->
``debit`` (``income`` -> ``credit``). Bảng nhãn nới CHECK ``labeled_by`` thành hợp của hai bên và thay CHECK liệt kê slug bằng khóa ngoại ``kb_categories`` (nhóm cũ của bill
là dòng bí danh ở đó). Bảng đánh giá nhận thêm split ``cv`` (kiểm tra chéo theo cửa hàng của model hóa đơn).

Mọi ràng buộc mới đều ADD rồi VALIDATE nên dữ liệu cũ vi phạm sẽ dừng migration và nêu tên ràng buộc (DDL của Postgres nằm trong giao dịch). Bước lùi dựng lại
sáu bảng ``bill_*`` và chuyển hàng về (mẫu thô ``bill_scan``, model không thuộc ``classifier``/``categorizer``); riêng ``retired`` của hai model thông báo về lại ``archived``.

Revision ID: 0023
Revises: 0022
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql


# revision identifiers, used by Alembic.
revision: str = '0023'
down_revision: Union[str, Sequence[str], None] = '0022'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None

# (code, kind, provider, notes): bản sao cố định; stage lạ trong dữ liệu cũ được thêm tự động bên dưới.
MODELS = [
    ('classifier', 'sklearn', None, 'Thông báo ngân hàng: có phải giao dịch không (scripts/train.py --stage classifier)'),
    ('categorizer', 'sklearn', None, 'Thông báo ngân hàng: danh mục của giao dịch (scripts/train.py --stage categorizer)'),
    ('bill_slug', 'sklearn', None, 'Danh mục hóa đơn học từ nhãn người dùng (app/bill/slug_model.py)'),
    ('bill_category', 'sklearn', None, 'Nhóm danh mục hóa đơn do ocr-service huấn luyện (classification/category_model.py)'),
]
NOTIFICATION_MODELS = ('classifier', 'categorizer')  # phần còn lại là model hóa đơn (dùng cho bước lùi)

SOURCES_NEW = "'notification', 'manual_entry', 'receipt', 'seed', 'bill_scan'"
SOURCES_OLD = "'notification', 'manual_entry', 'receipt', 'seed'"
LABELED_BY_NEW = "'seed', 'user_feedback', 'human', 'user', 'imported', 'gold', 'synthetic_ocr'"
LABELED_BY_OLD = "'seed', 'user_feedback', 'human'"
CATEGORY_SLUGS_OLD = "'food', 'transport', 'shopping', 'education', 'housing', 'bills', 'entertainment', 'health', 'family', 'income', 'other'"
HEX64 = "~ '^[0-9a-f]{64}$'"


def _validated(table: str, name: str, expr: str) -> None:
    op.execute(f"ALTER TABLE {table} ADD CONSTRAINT {name} CHECK ({expr}) NOT VALID")
    op.execute(f"ALTER TABLE {table} VALIDATE CONSTRAINT {name}")


def upgrade() -> None:
    # ---- ml_models
    op.create_table('ml_models',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('code', sa.String(length=48), nullable=False),
    sa.Column('kind', sa.String(length=16), nullable=False),
    sa.Column('provider', sa.String(length=32), nullable=True),
    sa.Column('notes', sa.Text(), nullable=True),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint("kind IN ('sklearn', 'rasa', 'llm', 'rules')", name='chk_ml_models_kind'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('code', name='uq_ml_models_code')
    )
    models = sa.table('ml_models', sa.column('code', sa.String), sa.column('kind', sa.String), sa.column('provider', sa.String), sa.column('notes', sa.Text))
    op.bulk_insert(models, [{'code': c, 'kind': k, 'provider': p, 'notes': n} for c, k, p, n in MODELS])
    op.execute(
        "INSERT INTO ml_models (code, kind, notes) "
        "SELECT DISTINCT stage, 'sklearn', 'Thêm tự động lúc gộp bảng: stage này có trong dữ liệu cũ, hãy sửa kind/notes cho đúng' "
        "FROM (SELECT stage FROM training_jobs UNION SELECT stage FROM model_versions "
        "      UNION SELECT stage FROM bill_training_jobs UNION SELECT stage FROM bill_model_versions) AS found "
        "WHERE stage NOT IN (SELECT code FROM ml_models)"
    )

    # ---- ml_training_jobs
    op.rename_table('training_jobs', 'ml_training_jobs')
    op.add_column('ml_training_jobs', sa.Column('model_id', sa.UUID(), nullable=True))
    op.add_column('ml_training_jobs', sa.Column('dataset_hash', sa.String(length=64), nullable=True))
    op.execute("UPDATE ml_training_jobs j SET model_id = m.id FROM ml_models m WHERE m.code = j.stage")
    op.drop_index('idx_training_jobs_stage_created', table_name='ml_training_jobs')
    op.execute("ALTER TABLE ml_training_jobs DROP CONSTRAINT chk_training_jobs_stage")
    op.drop_column('ml_training_jobs', 'stage')  # trước khi chép hàng bill: cột này NOT NULL và bill đã có model_id
    op.execute(
        "INSERT INTO ml_training_jobs (id, model_id, status, params, sample_count, started_at, finished_at, error_message, created_at) "
        "SELECT b.id, m.id, CASE b.status WHEN 'finished' THEN 'succeeded' ELSE b.status END, b.params, b.sample_count, "
        "       COALESCE(b.started_at, b.created_at), b.finished_at, b.error_message, b.created_at "
        "FROM bill_training_jobs b JOIN ml_models m ON m.code = b.stage"
    )
    op.alter_column('ml_training_jobs', 'model_id', nullable=False)
    op.create_foreign_key('ml_training_jobs_model_id_fkey', 'ml_training_jobs', 'ml_models', ['model_id'], ['id'], ondelete='RESTRICT')
    op.create_index('idx_ml_training_jobs_model_created', 'ml_training_jobs', ['model_id', 'created_at'], unique=False)
    _validated('ml_training_jobs', 'chk_ml_training_jobs_sample_count', 'sample_count IS NULL OR sample_count >= 0')
    _validated('ml_training_jobs', 'chk_ml_training_jobs_dataset_hash', f'dataset_hash IS NULL OR dataset_hash {HEX64}')
    _validated('ml_training_jobs', 'chk_ml_training_jobs_times', 'finished_at IS NULL OR finished_at >= started_at')
    _validated('ml_training_jobs', 'chk_ml_training_jobs_params', "params IS NULL OR jsonb_typeof(params) = 'object'")

    # ---- ml_model_versions
    op.rename_table('model_versions', 'ml_model_versions')
    op.add_column('ml_model_versions', sa.Column('model_id', sa.UUID(), nullable=True))
    op.add_column('ml_model_versions', sa.Column('prompt_hash', sa.String(length=64), nullable=True))
    op.execute("UPDATE ml_model_versions v SET model_id = m.id FROM ml_models m WHERE m.code = v.stage")
    op.execute("ALTER TABLE ml_model_versions DROP CONSTRAINT uq_model_versions_stage_version")
    op.drop_index('uq_model_versions_active', table_name='ml_model_versions', postgresql_where=sa.text("status = 'active'"))
    op.execute("ALTER TABLE ml_model_versions DROP CONSTRAINT chk_model_versions_stage")
    op.execute("ALTER TABLE ml_model_versions DROP CONSTRAINT chk_model_versions_status")
    op.execute("UPDATE ml_model_versions SET status = 'retired' WHERE status = 'archived'")
    op.drop_column('ml_model_versions', 'stage')  # trước khi chép hàng bill: cột này NOT NULL và bill đã có model_id
    op.alter_column('ml_model_versions', 'artifact_path', nullable=True)
    op.execute(
        "INSERT INTO ml_model_versions (id, model_id, version, artifact_path, status, metrics, training_job_id, trained_at, notes, created_at, updated_at) "
        "SELECT b.id, m.id, b.version, b.artifact_path, b.status, b.metrics, b.training_job_id, b.trained_at, b.notes, b.created_at, b.updated_at "
        "FROM bill_model_versions b JOIN ml_models m ON m.code = b.stage"
    )
    op.alter_column('ml_model_versions', 'model_id', nullable=False)
    op.create_foreign_key('ml_model_versions_model_id_fkey', 'ml_model_versions', 'ml_models', ['model_id'], ['id'], ondelete='RESTRICT')
    op.create_unique_constraint('uq_ml_model_versions_model_version', 'ml_model_versions', ['model_id', 'version'])
    op.create_index('uq_ml_model_versions_active', 'ml_model_versions', ['model_id'], unique=True, postgresql_where=sa.text("status = 'active'"))
    _validated('ml_model_versions', 'chk_ml_model_versions_status', "status IN ('candidate', 'active', 'retired', 'rejected')")
    _validated('ml_model_versions', 'chk_ml_model_versions_prompt_hash', f'prompt_hash IS NULL OR prompt_hash {HEX64}')

    # ---- ml_evaluation_*
    op.rename_table('evaluation_runs', 'ml_evaluation_runs')
    op.execute("ALTER TABLE ml_evaluation_runs DROP CONSTRAINT chk_evaluation_runs_split")
    op.execute(
        "INSERT INTO ml_evaluation_runs (id, model_version_id, split, sample_count, accuracy, macro_f1, metrics, notes, created_at) "
        "SELECT id, model_version_id, split, sample_count, accuracy, macro_f1, metrics, notes, created_at FROM bill_evaluation_runs"
    )
    _validated('ml_evaluation_runs', 'chk_ml_evaluation_runs_split', "split IN ('train', 'val', 'test', 'cv')")
    _validated('ml_evaluation_runs', 'chk_ml_evaluation_runs_scores', 'accuracy BETWEEN 0 AND 1 AND macro_f1 BETWEEN 0 AND 1')
    _validated('ml_evaluation_runs', 'chk_ml_evaluation_runs_sample_count', 'sample_count >= 0')

    op.rename_table('evaluation_category_metrics', 'ml_evaluation_category_metrics')
    op.execute(
        'INSERT INTO ml_evaluation_category_metrics (id, evaluation_run_id, label, "precision", recall, f1, support) '
        'SELECT id, evaluation_run_id, label, "precision", recall, f1, support FROM bill_evaluation_category_metrics'
    )
    _validated('ml_evaluation_category_metrics', 'chk_ml_evaluation_category_metrics_scores', '"precision" BETWEEN 0 AND 1 AND recall BETWEEN 0 AND 1 AND f1 BETWEEN 0 AND 1')
    _validated('ml_evaluation_category_metrics', 'chk_ml_evaluation_category_metrics_support', 'support >= 0')

    op.rename_table('evaluation_predictions', 'ml_evaluation_predictions')
    op.create_unique_constraint('uq_ml_evaluation_predictions_run_sample', 'ml_evaluation_predictions', ['evaluation_run_id', 'labeled_sample_id'])
    _validated('ml_evaluation_predictions', 'chk_ml_evaluation_predictions_confidence', 'confidence BETWEEN 0 AND 1')

    # ---- ml_raw_samples (bảng đã đổi tên ở 0021)
    op.execute("ALTER TABLE ml_raw_samples DROP CONSTRAINT chk_raw_samples_source")
    op.execute(
        "INSERT INTO ml_raw_samples (id, source, package_name, notification_title, notification_body, received_at, user_id_hash, content_hash, created_at) "
        "SELECT id, source, package_name, notification_title, notification_body, received_at, user_id_hash, content_hash, created_at FROM bill_raw_samples"
    )
    _validated('ml_raw_samples', 'chk_raw_samples_source', f'source IN ({SOURCES_NEW})')

    # ---- ml_labeled_samples
    op.rename_table('labeled_samples', 'ml_labeled_samples')
    op.execute("ALTER TABLE ml_labeled_samples DROP CONSTRAINT chk_labeled_samples_labeled_by")
    op.execute("ALTER TABLE ml_labeled_samples DROP CONSTRAINT chk_labeled_samples_category_slug")
    op.execute(
        "INSERT INTO ml_labeled_samples (id, raw_sample_id, is_financial, transaction_type, amount_cents, merchant_name, category_slug, labeled_by, labeled_at, is_gold) "
        "SELECT id, raw_sample_id, is_financial, "
        "       CASE lower(transaction_type) WHEN 'expense' THEN 'debit' WHEN 'income' THEN 'credit' ELSE transaction_type END, "
        "       amount_cents, merchant_name, category_slug, labeled_by, labeled_at, is_gold FROM bill_labeled_samples"
    )
    _validated('ml_labeled_samples', 'chk_ml_labeled_samples_labeled_by', f'labeled_by IN ({LABELED_BY_NEW})')
    op.execute(
        "ALTER TABLE ml_labeled_samples ADD CONSTRAINT ml_labeled_samples_category_slug_fkey FOREIGN KEY (category_slug) "
        "REFERENCES kb_categories (slug) ON UPDATE CASCADE ON DELETE RESTRICT NOT VALID"
    )
    op.execute("ALTER TABLE ml_labeled_samples VALIDATE CONSTRAINT ml_labeled_samples_category_slug_fkey")
    op.create_index('idx_ml_labeled_samples_gold', 'ml_labeled_samples', ['id'], unique=False, postgresql_where=sa.text('is_gold'))

    # ---- bỏ các bảng đã gộp (con trước, cha sau)
    for table in ('bill_evaluation_category_metrics', 'bill_evaluation_runs', 'bill_model_versions', 'bill_training_jobs', 'bill_labeled_samples', 'bill_raw_samples'):
        op.drop_table(table)


def _create_bill_tables() -> None:
    op.create_table('bill_raw_samples',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('source', sa.String(length=32), nullable=False),
    sa.Column('package_name', sa.Text(), nullable=False),
    sa.Column('notification_title', sa.Text(), nullable=True),
    sa.Column('notification_body', sa.Text(), nullable=False),
    sa.Column('received_at', sa.DateTime(timezone=True), nullable=False),
    sa.Column('user_id_hash', sa.String(length=64), nullable=True),
    sa.Column('content_hash', sa.String(length=64), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint("source = 'bill_scan'", name='chk_bill_raw_samples_source'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('content_hash', name='uq_bill_raw_samples_content_hash')
    )
    op.create_index('idx_bill_raw_samples_created_at', 'bill_raw_samples', ['created_at'], unique=False)
    op.create_index('idx_bill_raw_samples_source', 'bill_raw_samples', ['source'], unique=False)
    op.create_table('bill_training_jobs',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('stage', sa.String(length=32), nullable=False),
    sa.Column('status', sa.String(length=16), nullable=False),
    sa.Column('params', postgresql.JSONB(astext_type=sa.Text()), nullable=True),
    sa.Column('sample_count', sa.Integer(), nullable=True),
    sa.Column('started_at', sa.DateTime(timezone=True), nullable=True),
    sa.Column('finished_at', sa.DateTime(timezone=True), nullable=True),
    sa.Column('error_message', sa.Text(), nullable=True),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.PrimaryKeyConstraint('id')
    )
    op.create_index('idx_bill_training_jobs_stage_created', 'bill_training_jobs', ['stage', 'created_at'], unique=False)
    op.create_table('bill_labeled_samples',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('raw_sample_id', sa.UUID(), nullable=False),
    sa.Column('is_financial', sa.Boolean(), nullable=False),
    sa.Column('transaction_type', sa.String(length=16), nullable=True),
    sa.Column('amount_cents', sa.BigInteger(), nullable=True),
    sa.Column('merchant_name', sa.Text(), nullable=True),
    sa.Column('category_slug', sa.String(length=32), nullable=True),
    sa.Column('labeled_by', sa.String(length=32), nullable=False),
    sa.Column('labeled_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('is_gold', sa.Boolean(), server_default=sa.text('false'), nullable=False),
    sa.CheckConstraint("category_slug IS NULL OR category_slug IN ('dining', 'transport', 'shopping', 'groceries', 'housing', 'utilities', 'healthcare', 'entertainment', 'other')", name='chk_bill_labeled_samples_category'),
    sa.CheckConstraint("labeled_by IN ('user', 'imported', 'gold', 'synthetic_ocr')", name='chk_bill_labeled_samples_labeled_by'),
    sa.ForeignKeyConstraint(['raw_sample_id'], ['bill_raw_samples.id'], ondelete='CASCADE'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('raw_sample_id', name='uq_bill_labeled_samples_raw_sample')
    )
    op.create_index('idx_bill_labeled_samples_category_slug', 'bill_labeled_samples', ['category_slug'], unique=False)
    op.create_table('bill_model_versions',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('stage', sa.String(length=32), nullable=False),
    sa.Column('version', sa.String(length=32), nullable=False),
    sa.Column('artifact_path', sa.Text(), nullable=False),
    sa.Column('status', sa.String(length=16), nullable=False),
    sa.Column('metrics', postgresql.JSONB(astext_type=sa.Text()), nullable=True),
    sa.Column('training_job_id', sa.UUID(), nullable=True),
    sa.Column('trained_at', sa.DateTime(timezone=True), nullable=True),
    sa.Column('notes', sa.Text(), nullable=True),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.ForeignKeyConstraint(['training_job_id'], ['bill_training_jobs.id'], ondelete='SET NULL'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('stage', 'version', name='uq_bill_model_versions_stage_version')
    )
    op.create_table('bill_evaluation_runs',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('model_version_id', sa.UUID(), nullable=False),
    sa.Column('split', sa.String(length=16), nullable=False),
    sa.Column('sample_count', sa.Integer(), nullable=False),
    sa.Column('accuracy', sa.Float(), nullable=False),
    sa.Column('macro_f1', sa.Float(), nullable=False),
    sa.Column('metrics', postgresql.JSONB(astext_type=sa.Text()), nullable=True),
    sa.Column('notes', sa.Text(), nullable=True),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.ForeignKeyConstraint(['model_version_id'], ['bill_model_versions.id'], ondelete='CASCADE'),
    sa.PrimaryKeyConstraint('id')
    )
    op.create_index('idx_bill_evaluation_runs_model_created', 'bill_evaluation_runs', ['model_version_id', 'created_at'], unique=False)
    op.create_table('bill_evaluation_category_metrics',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('evaluation_run_id', sa.UUID(), nullable=False),
    sa.Column('label', sa.String(length=64), nullable=False),
    sa.Column('precision', sa.Float(), nullable=False),
    sa.Column('recall', sa.Float(), nullable=False),
    sa.Column('f1', sa.Float(), nullable=False),
    sa.Column('support', sa.Integer(), nullable=False),
    sa.ForeignKeyConstraint(['evaluation_run_id'], ['bill_evaluation_runs.id'], ondelete='CASCADE'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('evaluation_run_id', 'label', name='uq_bill_eval_category_metrics')
    )


def downgrade() -> None:
    _create_bill_tables()
    bill_models = "(SELECT id FROM ml_models WHERE code NOT IN ('classifier', 'categorizer'))"

    # ---- hóa đơn: chuyển hàng về bảng bill_* (cha trước con)
    op.execute("INSERT INTO bill_raw_samples (id, source, package_name, notification_title, notification_body, received_at, user_id_hash, content_hash, created_at) "
               "SELECT id, source, package_name, notification_title, notification_body, received_at, user_id_hash, content_hash, created_at FROM ml_raw_samples WHERE source = 'bill_scan'")
    op.execute("INSERT INTO bill_labeled_samples (id, raw_sample_id, is_financial, transaction_type, amount_cents, merchant_name, category_slug, labeled_by, labeled_at, is_gold) "
               "SELECT l.id, l.raw_sample_id, l.is_financial, l.transaction_type, l.amount_cents, l.merchant_name, l.category_slug, l.labeled_by, l.labeled_at, l.is_gold "
               "FROM ml_labeled_samples l JOIN ml_raw_samples r ON r.id = l.raw_sample_id WHERE r.source = 'bill_scan'")
    op.execute("INSERT INTO bill_training_jobs (id, stage, status, params, sample_count, started_at, finished_at, error_message, created_at) "
               f"SELECT j.id, m.code, j.status, j.params, j.sample_count, j.started_at, j.finished_at, j.error_message, j.created_at FROM ml_training_jobs j JOIN ml_models m ON m.id = j.model_id WHERE j.model_id IN {bill_models}")
    op.execute("INSERT INTO bill_model_versions (id, stage, version, artifact_path, status, metrics, training_job_id, trained_at, notes, created_at, updated_at) "
               f"SELECT v.id, m.code, v.version, COALESCE(v.artifact_path, ''), v.status, v.metrics, v.training_job_id, v.trained_at, v.notes, v.created_at, v.updated_at FROM ml_model_versions v JOIN ml_models m ON m.id = v.model_id WHERE v.model_id IN {bill_models}")
    op.execute("INSERT INTO bill_evaluation_runs (id, model_version_id, split, sample_count, accuracy, macro_f1, metrics, notes, created_at) "
               f"SELECT e.id, e.model_version_id, e.split, e.sample_count, e.accuracy, e.macro_f1, e.metrics, e.notes, e.created_at FROM ml_evaluation_runs e JOIN ml_model_versions v ON v.id = e.model_version_id WHERE v.model_id IN {bill_models}")
    op.execute('INSERT INTO bill_evaluation_category_metrics (id, evaluation_run_id, label, "precision", recall, f1, support) '
               'SELECT id, evaluation_run_id, label, "precision", recall, f1, support FROM ml_evaluation_category_metrics WHERE evaluation_run_id IN (SELECT id FROM bill_evaluation_runs)')

    op.execute("DELETE FROM ml_evaluation_runs WHERE id IN (SELECT id FROM bill_evaluation_runs)")  # kéo theo metrics (CASCADE)
    op.execute(f"DELETE FROM ml_model_versions WHERE model_id IN {bill_models}")
    op.execute(f"DELETE FROM ml_training_jobs WHERE model_id IN {bill_models}")
    op.execute("DELETE FROM ml_labeled_samples WHERE raw_sample_id IN (SELECT id FROM bill_raw_samples)")
    op.execute("DELETE FROM ml_raw_samples WHERE source = 'bill_scan'")

    # ---- bảng labeled / raw về hình cũ
    op.drop_index('idx_ml_labeled_samples_gold', table_name='ml_labeled_samples', postgresql_where=sa.text('is_gold'))
    op.execute("ALTER TABLE ml_labeled_samples DROP CONSTRAINT ml_labeled_samples_category_slug_fkey")
    op.execute("ALTER TABLE ml_labeled_samples DROP CONSTRAINT chk_ml_labeled_samples_labeled_by")
    _validated('ml_labeled_samples', 'chk_labeled_samples_labeled_by', f'labeled_by IN ({LABELED_BY_OLD})')
    _validated('ml_labeled_samples', 'chk_labeled_samples_category_slug', f'category_slug IS NULL OR category_slug IN ({CATEGORY_SLUGS_OLD})')
    op.rename_table('ml_labeled_samples', 'labeled_samples')
    op.execute("ALTER TABLE ml_raw_samples DROP CONSTRAINT chk_raw_samples_source")
    _validated('ml_raw_samples', 'chk_raw_samples_source', f'source IN ({SOURCES_OLD})')

    # ---- đánh giá về hình cũ
    op.drop_constraint('uq_ml_evaluation_predictions_run_sample', 'ml_evaluation_predictions', type_='unique')
    op.execute("ALTER TABLE ml_evaluation_predictions DROP CONSTRAINT chk_ml_evaluation_predictions_confidence")
    op.rename_table('ml_evaluation_predictions', 'evaluation_predictions')
    op.execute("ALTER TABLE ml_evaluation_category_metrics DROP CONSTRAINT chk_ml_evaluation_category_metrics_scores")
    op.execute("ALTER TABLE ml_evaluation_category_metrics DROP CONSTRAINT chk_ml_evaluation_category_metrics_support")
    op.rename_table('ml_evaluation_category_metrics', 'evaluation_category_metrics')
    for name in ('chk_ml_evaluation_runs_split', 'chk_ml_evaluation_runs_scores', 'chk_ml_evaluation_runs_sample_count'):
        op.execute(f"ALTER TABLE ml_evaluation_runs DROP CONSTRAINT {name}")
    _validated('ml_evaluation_runs', 'chk_evaluation_runs_split', "split IN ('train', 'val', 'test')")
    op.rename_table('ml_evaluation_runs', 'evaluation_runs')

    # ---- phiên bản về hình cũ: thêm lại ``stage`` từ ml_models
    op.add_column('ml_model_versions', sa.Column('stage', sa.String(length=32), nullable=True))
    op.execute("UPDATE ml_model_versions v SET stage = m.code FROM ml_models m WHERE m.id = v.model_id")
    op.alter_column('ml_model_versions', 'stage', nullable=False)
    op.execute("ALTER TABLE ml_model_versions DROP CONSTRAINT chk_ml_model_versions_status")
    op.execute("ALTER TABLE ml_model_versions DROP CONSTRAINT chk_ml_model_versions_prompt_hash")
    op.execute("UPDATE ml_model_versions SET status = 'archived' WHERE status IN ('retired', 'rejected')")
    op.drop_index('uq_ml_model_versions_active', table_name='ml_model_versions', postgresql_where=sa.text("status = 'active'"))
    op.drop_constraint('uq_ml_model_versions_model_version', 'ml_model_versions', type_='unique')
    op.drop_constraint('ml_model_versions_model_id_fkey', 'ml_model_versions', type_='foreignkey')
    op.execute("UPDATE ml_model_versions SET artifact_path = '' WHERE artifact_path IS NULL")
    op.alter_column('ml_model_versions', 'artifact_path', nullable=False)
    op.drop_column('ml_model_versions', 'prompt_hash')
    op.drop_column('ml_model_versions', 'model_id')
    op.create_unique_constraint('uq_model_versions_stage_version', 'ml_model_versions', ['stage', 'version'])
    op.create_index('uq_model_versions_active', 'ml_model_versions', ['stage'], unique=True, postgresql_where=sa.text("status = 'active'"))
    _validated('ml_model_versions', 'chk_model_versions_stage', "stage IN ('classifier', 'categorizer')")
    _validated('ml_model_versions', 'chk_model_versions_status', "status IN ('candidate', 'active', 'archived')")
    op.rename_table('ml_model_versions', 'model_versions')

    # ---- lần huấn luyện về hình cũ
    op.add_column('ml_training_jobs', sa.Column('stage', sa.String(length=32), nullable=True))
    op.execute("UPDATE ml_training_jobs j SET stage = m.code FROM ml_models m WHERE m.id = j.model_id")
    op.alter_column('ml_training_jobs', 'stage', nullable=False)
    for name in ('chk_ml_training_jobs_sample_count', 'chk_ml_training_jobs_dataset_hash', 'chk_ml_training_jobs_times', 'chk_ml_training_jobs_params'):
        op.execute(f"ALTER TABLE ml_training_jobs DROP CONSTRAINT {name}")
    op.drop_index('idx_ml_training_jobs_model_created', table_name='ml_training_jobs')
    op.drop_constraint('ml_training_jobs_model_id_fkey', 'ml_training_jobs', type_='foreignkey')
    op.drop_column('ml_training_jobs', 'dataset_hash')
    op.drop_column('ml_training_jobs', 'model_id')
    op.create_index('idx_training_jobs_stage_created', 'ml_training_jobs', ['stage', 'created_at'], unique=False)
    _validated('ml_training_jobs', 'chk_training_jobs_stage', "stage IN ('classifier', 'categorizer')")
    op.rename_table('ml_training_jobs', 'training_jobs')

    op.drop_table('ml_models')
