"""drop nlu training tables

Gỡ bộ huấn luyện NLU ngoài tiến trình: xóa ba bảng của luồng huấn luyện NLU (``nlu_examples``, ``nlu_candidates``, ``nlu_model_versions``) và bỏ loại model ``rasa`` (giá trị đã lưu của bộ NLU cũ) khỏi ``ml_models``.
Câu mẫu có nhãn đã được xuất sang ``data/intent/examples.jsonl`` (nguồn của bộ phân loại ý định chạy trong tiến trình), nên không còn bảng nào giữ chúng.

Bước lùi tạo lại ba bảng RỖNG đúng như trước (lấy bằng pg_dump); dữ liệu trong đó không khôi phục được từ đây: lấy từ bản sao lưu DB, hoặc nạp lại từ tệp ví dụ.

Revision ID: 0036
Revises: 0035
"""
from typing import Sequence, Union

from alembic import op

revision: str = '0036'
down_revision: Union[str, Sequence[str], None] = '0035'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None

_RECREATE_NLU_TABLES = """
CREATE TABLE public.nlu_candidates (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id_hash character varying(64) NOT NULL,
    text text NOT NULL,
    text_key character varying(400) NOT NULL,
    predicted_intent character varying(32),
    predicted_confidence double precision,
    unclear_reason character varying(24) NOT NULL,
    status character varying(16) DEFAULT 'pending'::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chk_nlu_candidates_status CHECK (((status)::text = ANY ((ARRAY['pending'::character varying, 'rejected'::character varying])::text[]))),
    CONSTRAINT chk_nlu_candidates_user_hash CHECK (((user_id_hash)::text ~ '^[0-9a-f]{64}$'::text))
);
CREATE TABLE public.nlu_examples (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    text text NOT NULL,
    text_key character varying(400) NOT NULL,
    intent character varying(32) NOT NULL,
    split character varying(16) NOT NULL,
    tag character varying(40),
    source character varying(32) NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chk_nlu_examples_source CHECK (((source)::text = ANY ((ARRAY['seed'::character varying, 'user_feedback'::character varying, 'human'::character varying])::text[]))),
    CONSTRAINT chk_nlu_examples_split CHECK (((split)::text = ANY ((ARRAY['train'::character varying, 'val'::character varying, 'test'::character varying])::text[]))),
    CONSTRAINT chk_nlu_examples_text_len CHECK ((char_length(text) <= 200))
);
CREATE TABLE public.nlu_model_versions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    version character varying(64) NOT NULL,
    artifact_path text NOT NULL,
    status character varying(16) NOT NULL,
    dataset_hash character varying(64) NOT NULL,
    dataset_counts jsonb,
    eval_raw jsonb,
    eval_final jsonb,
    notes text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    activated_at timestamp with time zone,
    CONSTRAINT chk_nlu_model_versions_dataset_hash CHECK ((((dataset_hash)::text = 'unknown'::text) OR ((dataset_hash)::text ~ '^[0-9a-f]{64}$'::text))),
    CONSTRAINT chk_nlu_model_versions_status CHECK (((status)::text = ANY ((ARRAY['candidate'::character varying, 'active'::character varying, 'retired'::character varying, 'rejected'::character varying])::text[])))
);
ALTER TABLE ONLY public.nlu_candidates
    ADD CONSTRAINT nlu_candidates_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.nlu_examples
    ADD CONSTRAINT nlu_examples_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.nlu_model_versions
    ADD CONSTRAINT nlu_model_versions_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.nlu_candidates
    ADD CONSTRAINT uq_nlu_candidates_user_text UNIQUE (user_id_hash, text_key);
ALTER TABLE ONLY public.nlu_examples
    ADD CONSTRAINT uq_nlu_examples_text_key UNIQUE (text_key);
ALTER TABLE ONLY public.nlu_model_versions
    ADD CONSTRAINT uq_nlu_model_versions_version UNIQUE (version);
CREATE INDEX idx_nlu_candidates_status_created ON public.nlu_candidates USING btree (status, created_at);
CREATE INDEX idx_nlu_examples_split_intent ON public.nlu_examples USING btree (split, intent);
CREATE UNIQUE INDEX uq_nlu_model_versions_active ON public.nlu_model_versions USING btree (status) WHERE ((status)::text = 'active'::text);
"""


def upgrade() -> None:
    op.execute("DROP TABLE IF EXISTS nlu_examples, nlu_candidates, nlu_model_versions")
    op.execute("DELETE FROM ml_models WHERE kind = 'rasa'")
    op.execute("ALTER TABLE ml_models DROP CONSTRAINT IF EXISTS chk_ml_models_kind")
    op.execute("ALTER TABLE ml_models ADD CONSTRAINT chk_ml_models_kind CHECK (kind IN ('sklearn', 'llm', 'rules'))")


def downgrade() -> None:
    op.execute("ALTER TABLE ml_models DROP CONSTRAINT IF EXISTS chk_ml_models_kind")
    op.execute("ALTER TABLE ml_models ADD CONSTRAINT chk_ml_models_kind CHECK (kind IN ('sklearn', 'rasa', 'llm', 'rules'))")
    for statement in [part.strip() for part in _RECREATE_NLU_TABLES.split(";") if part.strip()]:
        op.execute(statement)
