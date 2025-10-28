namespace IdeorAI.Model.DTOs;

/// <summary>
/// DTO para criação de projeto
/// </summary>
public class CreateProjectDto
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? ProductStructure { get; set; }
    public string? TargetAudience { get; set; }
}

/// <summary>
/// DTO para atualização de projeto
/// </summary>
public class UpdateProjectDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? ProductStructure { get; set; }
    public string? TargetAudience { get; set; }
    public decimal? Score { get; set; }
    public decimal? Valuation { get; set; }
}

/// <summary>
/// DTO para mudança de fase
/// </summary>
public class ChangePhaseDto
{
    public string NewPhase { get; set; } = null!;
}

/// <summary>
/// DTO para atualização de progresso
/// </summary>
public class UpdateProgressDto
{
    public Dictionary<string, object> Progress { get; set; } = new();
}

/// <summary>
/// DTO de resposta de projeto (simplificado)
/// </summary>
public class ProjectResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public decimal Score { get; set; }
    public decimal Valuation { get; set; }
    public string CurrentPhase { get; set; } = null!;
    public string? Category { get; set; }
    public string? ProductStructure { get; set; }
    public string? TargetAudience { get; set; }
    public string[]? GeneratedOptions { get; set; }
    public object? ProgressBreakdown { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int TasksCount { get; set; }
}
