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

    [Column("is_public")]
    public bool IsPublic { get; set; } = false;

    [Column("slug")]
    public string? Slug { get; set; }

    [Column("generated_options")]
    public string[]? GeneratedOptions { get; set; }

    [Column("product_structure")]
    public string? ProductStructure { get; set; }

    [Column("target_audience")]
    public string? TargetAudience { get; set; }

    [Column("region")]
    public string? Region { get; set; }

    [Column("constraints")]
    public string? Constraints { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    // IVO — Ideor Value Opportunity Index
    [Column("ivo_score_10")]
    public decimal IvoScore10 { get; set; } = 1.0m;

    [Column("ivo_o")]
    public decimal IvoO { get; set; } = 5.0m;

    [Column("ivo_m")]
    public decimal IvoM { get; set; } = 5.0m;

    [Column("ivo_v")]
    public decimal IvoV { get; set; } = 5.0m;

    [Column("ivo_e")]
    public decimal IvoE { get; set; } = 5.0m;

    [Column("ivo_t")]
    public decimal IvoT { get; set; } = 5.0m;

    [Column("ivo_d")]
    public decimal IvoD { get; set; } = 1.0m;

    [Column("ivo_index")]
    public decimal IvoIndex { get; set; } = 100.0m;

    // Navigation properties (loaded via joins)
    public List<TaskModel>? Tasks { get; set; }
}
