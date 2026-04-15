using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace IdeorAI.Model.SupabaseModels;

/// <summary>
/// Snapshot do IVO gravado cada vez que RecalculateAndPersistAsync é chamado.
/// Usado para exibir o gráfico de evolução do IVO ao longo do tempo.
/// </summary>
[Table("ivo_history")]
public class IvoHistoryModel : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("project_id")]
    public string ProjectId { get; set; } = "";

    [Column("ivo_index")]
    public decimal IvoIndex { get; set; }

    [Column("ivo_score_10")]
    public decimal IvoScore10 { get; set; }

    [Column("ivo_o")]
    public decimal IvoO { get; set; }

    [Column("ivo_m")]
    public decimal IvoM { get; set; }

    [Column("ivo_v")]
    public decimal IvoV { get; set; }

    [Column("ivo_e")]
    public decimal IvoE { get; set; }

    [Column("ivo_t")]
    public decimal IvoT { get; set; }

    [Column("ivo_d")]
    public decimal IvoD { get; set; }

    [Column("recorded_at")]
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
