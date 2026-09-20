CREATE TABLE IF NOT EXISTS ProcessedIngests (
    event_id TEXT PRIMARY KEY,
    processed_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_ingests_processed_at ON ProcessedIngests (processed_at);

CREATE TABLE IF NOT EXISTS Domains (
    name TEXT PRIMARY KEY,
    description TEXT,
    shape_version TEXT NOT NULL DEFAULT 'v1',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS ProcessedIncidents (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    kind TEXT NOT NULL CHECK (kind IN ('log_error', 'exception', 'panic', 'build_failure', 'test_failure', 'custom')),
    domain TEXT NOT NULL REFERENCES Domains(name),
    shape_hash TEXT NOT NULL,
    shape_version TEXT NOT NULL DEFAULT 'v1',
    shape_template TEXT NOT NULL,
    origin TEXT,
    error_embedding VECTOR(384) NOT NULL,
    embedding_model TEXT NOT NULL DEFAULT 'all-MiniLM-L6-v2',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (kind, domain, shape_hash, shape_version)
);

CREATE INDEX IF NOT EXISTS idx_incidents_kind_domain ON ProcessedIncidents (kind, domain);
CREATE INDEX IF NOT EXISTS idx_incidents_domain_created ON ProcessedIncidents (domain, created_at);
CREATE INDEX IF NOT EXISTS idx_incidents_embedding ON ProcessedIncidents USING hnsw (error_embedding vector_cosine_ops);

CREATE TABLE IF NOT EXISTS IncidentVariants (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    incident_id UUID NOT NULL REFERENCES ProcessedIncidents(id) ON DELETE CASCADE,
    context_fingerprint TEXT NOT NULL,
    context_embedding VECTOR(384) NOT NULL,
    embedding_model TEXT NOT NULL DEFAULT 'all-MiniLM-L6-v2',
    context_json JSONB NOT NULL,
    context_sample TEXT,
    status TEXT NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'needs_review')),
    verified BOOLEAN NOT NULL DEFAULT false,
    merged_by_embedding BOOLEAN NOT NULL DEFAULT false,
    occurrence_count BIGINT NOT NULL DEFAULT 1,
    version BIGINT NOT NULL DEFAULT 1,
    first_seen_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_seen_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (incident_id, context_fingerprint)
);

CREATE INDEX IF NOT EXISTS idx_variants_incident ON IncidentVariants (incident_id);
CREATE INDEX IF NOT EXISTS idx_variants_embedding ON IncidentVariants USING hnsw (context_embedding vector_cosine_ops);

CREATE TABLE IF NOT EXISTS VariantSolutions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    variant_id UUID NOT NULL REFERENCES IncidentVariants(id) ON DELETE CASCADE,
    solution_text TEXT NOT NULL,
    patch_content TEXT,
    solution_type TEXT,
    confidence_score DOUBLE PRECISION,
    verified BOOLEAN NOT NULL DEFAULT false,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    superseded_at TIMESTAMPTZ,
    superseded_reason TEXT
);

CREATE UNIQUE INDEX IF NOT EXISTS one_current_solution_per_variant ON VariantSolutions (variant_id) WHERE superseded_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_solutions_variant ON VariantSolutions (variant_id);

CREATE TABLE IF NOT EXISTS SolutionFailures (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    variant_id UUID NOT NULL REFERENCES IncidentVariants(id) ON DELETE CASCADE,
    solution_id UUID REFERENCES VariantSolutions(id) ON DELETE SET NULL,
    failure_reason TEXT,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_failures_variant ON SolutionFailures (variant_id);

CREATE TABLE IF NOT EXISTS IncidentLinks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    from_incident UUID NOT NULL REFERENCES ProcessedIncidents(id) ON DELETE CASCADE,
    to_incident UUID NOT NULL REFERENCES ProcessedIncidents(id) ON DELETE CASCADE,
    similarity REAL NOT NULL,
    relation TEXT NOT NULL DEFAULT 'related',
    kind TEXT NOT NULL DEFAULT 'suggested' CHECK (kind IN ('suggested', 'confirmed', 'rejected')),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (from_incident, to_incident)
);

CREATE INDEX IF NOT EXISTS idx_links_from ON IncidentLinks (from_incident) WHERE kind != 'rejected';
CREATE INDEX IF NOT EXISTS idx_links_to ON IncidentLinks (to_incident) WHERE kind != 'rejected';

CREATE TABLE IF NOT EXISTS SystemConfig (
    key TEXT PRIMARY KEY,
    value JSONB NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
