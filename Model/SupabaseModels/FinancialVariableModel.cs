using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace IdeorAI.Model.SupabaseModels;

/// <summary>
/// Model Supabase para tabela financial_variables (Spec 027).
/// Fonte de verdade única dos valores financeiros do projeto: as etapas 4/5
/// gravam aqui; a DRE lê para se ancorar; a edição da DRE propaga de volta.
/// </summary>
[Table("financial_variables")]
public class FinancialVariableModel : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = null!;

    [Column("project_id")]
    public string ProjectId { get; set; } = null!;

    /// <summary>Identificador canônico (ex.: receita_mensal_media, cac, custo_desenvolvimento_total).</summary>
    [Column("key")]
    public string Key { get; set; } = null!;

    [Column("value")]
    public decimal Value { get; set; }

    /// <summary>Unidade: BRL, BRL/mês, pct, meses, ratio.</summary>
    [Column("unit")]
    public string Unit { get; set; } = null!;

    /// <summary>Etapa de origem: etapa4 | etapa5 | dre.</summary>
    [Column("source_stage")]
    public string SourceStage { get; set; } = null!;

    /// <summary>Caminho no JSON da etapa de origem (write-back). Null quando não isolável.</summary>
    [Column("source_path")]
    public string? SourcePath { get; set; }

    /// <summary>C2: editado à mão na DRE → não é sobrescrito ao regenerar a etapa.</summary>
    [Column("locked")]
    public bool Locked { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
