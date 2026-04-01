using System.Text.Json;

namespace IdeorAI.Model.Entities;

/// <summary>
/// Entity que mapeia a tabela 'ia_evaluations' do Supabase
/// Armazena histórico de chamadas LLM (Gemini, OpenAI, etc)
/// </summary>
public class IaEvaluation
{
    /// <summary>
    /// ID único da avaliação
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// ID da task avaliada (FK para tasks)
    /// </summary>
    public Guid TaskId { get; set; }

    /// <summary>
    /// Texto de entrada enviado ao modelo
    /// </summary>
    public string? InputText { get; set; }

    /// <summary>
    /// JSON de saída retornado pelo modelo
    /// </summary>
    public JsonDocument? OutputJson { get; set; }

    /// <summary>
    /// Nome do modelo usado (ex: "gemini-2.5-flash", "gpt-4o")
    /// </summary>
    public string? ModelUsed { get; set; }

    /// <summary>
    /// Tokens consumidos na chamada
    /// </summary>
    public int? TokensUsed { get; set; }

    /// <summary>
    /// Data/hora da avaliação
    /// </summary>
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    /// <summary>
    /// Task relacionada a esta avaliação
    /// </summary>
    public ProjectTask Task { get; set; } = null!;
}
