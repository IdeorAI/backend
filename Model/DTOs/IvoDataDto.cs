namespace IdeorAI.Model.DTOs;

/// <summary>
/// DTO com todos os dados do IVO (Ideor Value Opportunity Index) de um projeto.
/// IsPartial = true quando alguma variável O/M/V/E/T ainda está no valor padrão (5.0),
/// indicando que nem todas as etapas foram avaliadas pela IA.
/// </summary>
public record IvoDataDto(
    decimal ScoreIvo,
    decimal O,
    decimal M,
    decimal V,
    decimal E,
    decimal T,
    decimal D,
    decimal IvoValue,
    decimal IvoIndex,
    bool IsPartial
);
