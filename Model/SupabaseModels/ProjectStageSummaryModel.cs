using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System.Text.Json;

namespace IdeorAI.Model.SupabaseModels;

/// <summary>
/// Model Supabase para tabela project_stage_summaries
/// Armazena resumos estruturados de cada etapa para contexto acumulado
/// </summary>
[Table("project_stage_summaries")]
public class ProjectStageSummaryModel : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = null!;

    [Column("project_id")]
    public string ProjectId { get; set; } = null!;

    [Column("user_id")]
    public string UserId { get; set; } = null!;

    [Column("stage")]
    public string Stage { get; set; } = null!;

    [Column("summary_json")]
    public JsonElement SummaryJson { get; set; }

    [Column("summary_text")]
    public string SummaryText { get; set; } = null!;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
