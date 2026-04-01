using System.Text.Json;

namespace IdeorAI.Model.Entities;

/// <summary>
/// Entity que mapeia a tabela 'tasks' do Supabase
/// Representa uma etapa/tarefa dentro de um projeto (ex: etapa1, etapa2 da fase2)
/// Renomeado para ProjectTask para evitar conflito com System.Threading.Tasks.Task
/// </summary>
public class ProjectTask
{
    /// <summary>
    /// ID único da task
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// ID do projeto (FK para projects)
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Título da task (ex: "Problema e Oportunidade")
    /// </summary>
    public string Title { get; set; } = null!;

    /// <summary>
    /// Descrição da task
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Fase/etapa da task (ex: "etapa1", "etapa2", etc)
    /// </summary>
    public string Phase { get; set; } = null!;

    /// <summary>
    /// Conteúdo gerado ou inputs do usuário
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Status da task (draft, submitted, evaluated)
    /// </summary>
    public string Status { get; set; } = "draft";

    /// <summary>
    /// Resultado da avaliação em formato JSON
    /// </summary>
    public JsonDocument? EvaluationResult { get; set; }

    /// <summary>
    /// Data de criação da task
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Data da última atualização
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    /// <summary>
    /// Projeto ao qual a task pertence
    /// </summary>
    public Project Project { get; set; } = null!;

    /// <summary>
    /// Avaliações de IA relacionadas a esta task
    /// </summary>
    public ICollection<IaEvaluation> IaEvaluations { get; set; } = new List<IaEvaluation>();
}
