using IdeorAI.Client;
using IdeorAI.Data;
using IdeorAI.Model.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IdeorAI.Services;

/// <summary>
/// Serviço de geração de documentos via IA (Gemini)
/// </summary>
public class DocumentGenerationService : IDocumentGenerationService
{
    private readonly IdeorDbContext _context;
    private readonly GeminiApiClient _geminiClient;
    private readonly IStageService _stageService;
    private readonly ILogger<DocumentGenerationService> _logger;
    private readonly IConfiguration _configuration;

    // Mapeamento de etapas para títulos
    private static readonly Dictionary<string, string> StageTitles = new()
    {
        { "etapa1", "Problema e Oportunidade" },
        { "etapa2", "Pesquisa de Mercado" },
        { "etapa3", "Proposta de Valor" },
        { "etapa4", "Modelo de Negócio" },
        { "etapa5", "MVP (Minimum Viable Product)" },
        { "etapa6", "Equipe Mínima" },
        { "etapa7", "Pitch Deck + Plano Executivo + Resumo" }
    };

    public DocumentGenerationService(
        IdeorDbContext context,
        GeminiApiClient geminiClient,
        IStageService stageService,
        ILogger<DocumentGenerationService> logger,
        IConfiguration configuration)
    {
        _context = context;
        _geminiClient = geminiClient;
        _stageService = stageService;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<ProjectTask?> GenerateDocumentAsync(
        Guid projectId,
        Guid userId,
        string stage,
        Dictionary<string, string> inputs)
    {
        _logger.LogInformation("Generating document for project {ProjectId}, stage {Stage}", projectId, stage);

        // Validar stage
        if (!StageTitles.ContainsKey(stage))
        {
            _logger.LogWarning("Invalid stage: {Stage}", stage);
            return null;
        }

        // Gerar o prompt (usando versão simplificada em dev, completa em produção)
        string prompt;
        try
        {
            var useSimplified = _configuration.GetValue<bool>("PromptSettings:UseSimplifiedPrompts", false);

            if (useSimplified)
            {
                _logger.LogInformation("Using simplified prompts (development mode)");
                prompt = PromptResumidos.GetPromptForStage(stage, inputs);
            }
            else
            {
                _logger.LogInformation("Using full prompts (production mode)");
                prompt = PromptTemplates.GetPromptForStage(stage, inputs);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating prompt for stage {Stage}", stage);
            return null;
        }

        // Chamar Gemini API
        string generatedContent;
        try
        {
            generatedContent = await _geminiClient.GenerateContentAsync(prompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Gemini API for stage {Stage}", stage);
            return null;
        }

        // Criar a task com o conteúdo gerado
        var task = new ProjectTask
        {
            Title = StageTitles[stage],
            Description = $"Documento gerado automaticamente para a {StageTitles[stage]}",
            Phase = stage,
            Content = generatedContent,
            Status = "evaluated" // Já vem avaliado pela IA
        };

        var createdTask = await _stageService.CreateTaskAsync(projectId, userId, task);

        if (createdTask == null)
        {
            _logger.LogWarning("Failed to create task for project {ProjectId}", projectId);
            return null;
        }

        // Criar registro de avaliação de IA
        var evaluation = new IaEvaluation
        {
            Id = Guid.NewGuid(),
            TaskId = createdTask.Id,
            InputText = prompt,
            OutputJson = JsonDocument.Parse(generatedContent),
            ModelUsed = "gemini-2.5-flash",
            TokensUsed = EstimateTokens(prompt + generatedContent),
            CreatedAt = DateTime.UtcNow
        };

        _context.IaEvaluations.Add(evaluation);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Document generated successfully for task {TaskId}", createdTask.Id);

        return createdTask;
    }

    public async Task<ProjectTask?> RegenerateDocumentAsync(
        Guid taskId,
        Guid userId,
        Dictionary<string, string> newInputs)
    {
        _logger.LogInformation("Regenerating document for task {TaskId}", taskId);

        var task = await _stageService.GetTaskByIdAsync(taskId, userId);

        if (task == null)
        {
            return null;
        }

        // Gerar novo prompt com novos inputs (usando versão simplificada em dev, completa em produção)
        string prompt;
        try
        {
            var useSimplified = _configuration.GetValue<bool>("PromptSettings:UseSimplifiedPrompts", false);

            if (useSimplified)
            {
                prompt = PromptResumidos.GetPromptForStage(task.Phase, newInputs);
            }
            else
            {
                prompt = PromptTemplates.GetPromptForStage(task.Phase, newInputs);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating prompt for stage {Stage}", task.Phase);
            return null;
        }

        // Chamar Gemini API
        string generatedContent;
        try
        {
            generatedContent = await _geminiClient.GenerateContentAsync(prompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Gemini API");
            return null;
        }

        // Atualizar a task
        var updatedTask = await _stageService.UpdateTaskAsync(taskId, userId, t =>
        {
            t.Content = generatedContent;
            t.Status = "evaluated";
        });

        if (updatedTask == null)
        {
            return null;
        }

        // Criar novo registro de avaliação
        var evaluation = new IaEvaluation
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            InputText = prompt,
            OutputJson = JsonDocument.Parse(generatedContent),
            ModelUsed = "gemini-2.5-flash",
            TokensUsed = EstimateTokens(prompt + generatedContent),
            CreatedAt = DateTime.UtcNow
        };

        _context.IaEvaluations.Add(evaluation);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Document regenerated successfully for task {TaskId}", taskId);

        return updatedTask;
    }

    public async Task<ProjectTask?> RefineDocumentAsync(
        Guid taskId,
        Guid userId,
        string feedback)
    {
        _logger.LogInformation("Refining document for task {TaskId}", taskId);

        var task = await _stageService.GetTaskByIdAsync(taskId, userId);

        if (task == null || string.IsNullOrWhiteSpace(task.Content))
        {
            return null;
        }

        // Criar prompt de refinamento
        var refinementPrompt = $@"Você é um assistente de refinamento de documentos de startup.

## Documento Original:
{task.Content}

## Feedback do usuário:
{feedback}

## Instruções:
Refine o documento acima incorporando o feedback do usuário. Mantenha a estrutura JSON e melhore apenas as partes mencionadas no feedback.

**IMPORTANTE:** Retorne APENAS o JSON refinado, sem texto adicional.";

        // Chamar Gemini API
        string refinedContent;
        try
        {
            refinedContent = await _geminiClient.GenerateContentAsync(refinementPrompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Gemini API for refinement");
            return null;
        }

        // Atualizar a task
        var updatedTask = await _stageService.UpdateTaskAsync(taskId, userId, t =>
        {
            t.Content = refinedContent;
        });

        if (updatedTask == null)
        {
            return null;
        }

        // Criar registro de avaliação
        var evaluation = new IaEvaluation
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            InputText = refinementPrompt,
            OutputJson = JsonDocument.Parse(refinedContent),
            ModelUsed = "gemini-2.5-flash",
            TokensUsed = EstimateTokens(refinementPrompt + refinedContent),
            CreatedAt = DateTime.UtcNow
        };

        _context.IaEvaluations.Add(evaluation);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Document refined successfully for task {TaskId}", taskId);

        return updatedTask;
    }

    /// <summary>
    /// Estimativa simples de tokens (aproximadamente 1 token = 4 caracteres)
    /// </summary>
    private int EstimateTokens(string text)
    {
        return text.Length / 4;
    }
}
