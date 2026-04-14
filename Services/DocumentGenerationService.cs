using IdeorAI.Client;
using IdeorAI.Model.Entities;
using IdeorAI.Model.SupabaseModels;
using System.Text.Json;

namespace IdeorAI.Services;

/// <summary>
/// Serviço de geração de documentos via IA (Gemini ou OpenRouter)
/// Implementação com Supabase Client
/// </summary>
public class DocumentGenerationService : IDocumentGenerationService
{
    private readonly Supabase.Client _supabase;
    private readonly GeminiApiClient? _geminiClient;
    private readonly OpenRouterClient? _openRouterClient;
    private readonly IStageService _stageService;
    private readonly IProjectService _projectService;
    private readonly IStageSummaryService _stageSummaryService;
    private readonly ILogger<DocumentGenerationService> _logger;
    private readonly IConfiguration _configuration;

    public DocumentGenerationService(
        Supabase.Client supabase,
        IStageService stageService,
        IProjectService projectService,
        IStageSummaryService stageSummaryService,
        ILogger<DocumentGenerationService> logger,
        IConfiguration configuration,
        GeminiApiClient? geminiClient = null,
        OpenRouterClient? openRouterClient = null)
    {
        _supabase = supabase;
        _geminiClient = geminiClient;
        _openRouterClient = openRouterClient;
        _stageService = stageService;
        _projectService = projectService;
        _stageSummaryService = stageSummaryService;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Chama a API de IA configurada (OpenRouter ou Gemini)
    /// </summary>
    private async Task<string> CallAiApiAsync(string prompt, string stage = "")
    {
        // Prioridade: OpenRouter > Gemini
        if (_openRouterClient != null)
        {
            _logger.LogInformation("[DocumentGeneration] Using OpenRouter for stage {Stage}", stage);
            return await _openRouterClient.GenerateContentAsync(prompt);
        }
        
        if (_geminiClient != null)
        {
            _logger.LogInformation("[DocumentGeneration] Using Gemini for stage {Stage}", stage);
            return await _geminiClient.GenerateContentAsync(prompt, stage);
        }
        
        throw new InvalidOperationException("Nenhum cliente de IA configurado");
    }

    private const int MaxContextLength = 3500;
    private const int MaxInputLength = 500;

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
        IProjectService projectService,
        IStageSummaryService stageSummaryService,
        ILogger<DocumentGenerationService> logger,
        IConfiguration configuration)
    {
        _supabase = supabase;
        _geminiClient = geminiClient;
        _stageService = stageService;
        _projectService = projectService;
        _stageSummaryService = stageSummaryService;
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

        // Validar que etapas anteriores foram completadas (bloqueio sequencial)
        // etapa1 pode ser gerada sem dependências
        if (stage.ToLower() != "etapa1")
        {
            var stageOrder = new[] { "etapa1", "etapa2", "etapa3", "etapa4", "etapa5" };
            var currentStageIndex = Array.IndexOf(stageOrder, stage.ToLower());
            
            if (currentStageIndex > 0)
            {
                var previousStage = stageOrder[currentStageIndex - 1];
                var previousSummaries = await _stageSummaryService.GetByProjectAsync(projectId);
                var previousCompleted = previousSummaries.Any(s => s.Stage?.ToLower() == previousStage);
                
                if (!previousCompleted)
                {
                    _logger.LogWarning("[DocumentGeneration] Etapa anterior {PreviousStage} não completada. Bloqueando {Stage}", previousStage, stage);
                    return null;
                }
            }
        }

        // Log dos inputs recebidos (apenas em debug para não poluir produção)
        _logger.LogDebug("[DocumentGeneration] Inputs recebidos: {InputCount} campos", inputs.Count);
        foreach (var kvp in inputs)
        {
            var valuePreview = kvp.Value.Length > 100 ? kvp.Value.Substring(0, 100) + "..." : kvp.Value;
            _logger.LogDebug("[DocumentGeneration] Input [{Key}]: {Value}", kvp.Key, valuePreview);
        }

        // Invalidar etapas posteriores se estivermos regenerando uma etapa existente
        try
        {
            // Verificar se já existe um resumo para esta etapa
            var existingSummaries = await _stageSummaryService.GetByProjectAsync(projectId);
            var exists = existingSummaries.Any(s => s.Stage?.ToLower() == stage.ToLower());
            
            if (exists)
            {
                _logger.LogInformation("[DocumentGeneration] Etapa {Stage} já existe. Invalidando etapas posteriores...", stage);
                await _stageSummaryService.DeleteSubsequentStagesAsync(projectId, stage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DocumentGeneration] Falha ao invalidar etapas posteriores (continuando)");
        }

        // Enriquecer inputs com dados do projeto (Region e Constraints)
        try
        {
            var project = await _projectService.GetByIdAsync(projectId, userId);
            if (project != null)
            {
                if (!string.IsNullOrEmpty(project.Region))
                    inputs["regiao"] = SanitizeInput(project.Region);
                
                if (!string.IsNullOrEmpty(project.Constraints))
                    inputs["restricoes"] = SanitizeInput(project.Constraints);

                // Se não tiver ideia nos inputs, tenta pegar da descrição
                if (!inputs.ContainsKey("ideia") || string.IsNullOrEmpty(inputs["ideia"]))
                {
                    inputs["ideia"] = SanitizeInput(project.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DocumentGeneration] Falha ao enriquecer inputs com dados do projeto");
        }

        // Buscar contexto acumulado das etapas anteriores (se não for etapa 1)
        string contextPrompt = "";
        string fullContext = "";
        if (stage.ToLower() != "etapa1")
        {
            try
            {
                _logger.LogInformation("[DocumentGeneration] Buscando contexto acumulado para {Stage}...", stage);
                var previousSummaries = await _stageSummaryService.GetPreviousStagesAsync(projectId, stage);
                
                if (previousSummaries.Any())
                {
                    var contextParts = previousSummaries.Select(s => $"[{s.Stage}] {s.SummaryText}");
                    fullContext = string.Join("\n", contextParts);
                    
                    // Limitar contexto a 3500 caracteres
                    if (fullContext.Length > MaxContextLength)
                    {
                        fullContext = fullContext.Substring(0, MaxContextLength - 3) + "...";
                    }
                    
                    contextPrompt = $"\n\n## CONTEXTO DAS ETAPAS ANTERIORES:\n{fullContext}\n\nUse esse contexto para manter consistência com o que já foi definido.\n";
                    
                    // Adicionar contexto acumulado aos inputs para uso nos templates
                    inputs["contexto_acumulado"] = fullContext;
                    
                    _logger.LogInformation("[DocumentGeneration] Contexto acumulado adicionado: {Length} caracteres de {Count} etapas anteriores",
                        contextPrompt.Length, previousSummaries.Count);
                }
                else
                {
                    _logger.LogInformation("[DocumentGeneration] Nenhum contexto anterior encontrado para {Stage}", stage);
                }
            }
            catch (Exception ctxEx)
            {
                _logger.LogWarning(ctxEx, "[DocumentGeneration] Falha ao buscar contexto acumulado (continuando sem contexto)");
            }
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

            // Adicionar contexto acumulado ao prompt
            prompt += contextPrompt;

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

            generatedContent = await CallAiApiAsync(prompt, stage);

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
                OutputJson = ExtractJsonString(generatedContent),
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

        // Sanitizar JSON e salvar resumo da etapa
        await SaveStageSummaryAsync(projectId, userId, stage, generatedContent);

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

        var projectId = task.ProjectId;
        var stage = task.Phase;

        // Buscar contexto acumulado das etapas anteriores (se não for etapa 1)
        string contextPrompt = "";
        if (stage.ToLower() != "etapa1")
        {
            try
            {
                _logger.LogInformation("[DocumentGeneration] Buscando contexto acumulado para regeneração de {Stage}...", stage);
                var previousSummaries = await _stageSummaryService.GetPreviousStagesAsync(projectId, stage);
                
                if (previousSummaries.Any())
                {
                    var contextParts = previousSummaries.Select(s => $"[{s.Stage}] {s.SummaryText}");
                    var fullContext = string.Join("\n", contextParts);
                    
                    // Limitar contexto a 3500 caracteres
                    if (fullContext.Length > MaxContextLength)
                    {
                        fullContext = fullContext.Substring(0, MaxContextLength - 3) + "...";
                    }
                    
                    contextPrompt = $"\n\n## CONTEXTO DAS ETAPAS ANTERIORES:\n{fullContext}\n\nUse esse contexto para manter consistência com o que já foi definido.\n";
                    
                    _logger.LogInformation("[DocumentGeneration] Contexto acumulado adicionado para regeneração: {Length} caracteres",
                        contextPrompt.Length);
                }
            }
            catch (Exception ctxEx)
            {
                _logger.LogWarning(ctxEx, "[DocumentGeneration] Falha ao buscar contexto acumulado (continuando sem contexto)");
            }
        }

        // Gerar novo prompt com novos inputs (usando versão simplificada em dev, completa em produção)
        string prompt;
        try
        {
            var useSimplified = _configuration.GetValue<bool>("PromptSettings:UseSimplifiedPrompts", false);

            if (useSimplified)
            {
                prompt = PromptResumidos.GetPromptForStage(stage, newInputs);
            }
            else
            {
                prompt = PromptTemplates.GetPromptForStage(stage, newInputs);
            }

            // Adicionar contexto acumulado ao prompt
            prompt += contextPrompt;
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
            generatedContent = await CallAiApiAsync(prompt);
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
                OutputJson = ExtractJsonString(generatedContent),
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

        // Sanitizar JSON e salvar resumo da etapa
        await SaveStageSummaryAsync(projectId, userId, stage, generatedContent);

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
            refinedContent = await CallAiApiAsync(refinementPrompt);
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
                OutputJson = ExtractJsonString(refinedContent),
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
    /// Sanitiza JSON, gera summary_text e salva na tabela project_stage_summaries
    /// </summary>
    private async Task SaveStageSummaryAsync(Guid projectId, Guid userId, string stage, string generatedContent)
    {
        try
        {
            _logger.LogInformation("[DocumentGeneration] Sanitizando JSON e salvando resumo para {Stage}...", stage);

            // Extrair e validar JSON
            var extractedJson = JsonSanitizer.ExtractJson(generatedContent);
            
            if (!JsonSanitizer.TryValidateSchema(extractedJson, stage, out var jsonDoc, out var errorMessage))
            {
                _logger.LogWarning("[DocumentGeneration] JSON inválido para {Stage}: {Error}", stage, errorMessage);
                // Não falha a operação, apenas loga o erro
                return;
            }

            // Gerar summary_text
            var summaryText = SummaryTextGenerator.Generate(stage, jsonDoc!.RootElement);
            
            _logger.LogInformation("[DocumentGeneration] Summary_text gerado: {Length} caracteres", summaryText.Length);

            // Fazer UPSERT no banco
            await _stageSummaryService.UpsertAsync(projectId, userId, stage, jsonDoc.RootElement, summaryText);
            
            _logger.LogInformation("[DocumentGeneration] Resumo da etapa salvo com sucesso");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DocumentGeneration] Erro ao salvar resumo da etapa {Stage} (não crítico)", stage);
            // Não falha a operação principal se o resumo falhar
        }
    }

    /// <summary>
    /// Estimativa simples de tokens (aproximadamente 1 token = 4 caracteres)
    /// </summary>
    private int EstimateTokens(string text)
    {
        return text.Length / 4;
    }

    /// <summary>
    /// Extrai o JSON do conteúdo gerado como string para armazenamento.
    /// Evita usar JsonElement diretamente (incompatível com Newtonsoft.Json).
    /// </summary>
    private string ExtractJsonString(string content)
    {
        try
        {
            // Valida se é JSON válido — se for, retorna a string normalizada
            using var doc = JsonDocument.Parse(content);
            return content.Trim();
        }
        catch
        {
            // Conteúdo não é JSON — embute como campo "content" num objeto
            return JsonSerializer.Serialize(new { content });
        }
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

    /// <summary>
    /// Sanitiza input do usuário para prevenir prompt injection
    /// Remove caracteres de controle, sequências suspeitas e limita comprimento
    /// </summary>
    private string SanitizeInput(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;
        
        // Limitar comprimento
        if (input.Length > MaxInputLength)
            input = input.Substring(0, MaxInputLength);
        
        // Remover sequências suspeitas que podem indicar prompt injection
        input = input.Replace("```", "")
                     .Replace("System:", "")
                     .Replace("Instruction:", "")
                     .Replace("Ignore previous", "")
                     .Replace("ignore previous", "")
                     .Replace("System instruction", "")
                     .Replace("Override", "")
                     .Replace("override", "");
        
        return input.Trim();
    }
}
