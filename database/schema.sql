CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS tb_user (
    id BIGSERIAL PRIMARY KEY,
    username VARCHAR(100) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL,
    create_time TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS tb_vehicle (
    id BIGSERIAL PRIMARY KEY,
    brand VARCHAR(100) NOT NULL,
    model VARCHAR(100) NOT NULL,
    year INT NOT NULL,
    engine VARCHAR(100),
    configuration VARCHAR(100),
    create_time TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS tb_user_vehicle (
    id BIGSERIAL PRIMARY KEY,
    user_id BIGINT NOT NULL REFERENCES tb_user(id) ON DELETE CASCADE,
    vehicle_id BIGINT NOT NULL REFERENCES tb_vehicle(id) ON DELETE RESTRICT,
    buy_date DATE,
    mileage INT,
    create_time TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS tb_manual (
    id BIGSERIAL PRIMARY KEY,
    vehicle_id BIGINT NOT NULL REFERENCES tb_vehicle(id) ON DELETE RESTRICT,
    file_name VARCHAR(255) NOT NULL,
    file_path VARCHAR(500),
    pdf_url VARCHAR(500),
    source_type VARCHAR(50),
    source_url VARCHAR(500),
    status VARCHAR(50) NOT NULL DEFAULT 'uploaded',
    total_pages INT NOT NULL DEFAULT 0,
    generated_page_images INT NOT NULL DEFAULT 0,
    knowledge_chunks INT NOT NULL DEFAULT 0,
    create_time TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS tb_manual_page (
    id BIGSERIAL PRIMARY KEY,
    manual_id BIGINT NOT NULL REFERENCES tb_manual(id) ON DELETE CASCADE,
    pdf_page_number INT NOT NULL,
    printed_page_number INT,
    chapter VARCHAR(255),
    page_text TEXT,
    page_image_url VARCHAR(500),
    create_time TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (manual_id, pdf_page_number)
);

CREATE TABLE IF NOT EXISTS tb_knowledge_chunk (
    id BIGSERIAL PRIMARY KEY,
    manual_id BIGINT NOT NULL REFERENCES tb_manual(id) ON DELETE CASCADE,
    manual_page_id BIGINT NOT NULL REFERENCES tb_manual_page(id) ON DELETE CASCADE,
    vehicle_id BIGINT NOT NULL REFERENCES tb_vehicle(id) ON DELETE RESTRICT,
    chapter VARCHAR(255),
    content TEXT NOT NULL,
    chunk_index INT NOT NULL,
    embedding VECTOR(1536),
    create_time TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS tb_chat_history (
    id BIGSERIAL PRIMARY KEY,
    user_id BIGINT NOT NULL REFERENCES tb_user(id) ON DELETE CASCADE,
    vehicle_id BIGINT NOT NULL REFERENCES tb_vehicle(id) ON DELETE RESTRICT,
    conversation_id VARCHAR(64),
    question TEXT NOT NULL,
    answer TEXT NOT NULL,
    references_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    create_time TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

ALTER TABLE tb_chat_history
    ADD COLUMN IF NOT EXISTS conversation_id VARCHAR(64);

CREATE INDEX IF NOT EXISTS idx_vehicle_lookup
    ON tb_vehicle (brand, model, year);

CREATE UNIQUE INDEX IF NOT EXISTS idx_vehicle_unique_version
    ON tb_vehicle (brand, model, year, (COALESCE(engine, '')), (COALESCE(configuration, '')));

CREATE INDEX IF NOT EXISTS idx_manual_vehicle_status
    ON tb_manual (vehicle_id, status);

CREATE INDEX IF NOT EXISTS idx_manual_page_manual_page
    ON tb_manual_page (manual_id, pdf_page_number);

CREATE INDEX IF NOT EXISTS idx_chunk_vehicle_manual
    ON tb_knowledge_chunk (vehicle_id, manual_id);

CREATE INDEX IF NOT EXISTS idx_chunk_embedding_hnsw
    ON tb_knowledge_chunk
    USING hnsw (embedding vector_cosine_ops);

CREATE INDEX IF NOT EXISTS idx_chat_user_vehicle_time
    ON tb_chat_history (user_id, vehicle_id, create_time DESC);

CREATE INDEX IF NOT EXISTS idx_chat_conversation_time
    ON tb_chat_history (user_id, vehicle_id, conversation_id, create_time);
