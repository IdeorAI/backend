using System.Text.Json;

namespace IdeorAI.Model.Entities;

/// <summary>
/// Entity que mapeia a tabela 'projects' do Supabase
/// Representa um projeto de startup com suas fases e progresso
/// </summary>
public class Project
{
    /// <summary>
    /// ID único do projeto
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// ID do dono do projeto (FK para profiles)
    /// </summary>
    public Guid OwnerId { get; set; }

    /// <summary>
    /// Nome do projeto
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Descrição do projeto (max 400 chars)
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Score do projeto (0.0 a 10.0)
    /// </summary>
    public decimal Score { get; set; }

    /// <summary>
    /// Valuation estimado do projeto
    /// </summary>
    public decimal Valuation { get; set; }

    /// <summary>
    /// Breakdown do progresso em formato JSON
    /// Ex: {"fase1": 100, "fase2": 45, "etapa1": true}
    /// </summary>
    public JsonDocument ProgressBreakdown { get; set; } = null!;

    /// <summary>
    /// Fase atual do projeto (fase1, fase2, etc)
    /// </summary>
    public string CurrentPhase { get; set; } = "fase1";

    /// <summary>
    /// Categoria do projeto
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Opções geradas pela IA (array de strings)
    /// </summary>
    public string[]? GeneratedOptions { get; set; }

    /// <summary>
    /// Estrutura do produto (SaaS, Marketplace, App, API, etc)
    /// </summary>
    public string? ProductStructure { get; set; }

    /// <summary>
    /// Público-alvo (B2B, B2C, Híbrido)
    /// </summary>
    public string? TargetAudience { get; set; }

    /// <summary>
    /// Data de criação do projeto
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Data da última atualização
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    /// <summary>
    /// Perfil do dono do projeto
    /// </summary>
    public Profile Owner { get; set; } = null!;

    /// <summary>
    /// Tasks/etapas do projeto
    /// </summary>
    public ICollection<ProjectTask> Tasks { get; set; } = new List<ProjectTask>();
}
