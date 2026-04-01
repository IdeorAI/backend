-- Migration: Criação da tabela project_stage_summaries
-- Data: 2026-04-01

-- Criação da tabela
CREATE TABLE IF NOT EXISTS project_stage_summaries (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    user_id UUID NOT NULL,
    stage VARCHAR(10) NOT NULL CHECK (stage IN ('etapa1', 'etapa2', 'etapa3', 'etapa4', 'etapa5')),
    summary_json JSONB NOT NULL,
    summary_text TEXT NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    UNIQUE (project_id, stage)
);

-- Índices para performance
CREATE INDEX IF NOT EXISTS idx_stage_summaries_project ON project_stage_summaries(project_id);
CREATE INDEX IF NOT EXISTS idx_stage_summaries_stage ON project_stage_summaries(stage);
CREATE INDEX IF NOT EXISTS idx_stage_summaries_user ON project_stage_summaries(user_id);

-- Trigger para atualizar updated_at automaticamente
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ language 'plpgsql';

-- Remover trigger existente se houver
DROP TRIGGER IF EXISTS update_project_stage_summaries_updated_at ON project_stage_summaries;

-- Criar trigger
CREATE TRIGGER update_project_stage_summaries_updated_at
    BEFORE UPDATE ON project_stage_summaries
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

-- Comentários para documentação
COMMENT ON TABLE project_stage_summaries IS 'Resumos estruturados de cada etapa do IdeorAI para contexto acumulado';
COMMENT ON COLUMN project_stage_summaries.summary_json IS 'JSON completo da análise da etapa';
COMMENT ON COLUMN project_stage_summaries.summary_text IS 'Versão resumida em texto (max 800 chars) para injeção no prompt';
