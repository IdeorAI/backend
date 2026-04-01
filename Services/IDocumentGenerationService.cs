using IdeorAI.Model.Entities;

namespace IdeorAI.Services;

/// <summary>
/// Interface para serviço de geração de documentos via IA
/// </summary>
public interface IDocumentGenerationService
{
    /// <summary>
    /// Gera um documento para uma etapa específica do projeto
    /// </summary>
    /// <param name="projectId">ID do projeto</param>
    /// <param name="userId">ID do usuário (validação)</param>
    /// <param name="stage">Etapa (etapa1, etapa2, etc)</param>
    /// <param name="inputs">Inputs do usuário para o prompt</param>
    /// <returns>Task criada com conteúdo gerado</returns>
    Task<ProjectTask?> GenerateDocumentAsync(
        Guid projectId,
        Guid userId,
        string stage,
        Dictionary<string, string> inputs);

    /// <summary>
    /// Regenera um documento existente com novos inputs
    /// </summary>
    /// <param name="taskId">ID da task</param>
    /// <param name="userId">ID do usuário (validação)</param>
    /// <param name="newInputs">Novos inputs</param>
    /// <returns>Task atualizada</returns>
    Task<ProjectTask?> RegenerateDocumentAsync(
        Guid taskId,
        Guid userId,
        Dictionary<string, string> newInputs);

    /// <summary>
    /// Refina um documento existente com feedback do usuário
    /// </summary>
    /// <param name="taskId">ID da task</param>
    /// <param name="userId">ID do usuário (validação)</param>
    /// <param name="feedback">Feedback para refinamento</param>
    /// <returns>Task atualizada</returns>
    Task<ProjectTask?> RefineDocumentAsync(
        Guid taskId,
        Guid userId,
        string feedback);
}
