using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace IdeorAI.Model.SupabaseModels;

/// <summary>
/// Model Supabase para tabela marketplace_listings
/// </summary>
[Table("marketplace_listings")]
public class MarketplaceListingModel : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = null!;

    [Column("project_id")]
    public string? ProjectId { get; set; }

    [Column("owner_id")]
    public string OwnerId { get; set; } = null!;

    [Column("title")]
    public string Title { get; set; } = null!;

    [Column("description")]
    public string? Description { get; set; }

    [Column("category")]
    public string? Category { get; set; }

    /// <summary>
    /// "project" ou "service"
    /// </summary>
    [Column("listing_type")]
    public string ListingType { get; set; } = "project";

    [Column("contact_email")]
    public string? ContactEmail { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("published_at")]
    public DateTime PublishedAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
