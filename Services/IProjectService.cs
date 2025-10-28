using IdeorAI.Model.Entities;

namespace IdeorAI.Services;

/// <summary>
/// Interface para serviço de gerenciamento de projetos
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// Obtém um projeto por ID com validação de ownership
    /// </summary>
    /// <param name="projectId">ID do projeto</param>
    /// <param name="userId">ID do usuário (para validação)</param>
    /// <returns>Projeto ou null se não encontrado/não autorizado</returns>
    Task<Project?> GetByIdAsync(Guid projectId, Guid userId);

    /// <summary>
    /// Lista todos os projetos de um usuário
    /// </summary>
    /// <param name="userId">ID do usuário</param>
    /// <param name="includeDeleted">Se deve incluir projetos soft-deleted</param>
    /// <returns>Lista de projetos</returns>
    Task<List<Project>> GetUserProjectsAsync(Guid userId, bool includeDeleted = false);

    /// <summary>
    /// Cria um novo projeto
    /// </summary>
    /// <param name="project">Dados do projeto</param>
    /// <param name="userId">ID do usuário criador</param>
    /// <returns>Projeto criado</returns>
    Task<Project> CreateAsync(Project project, Guid userId);

    /// <summary>
    /// Atualiza um projeto existente
    /// </summary>
    /// <param name="projectId">ID do projeto</param>
    /// <param name="userId">ID do usuário (validação)</param>
    /// <param name="updateAction">Ação de atualização</param>
    /// <returns>Projeto atualizado ou null se não autorizado</returns>
    Task<Project?> UpdateAsync(Guid projectId, Guid userId, Action<Project> updateAction);

    /// <summary>
    /// Deleta um projeto (soft delete)
    /// </summary>
    /// <param name="projectId">ID do projeto</param>
    /// <param name="userId">ID do usuário (validação)</param>
    /// <returns>True se deletado com sucesso</returns>
    Task<bool> DeleteAsync(Guid projectId, Guid userId);

    /// <summary>
    /// Muda a fase do projeto (ex: fase1 -> fase2)
    /// </summary>
    /// <param name="projectId">ID do projeto</param>
    /// <param name="userId">ID do usuário (validação)</param>
    /// <param name="newPhase">Nova fase</param>
    /// <returns>Projeto atualizado ou null se não autorizado</returns>
    Task<Project?> ChangePhaseAsync(Guid projectId, Guid userId, string newPhase);

    /// <summary>
    /// Atualiza o breakdown de progresso
    /// </summary>
    /// <param name="projectId">ID do projeto</param>
    /// <param name="userId">ID do usuário (validação)</param>
    /// <param name="progressData">Dados de progresso (será serializado para jsonb)</param>
    /// <returns>Projeto atualizado ou null se não autorizado</returns>
    Task<Project?> UpdateProgressAsync(Guid projectId, Guid userId, object progressData);
}
