using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace IdeorAI.Model.SupabaseModels;

/// <summary>
/// Model Supabase para tabela generated_documents (spec 019).
/// Armazena documentos finais sintetizados (pitch-deck, business-plan, executive-summary).
/// </summary>
[Table("generated_documents")]
public class GeneratedDocumentModel : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = null!;

    [Column("project_id")]
    public string ProjectId { get; set; } = null!;

    [Column("doc_type")]
    public string DocType { get; set; } = null!;

    [Column("content_md")]
    public string ContentMd { get; set; } = null!;

    [Column("model_used")]
    public string? ModelUsed { get; set; }

    [Column("generated_at")]
    public DateTime GeneratedAt { get; set; }
}
