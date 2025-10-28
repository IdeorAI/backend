using IdeorAI.Model.Entities;

namespace IdeorAI.Services;

/// <summary>
/// Interface para serviço de gerenciamento de etapas (stages) dos projetos
/// </summary>
public interface IStageService
{
    /// <summary>
    /// Cria uma nova task/etapa para um projeto
    /// </summary>
    /// <param name="projectId">ID do projeto</param>
    /// <param name="userId">ID do usuário (validação)</param>
    /// <param name="task">Dados da task</param>
    /// <returns>Task criada ou null se não autorizado</returns>
    Task<ProjectTask?> CreateTaskAsync(Guid projectId, Guid userId, ProjectTask task);

    /// <summary>
    /// Obtém uma task por ID com validação de ownership
    /// </summary>
    /// <param name="taskId">ID da task</param>
    /// <param name="userId">ID do usuário (validação)</param>
    /// <returns>Task ou null se não encontrada/não autorizada</returns>
    Task<ProjectTask?> GetTaskByIdAsync(Guid taskId, Guid userId);

    /// <summary>
    /// Lista todas as tasks de um projeto
    /// </summary>
    /// <param name="projectId">ID do projeto</param>
    /// <param name="userId">ID do usuário (validação)</param>
    /// <returns>Lista de tasks ou null se não autorizado</returns>
    Task<List<ProjectTask>?> GetProjectTasksAsync(Guid projectId, Guid userId);

    /// <summary>
    /// Atualiza uma task existente
    /// </summary>
    /// <param name="taskId">ID da task</param>
    /// <param name="userId">ID do usuário (validação)</param>
    /// <param name="updateAction">Ação de atualização</param>
    /// <returns>Task atualizada ou null se não autorizado</returns>
    Task<ProjectTask?> UpdateTaskAsync(Guid taskId, Guid userId, Action<ProjectTask> updateAction);

    /// <summary>
    /// Muda o status de uma task (draft -> submitted -> evaluated)
    /// </summary>
    /// <param name="taskId">ID da task</param>
    /// <param name="userId">ID do usuário (validação)</param>
    /// <param name="newStatus">Novo status</param>
    /// <returns>Task atualizada ou null se não autorizado</returns>
    Task<ProjectTask?> ChangeTaskStatusAsync(Guid taskId, Guid userId, string newStatus);

    /// <summary>
    /// Verifica se um projeto pode avançar para a próxima fase
    /// </summary>
    /// <param name="projectId">ID do projeto</param>
    /// <param name="userId">ID do usuário (validação)</param>
    /// <returns>True se pode avançar</returns>
    Task<bool> CanAdvanceToNextPhaseAsync(Guid projectId, Guid userId);

    /// <summary>
    /// Obtém a próxima etapa disponível para um projeto na fase2
    /// </summary>
    /// <param name="projectId">ID do projeto</param>
    /// <param name="userId">ID do usuário (validação)</param>
    /// <returns>Nome da próxima etapa (etapa1, etapa2, etc) ou null se todas completas</returns>
    Task<string?> GetNextAvailableStageAsync(Guid projectId, Guid userId);
}
