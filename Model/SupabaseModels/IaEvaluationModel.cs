using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace IdeorAI.Model.SupabaseModels;

/// <summary>
/// Model Supabase para tabela ia_evaluations
/// </summary>
[Table("ia_evaluations")]
public class IaEvaluationModel : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = null!;

    [Column("task_id")]
    public string TaskId { get; set; } = null!;

    [Column("input_text")]
    public string? InputText { get; set; }

    /// <summary>
    /// Armazenado como string JSON para evitar problemas de serialização com Newtonsoft.Json
    /// </summary>
    [Column("output_json")]
    public string? OutputJson { get; set; }

    [Column("model_used")]
    public string? ModelUsed { get; set; }

    [Column("tokens_used")]
    public int? TokensUsed { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    // Navigation property — ignorado na serialização para evitar loop
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public TaskModel? Task { get; set; }
}
