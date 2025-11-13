using IdeorAI.Client;
using IdeorAI.Model.Entities;
using IdeorAI.Model.SupabaseModels;
using System.Text.Json;

namespace IdeorAI.Services;

/// <summary>
/// Serviço de geração de documentos via IA (Gemini)
/// Implementação com Supabase Client
/// </summary>
public class DocumentGenerationService : IDocumentGenerationService
{
    private readonly Supabase.Client _supabase;
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
        Supabase.Client supabase,
        GeminiApiClient geminiClient,
        IStageService stageService,
        ILogger<DocumentGenerationService> logger,
        IConfiguration configuration)
    {
        _supabase = supabase;
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
        _logger.LogInformation("[DocumentGeneration] Iniciando geração para project {ProjectId}, stage {Stage}, userId {UserId}",
            projectId, stage, userId);

        // Validar stage
        if (!StageTitles.ContainsKey(stage))
        {
            _logger.LogWarning("[DocumentGeneration] Stage inválido: {Stage}", stage);
            return null;
        }

        _logger.LogInformation("[DocumentGeneration] Stage válido. Título: {Title}", StageTitles[stage]);

        // Log dos inputs recebidos
        _logger.LogInformation("[DocumentGeneration] Inputs recebidos: {InputCount} campos", inputs.Count);
        foreach (var kvp in inputs)
        {
            var valuePreview = kvp.Value.Length > 100 ? kvp.Value.Substring(0, 100) + "..." : kvp.Value;
            _logger.LogInformation("[DocumentGeneration] Input [{Key}]: {Value}", kvp.Key, valuePreview);
        }

        // Gerar o prompt (3 níveis: full, resumido, mini)
        string prompt;
        string promptMode;
        try
        {
            // Prioridade: 1) Variável de ambiente, 2) appsettings.json, 3) default = "mini"
            promptMode = Environment.GetEnvironmentVariable("PromptSettings__PromptMode")
                ?? _configuration.GetValue<string>("PromptSettings:PromptMode")
                ?? "mini";

            promptMode = promptMode.ToLower();

            _logger.LogInformation("[DocumentGeneration] PromptMode configurado: {PromptMode}", promptMode);

            switch (promptMode)
            {
                case "full":
                case "completo":
                    _logger.LogInformation("[DocumentGeneration] Usando prompts COMPLETOS (full)");
                    prompt = PromptTemplates.GetPromptForStage(stage, inputs);
                    break;

                case "resumido":
                case "resumed":
                    _logger.LogInformation("[DocumentGeneration] Usando prompts RESUMIDOS (resumed)");
                    prompt = PromptResumidos.GetPromptForStage(stage, inputs);
                    break;

                case "mini":
                case "minimal":
                default:
                    _logger.LogInformation("[DocumentGeneration] Usando prompts MINI (ultra-compactos)");
                    prompt = PromptMiniResumidos.GetPromptForStage(stage, inputs);
                    break;
            }

            _logger.LogInformation("[DocumentGeneration] Prompt gerado com sucesso. Modo: {Mode}, Comprimento: {Length} caracteres",
                promptMode, prompt.Length);

            // Log comparativo de tamanhos (apenas no primeiro uso para referência)
            try
            {
                var fullSize = PromptTemplates.GetPromptForStage(stage, inputs).Length;
                var resumidoSize = PromptResumidos.GetPromptForStage(stage, inputs).Length;
                var miniSize = PromptMiniResumidos.GetPromptForStage(stage, inputs).Length;

                var percentResumido = (resumidoSize * 100.0 / fullSize);
                var percentMini = (miniSize * 100.0 / fullSize);

                _logger.LogInformation(
                    "[DocumentGeneration] 📊 Comparação de tamanhos para {Stage}: Full={FullSize} chars | Resumido={ResumidoSize} ({PercentR:F1}%) | Mini={MiniSize} ({PercentM:F1}%) | Usando={Using}",
                    stage, fullSize, resumidoSize, percentResumido, miniSize, percentMini, promptMode);
            }
            catch (Exception compEx)
            {
                _logger.LogWarning(compEx, "[DocumentGeneration] Falha ao comparar tamanhos de prompt (não crítico)");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DocumentGeneration] Erro ao gerar prompt para stage {Stage}", stage);
            return null;
        }

        // Chamar Gemini API (com rotação inteligente de modelos baseada na etapa)
        string generatedContent;
        try
        {
            _logger.LogInformation("[DocumentGeneration] Chamando Gemini API para stage {Stage}...", stage);
            var startTime = DateTime.UtcNow;

            generatedContent = await _geminiClient.GenerateContentAsync(prompt, stage);

            var elapsed = DateTime.UtcNow - startTime;
            _logger.LogInformation("[DocumentGeneration] Gemini API respondeu com sucesso. Tempo: {ElapsedMs}ms, Conteúdo: {Length} caracteres",
                elapsed.TotalMilliseconds, generatedContent.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DocumentGeneration] Erro ao chamar Gemini API para stage {Stage}. Tipo: {ExceptionType}, Mensagem: {Message}",
                stage, ex.GetType().Name, ex.Message);
            return null;
        }

        // Criar a task com o conteúdo gerado
        _logger.LogInformation("[DocumentGeneration] Criando task no banco de dados...");

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
            _logger.LogWarning("[DocumentGeneration] Falha ao criar task para project {ProjectId}", projectId);
            return null;
        }

        _logger.LogInformation("[DocumentGeneration] Task criada com sucesso. TaskId: {TaskId}", createdTask.Id);

        // Criar registro de avaliação de IA
        try
        {
            _logger.LogInformation("[DocumentGeneration] Salvando registro de avaliação IA...");

            var evaluationModel = new IaEvaluationModel
            {
                Id = Guid.NewGuid().ToString(),
                TaskId = createdTask.Id.ToString(),
                InputText = prompt,
                OutputJson = TryParseJson(generatedContent).RootElement,
                ModelUsed = "gemini-rotação-inteligente",
                TokensUsed = EstimateTokens(prompt + generatedContent),
                CreatedAt = DateTime.UtcNow
            };

            await _supabase
                .From<IaEvaluationModel>()
                .Insert(evaluationModel);

            _logger.LogInformation("[DocumentGeneration] Registro de avaliação salvo com sucesso");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DocumentGeneration] Falha ao salvar avaliação para task {TaskId}, mas documento foi gerado com sucesso", createdTask.Id);
        }

        _logger.LogInformation("[DocumentGeneration] ✅ Documento gerado com sucesso! TaskId: {TaskId}, Stage: {Stage}", createdTask.Id, stage);

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
        try
        {
            var evaluationModel = new IaEvaluationModel
            {
                Id = Guid.NewGuid().ToString(),
                TaskId = taskId.ToString(),
                InputText = prompt,
                OutputJson = TryParseJson(generatedContent).RootElement,
                ModelUsed = "gemini-rotação-inteligente",
                TokensUsed = EstimateTokens(prompt + generatedContent),
                CreatedAt = DateTime.UtcNow
            };

            await _supabase
                .From<IaEvaluationModel>()
                .Insert(evaluationModel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save evaluation for task {TaskId}, but document was regenerated successfully", taskId);
        }

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
        try
        {
            var evaluationModel = new IaEvaluationModel
            {
                Id = Guid.NewGuid().ToString(),
                TaskId = taskId.ToString(),
                InputText = refinementPrompt,
                OutputJson = TryParseJson(refinedContent).RootElement,
                ModelUsed = "gemini-rotação-inteligente",
                TokensUsed = EstimateTokens(refinementPrompt + refinedContent),
                CreatedAt = DateTime.UtcNow
            };

            await _supabase
                .From<IaEvaluationModel>()
                .Insert(evaluationModel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save evaluation for task {TaskId}, but document was refined successfully", taskId);
        }

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

    /// <summary>
    /// Tenta fazer parse de JSON, retornando um objeto vazio em caso de falha
    /// </summary>
    private JsonDocument TryParseJson(string content)
    {
        try
        {
            return JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse content as JSON, wrapping in object");
            // Criar um JSON válido com o conteúdo como string
            var wrappedJson = JsonSerializer.Serialize(new { content = content });
            return JsonDocument.Parse(wrappedJson);
        }
    }
}
