namespace IdeorAI.Model.Entities;

/// <summary>
/// Entity que mapeia a tabela 'profiles' do Supabase
/// Representa o perfil de um usuário (linked com auth.users)
/// </summary>
public class Profile
{
    /// <summary>
    /// ID do perfil (FK para auth.users)
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Nome de usuário (opcional)
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Email do usuário
    /// </summary>
    public string Email { get; set; } = null!;

    /// <summary>
    /// Data de criação do perfil
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Data da última atualização
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    /// <summary>
    /// Projetos pertencentes a este usuário
    /// </summary>
    public ICollection<Project> Projects { get; set; } = new List<Project>();
}
