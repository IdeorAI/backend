using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System.Text.Json;

namespace IdeorAI.Model.SupabaseModels;

/// <summary>
/// Model Supabase para tabela projects
/// </summary>
[Table("projects")]
public class ProjectModel : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = null!;

    [Column("owner_id")]
    public string OwnerId { get; set; } = null!;

    [Column("name")]
    public string Name { get; set; } = null!;

    [Column("description")]
    public string? Description { get; set; }

    [Column("score")]
    public decimal Score { get; set; }

    [Column("valuation")]
    public decimal Valuation { get; set; }

    [Column("progress_breakdown")]
    public JsonElement? ProgressBreakdown { get; set; }

    [Column("current_phase")]
    public string CurrentPhase { get; set; } = "fase1";

    [Column("category")]
    public string? Category { get; set; }

    [Column("generated_options")]
    public string[]? GeneratedOptions { get; set; }

    [Column("product_structure")]
    public string? ProductStructure { get; set; }

    [Column("target_audience")]
    public string? TargetAudience { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    // Navigation properties (loaded via joins)
    public List<TaskModel>? Tasks { get; set; }
}
