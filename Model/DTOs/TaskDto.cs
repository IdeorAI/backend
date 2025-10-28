namespace IdeorAI.Model.DTOs;

/// <summary>
/// DTO para criação de task
/// </summary>
public class CreateTaskDto
{
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string Phase { get; set; } = null!;
    public string? Content { get; set; }
}

/// <summary>
/// DTO para atualização de task
/// </summary>
public class UpdateTaskDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Content { get; set; }
}

/// <summary>
/// DTO para mudança de status de task
/// </summary>
public class ChangeTaskStatusDto
{
    public string NewStatus { get; set; } = null!;
}

/// <summary>
/// DTO de resposta de task
/// </summary>
public class TaskResponseDto
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string Phase { get; set; } = null!;
    public string? Content { get; set; }
    public string Status { get; set; } = null!;
    public object? EvaluationResult { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int EvaluationsCount { get; set; }
}

/// <summary>
/// DTO para geração de documento (etapa)
/// </summary>
public class GenerateDocumentDto
{
    public string Phase { get; set; } = null!;
    public Dictionary<string, string> Inputs { get; set; } = new();
}

/// <summary>
/// DTO de resposta de geração de documento
/// </summary>
public class GenerateDocumentResponseDto
{
    public Guid TaskId { get; set; }
    public string Phase { get; set; } = null!;
    public string GeneratedContent { get; set; } = null!;
    public string ModelUsed { get; set; } = null!;
    public int TokensUsed { get; set; }
    public string Status { get; set; } = null!;
}
