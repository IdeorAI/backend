using Newtonsoft.Json.Linq;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace IdeorAI.Model.SupabaseModels;

/// <summary>
/// Model Supabase para tabela profiles. Inclui campos de onboarding, tooltips
/// e preferências do usuário (anteriormente faltavam, causando perda de dados em UPDATE).
/// </summary>
[Table("profiles")]
public class ProfileModel : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = null!;

    [Column("username")]
    public string? Username { get; set; }

    [Column("email")]
    public string Email { get; set; } = null!;

    [Column("bio")]
    public string? Bio { get; set; }

    [Column("theme_preference")]
    public string? ThemePreference { get; set; }

    [Column("notification_prefs")]
    public JToken? NotificationPrefs { get; set; }

    [Column("onboarding_completed")]
    public bool OnboardingCompleted { get; set; } = false;

    [Column("onboarding_answers")]
    public JToken? OnboardingAnswers { get; set; }

    [Column("seen_tooltips")]
    public JToken? SeenTooltips { get; set; }

    [Column("is_admin")]
    public bool IsAdmin { get; set; } = false;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
