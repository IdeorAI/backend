using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System.Text.Json;

namespace IdeorAI.Model.SupabaseModels;

/// <summary>
/// Model Supabase para tabela tasks
/// </summary>
[Table("tasks")]
public class TaskModel : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = null!;

    [Column("project_id")]
    public string ProjectId { get; set; } = null!;

    [Column("title")]
    public string Title { get; set; } = null!;

    [Column("description")]
    public string? Description { get; set; }

    [Column("phase")]
    public string Phase { get; set; } = null!;

    [Column("content")]
    public string? Content { get; set; }

    [Column("status")]
    public string Status { get; set; } = "draft";

    [Column("evaluation_result")]
    public JsonElement? EvaluationResult { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public ProjectModel? Project { get; set; }
    public List<IaEvaluationModel>? IaEvaluations { get; set; }
}
