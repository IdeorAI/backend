using IdeorAI.Model.Entities;
using IdeorAI.Model.SupabaseModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IdeorAI.Services;

/// <summary>
/// Serviço de exportação de documentos para PDF
/// Implementação com Supabase Client
/// </summary>
public class PdfExportService : IPdfExportService
{
    private readonly Supabase.Client _supabase;
    private readonly ILogger<PdfExportService> _logger;

    public PdfExportService(
        Supabase.Client supabase,
        ILogger<PdfExportService> logger)
    {
        _supabase = supabase;
        _logger = logger;

        // Configurar licença Community do QuestPDF (gratuita para uso não-comercial)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<byte[]?> ExportProjectDocumentsAsync(Guid projectId, Guid userId)
    {
        _logger.LogInformation("Exporting documents for project {ProjectId}", projectId);

        try
        {
            // Buscar o projeto
            var projectResponse = await _supabase
                .From<ProjectModel>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
                .Filter("owner_id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
                .Single();

            if (projectResponse == null)
            {
                _logger.LogWarning("Project {ProjectId} not found for user {UserId}", projectId, userId);
                return null;
            }

            // Buscar todas as tasks do projeto (documentos gerados)
            var tasksResponse = await _supabase
                .From<TaskModel>()
                .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
                .Order("phase", Supabase.Postgrest.Constants.Ordering.Ascending)
                .Get();

            var tasks = tasksResponse.Models
                .Where(t => !string.IsNullOrEmpty(t.Content))
                .Select(MapTaskToEntity)
                .ToList();

            if (!tasks.Any())
            {
                _logger.LogWarning("No documents found for project {ProjectId}", projectId);
                return null;
            }

            // Gerar PDF
            var pdfBytes = GeneratePdf(projectResponse.Name, tasks);
            _logger.LogInformation("PDF generated successfully for project {ProjectId}", projectId);
            return pdfBytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PDF for project {ProjectId}", projectId);
            return null;
        }
    }

    public async Task<byte[]?> ExportSinglePhaseDocumentAsync(Guid projectId, Guid userId, string phase)
    {
        _logger.LogInformation("Exporting single document for project {ProjectId}, phase {Phase}", projectId, phase);

        try
        {
            // Buscar o projeto
            var projectResponse = await _supabase
                .From<ProjectModel>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
                .Filter("owner_id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
                .Single();

            if (projectResponse == null)
            {
                _logger.LogWarning("Project {ProjectId} not found for user {UserId}", projectId, userId);
                return null;
            }

            // Buscar a task específica da fase
            var taskResponse = await _supabase
                .From<TaskModel>()
                .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId.ToString())
                .Filter("phase", Supabase.Postgrest.Constants.Operator.Equals, phase)
                .Single();

            if (taskResponse == null || string.IsNullOrEmpty(taskResponse.Content))
            {
                _logger.LogWarning("Task for phase {Phase} not found in project {ProjectId}", phase, projectId);
                return null;
            }

            // Gerar PDF com apenas essa task
            var tasks = new List<ProjectTask> { MapTaskToEntity(taskResponse) };
            var pdfBytes = GeneratePdf(projectResponse.Name, tasks);
            _logger.LogInformation("PDF generated successfully for project {ProjectId}, phase {Phase}", projectId, phase);
            return pdfBytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PDF for project {ProjectId}, phase {Phase}", projectId, phase);
            return null;
        }
    }

    // Helper para converter TaskModel (Supabase) para ProjectTask (Entity)
    private ProjectTask MapTaskToEntity(TaskModel model)
    {
        return new ProjectTask
        {
            Id = Guid.Parse(model.Id),
            ProjectId = Guid.Parse(model.ProjectId),
            Title = model.Title,
            Description = model.Description,
            Phase = model.Phase,
            Content = model.Content,
            Status = model.Status,
            EvaluationResult = model.EvaluationResult != null
                ? JsonDocument.Parse(model.EvaluationResult.ToString())
                : null,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    private byte[] GeneratePdf(string projectName, List<ProjectTask> tasks)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header()
                    .Text($"Relatório Completo - {projectName}")
                    .SemiBold()
                    .FontSize(20)
                    .FontColor(Colors.Blue.Darken2);

                page.Content()
                    .PaddingVertical(1, Unit.Centimetre)
                    .Column(column =>
                    {
                        column.Spacing(15);

                        // Informações gerais
                        column.Item().Text(text =>
                        {
                            text.Span("Projeto: ").Bold();
                            text.Span(projectName);
                        });

                        column.Item().Text(text =>
                        {
                            text.Span("Gerado em: ").Bold();
                            text.Span(DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
                        });

                        column.Item().LineHorizontal(1);

                        // Índice
                        column.Item().PaddingTop(10).Text("Índice").Bold().FontSize(16);
                        foreach (var task in tasks)
                        {
                            column.Item().Text($"• {task.Title}").FontSize(10);
                        }

                        column.Item().PageBreak();

                        // Documentos
                        foreach (var task in tasks)
                        {
                            // Título da etapa
                            column.Item().Text(task.Title)
                                .Bold()
                                .FontSize(16)
                                .FontColor(Colors.Blue.Medium);

                            // Descrição
                            if (!string.IsNullOrEmpty(task.Description))
                            {
                                column.Item().Text(task.Description)
                                    .Italic()
                                    .FontSize(10)
                                    .FontColor(Colors.Grey.Darken1);
                            }

                            // Conteúdo JSON formatado
                            column.Item().PaddingTop(10).Element(container =>
                            {
                                RenderJsonContent(container, task.Content);
                            });

                            // Separador
                            column.Item().PaddingVertical(10).LineHorizontal(1);
                        }
                    });

                page.Footer()
                    .AlignCenter()
                    .Text(x =>
                    {
                        x.Span("Página ");
                        x.CurrentPageNumber();
                        x.Span(" de ");
                        x.TotalPages();
                    });
            });
        });

        return document.GeneratePdf();
    }

    private void RenderJsonContent(IContainer container, string jsonContent)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(jsonContent);
            container.Background(Colors.Grey.Lighten4)
                .Padding(10)
                .Column(column =>
                {
                    column.Spacing(5);
                    RenderJsonElement(column, jsonDoc.RootElement, 0);
                });
        }
        catch (Exception)
        {
            // Se não for JSON válido, renderizar como texto simples
            container.Background(Colors.Grey.Lighten4)
                .Padding(10)
                .Text(jsonContent)
                .FontSize(9);
        }
    }

    private void RenderJsonElement(ColumnDescriptor column, JsonElement element, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 2);

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    column.Item().Text(text =>
                    {
                        text.Span(indent).FontSize(9);
                        text.Span($"{property.Name}: ").Bold().FontSize(9);
                    });

                    if (property.Value.ValueKind == JsonValueKind.Object ||
                        property.Value.ValueKind == JsonValueKind.Array)
                    {
                        RenderJsonElement(column, property.Value, indentLevel + 1);
                    }
                    else
                    {
                        column.Item().Text($"{indent}  {GetJsonValue(property.Value)}")
                            .FontSize(9);
                    }
                }
                break;

            case JsonValueKind.Array:
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    column.Item().Text($"{indent}[{index}]").Bold().FontSize(9);
                    RenderJsonElement(column, item, indentLevel + 1);
                    index++;
                }
                break;

            default:
                column.Item().Text($"{indent}{GetJsonValue(element)}").FontSize(9);
                break;
        }
    }

    private string GetJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => element.GetRawText()
        };
    }

    // ============================================================
    // Spec 018 — PDF Relatório por Etapa (markdown-aware)
    // ============================================================

    private const string IdeorPurple = "#8c7dff";

    public async Task<byte[]> GenerateStagePdfAsync(string projectId, string taskId, string userId, CancellationToken ct)
    {
        _logger.LogInformation("[StagePdf] Gerando PDF para project {ProjectId} task {TaskId}", projectId, taskId);

        // 1) Buscar projeto e validar owner
        var project = await _supabase
            .From<ProjectModel>()
            .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
            .Single();

        if (project == null)
            throw new KeyNotFoundException($"Project {projectId} not found");

        if (!string.Equals(project.OwnerId, userId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("User is not owner of this project");

        // 2) Buscar task e validar projectId
        var task = await _supabase
            .From<TaskModel>()
            .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, taskId)
            .Single();

        if (task == null)
            throw new KeyNotFoundException($"Task {taskId} not found");

        if (!string.Equals(task.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
            throw new KeyNotFoundException("Task does not belong to project");

        // 3) Parsear conteúdo nos 3 shapes
        var sections = ParseStageContent(task.Content ?? string.Empty);

        // 4) Construir PDF
        var stageName = !string.IsNullOrWhiteSpace(task.Title) ? task.Title : task.Phase ?? "Etapa";
        var projectName = project.Name ?? "Projeto";

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2.5f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(11).FontColor(Colors.Grey.Darken4));

                page.Header().Column(col =>
                {
                    col.Spacing(2);
                    col.Item().Text("IdeorAI").FontSize(14).Bold().FontColor(IdeorPurple);
                    col.Item().Text(projectName).FontSize(18).Bold().FontColor(Colors.Grey.Darken4);
                    col.Item().Text(stageName).FontSize(13).Medium().FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingVertical(0.6f, Unit.Centimetre).Column(column =>
                {
                    column.Spacing(10);

                    int idx = 1;
                    foreach (var section in sections)
                    {
                        if (!string.IsNullOrWhiteSpace(section.Title))
                        {
                            var title = section.Numbered ? $"{idx}. {section.Title}" : section.Title;
                            column.Item().PaddingTop(8).Text(title)
                                .FontSize(14).Bold().FontColor(IdeorPurple);
                            if (section.Numbered) idx++;
                        }

                        RenderMarkdownBlocks(column, section.Markdown ?? string.Empty);
                    }
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text(text =>
                    {
                        text.DefaultTextStyle(s => s.FontSize(9).FontColor(Colors.Grey.Medium));
                        text.Span("Página ");
                        text.CurrentPageNumber();
                        text.Span(" de ");
                        text.TotalPages();
                    });
                    row.RelativeItem().AlignRight().Text("IdeorAI - ideoria.ai")
                        .FontSize(9).FontColor(Colors.Grey.Medium);
                });
            });
        });

        return document.GeneratePdf();
    }

    private record StageSection(string Title, string Markdown, bool Numbered);

    private List<StageSection> ParseStageContent(string content)
    {
        var sections = new List<StageSection>();
        var trimmed = (content ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            sections.Add(new StageSection("", "", false));
            return sections;
        }

        // Tenta extrair JSON de fenced code block ```json ... ``` ou raw
        string? jsonCandidate = ExtractJsonCandidate(trimmed);

        if (jsonCandidate != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonCandidate);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    // Shape "wrapped": { "content": "markdown..." }
                    if (root.TryGetProperty("content", out var contentProp) &&
                        contentProp.ValueKind == JsonValueKind.String &&
                        root.EnumerateObject().Count() == 1)
                    {
                        sections.Add(new StageSection("", contentProp.GetString() ?? "", false));
                        return sections;
                    }

                    // Shape JSON estruturado: cada chave top-level vira seção
                    foreach (var prop in root.EnumerateObject())
                    {
                        var title = HumanizeKey(prop.Name);
                        var md = JsonValueToMarkdown(prop.Value);
                        sections.Add(new StageSection(title, md, true));
                    }
                    return sections;
                }
            }
            catch
            {
                // Cai no markdown puro
            }
        }

        // Markdown puro
        sections.Add(new StageSection("", trimmed, false));
        return sections;
    }

    private string? ExtractJsonCandidate(string input)
    {
        var fenced = Regex.Match(input, @"```(?:json)?\s*([\s\S]*?)```", RegexOptions.IgnoreCase);
        if (fenced.Success)
        {
            var inner = fenced.Groups[1].Value.Trim();
            if (inner.StartsWith("{") || inner.StartsWith("[")) return inner;
        }
        if (input.StartsWith("{") || input.StartsWith("["))
            return input;
        return null;
    }

    private string JsonValueToMarkdown(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString() ?? "";
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetRawText();
            case JsonValueKind.Null:
                return "";
            case JsonValueKind.Array:
                var sbA = new System.Text.StringBuilder();
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        sbA.AppendLine("- " + FlattenObject(item));
                    }
                    else
                    {
                        sbA.AppendLine("- " + JsonValueToMarkdown(item));
                    }
                }
                return sbA.ToString();
            case JsonValueKind.Object:
                var sbO = new System.Text.StringBuilder();
                foreach (var p in element.EnumerateObject())
                {
                    sbO.AppendLine($"**{HumanizeKey(p.Name)}:** {JsonValueToMarkdown(p.Value)}");
                }
                return sbO.ToString();
            default:
                return element.GetRawText();
        }
    }

    private string FlattenObject(JsonElement obj)
    {
        var parts = new List<string>();
        foreach (var p in obj.EnumerateObject())
        {
            var val = p.Value.ValueKind == JsonValueKind.String
                ? p.Value.GetString() ?? ""
                : p.Value.GetRawText();
            parts.Add($"**{HumanizeKey(p.Name)}:** {val}");
        }
        return string.Join(" — ", parts);
    }

    private string HumanizeKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        var parts = key.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var ti = CultureInfo.GetCultureInfo("pt-BR").TextInfo;
        return string.Join(' ', parts.Select(p => ti.ToTitleCase(p.ToLowerInvariant())));
    }

    private void RenderMarkdownBlocks(ColumnDescriptor column, string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var bulletBuffer = new List<string>();

        void FlushBullets()
        {
            if (bulletBuffer.Count == 0) return;
            var items = bulletBuffer.ToList();
            bulletBuffer.Clear();
            column.Item().Column(c =>
            {
                c.Spacing(3);
                foreach (var b in items)
                {
                    c.Item().Row(r =>
                    {
                        r.ConstantItem(12).Text("•").FontSize(11).FontColor(IdeorPurple);
                        r.RelativeItem().Text(t => RenderInlineMarkdown(t, b, 11));
                    });
                }
            });
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushBullets();
                column.Item().Height(4);
                continue;
            }

            if (line.StartsWith("### "))
            {
                FlushBullets();
                column.Item().PaddingTop(4).Text(line.Substring(4).Trim())
                    .FontSize(12).Bold().FontColor(Colors.Grey.Darken3);
                continue;
            }
            if (line.StartsWith("## "))
            {
                FlushBullets();
                column.Item().PaddingTop(4).Text(line.Substring(3).Trim())
                    .FontSize(13).Bold().FontColor(Colors.Grey.Darken3);
                continue;
            }
            if (line.StartsWith("# "))
            {
                FlushBullets();
                column.Item().PaddingTop(4).Text(line.Substring(2).Trim())
                    .FontSize(14).Bold().FontColor(Colors.Grey.Darken3);
                continue;
            }

            var bulletMatch = Regex.Match(line, @"^\s*[-*]\s+(.*)$");
            if (bulletMatch.Success)
            {
                bulletBuffer.Add(bulletMatch.Groups[1].Value);
                continue;
            }

            FlushBullets();
            var paragraph = line.TrimStart();
            column.Item().Text(t => RenderInlineMarkdown(t, paragraph, 11));
        }

        FlushBullets();
    }

    // ============================================================
    // Spec 019 — PDF dos documentos finais
    // ============================================================
    public async Task<byte[]> GenerateFinalDocumentPdfAsync(string projectId, string docType, string userId, CancellationToken ct)
    {
        _logger.LogInformation("[FinalDocPdf] project {ProjectId} type {DocType}", projectId, docType);

        var project = await _supabase
            .From<ProjectModel>()
            .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
            .Single();

        if (project == null)
            throw new KeyNotFoundException($"Project {projectId} not found");

        if (!string.Equals(project.OwnerId, userId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("User is not owner of this project");

        var docResp = await _supabase
            .From<GeneratedDocumentModel>()
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
            .Filter("doc_type", Supabase.Postgrest.Constants.Operator.Equals, docType)
            .Get();

        var doc = docResp.Models?.FirstOrDefault();
        if (doc == null)
            throw new KeyNotFoundException($"Documento '{docType}' não gerado para project {projectId}");

        var title = docType switch
        {
            "pitch-deck" => "Pitch Deck",
            "business-plan" => "Plano de Negócios",
            "executive-summary" => "Resumo Executivo",
            _ => "Documento"
        };

        var projectName = project.Name ?? "Projeto";
        var markdown = doc.ContentMd ?? string.Empty;

        // Carrega a DRE da task resumo_financeiro (só relevante para business-plan).
        JsonElement? dreForPdf = null;
        if (docType == "business-plan")
        {
            try
            {
                var finResp = await _supabase
                    .From<TaskModel>()
                    .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
                    .Filter("phase", Supabase.Postgrest.Constants.Operator.Equals, "resumo_financeiro")
                    .Order("created_at", Supabase.Postgrest.Constants.Ordering.Descending)
                    .Limit(1)
                    .Get();
                dreForPdf = DreCalculator.TryExtractDre(finResp.Models?.FirstOrDefault()?.Content);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FinalDocPdf] Falha ao carregar DRE para o PDF do business-plan project {ProjectId}", projectId);
            }
        }

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2.5f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(11).FontColor(Colors.Grey.Darken4));

                page.Header().Column(col =>
                {
                    col.Spacing(2);
                    col.Item().Text("IdeorAI").FontSize(14).Bold().FontColor(IdeorPurple);
                    col.Item().Text(title).FontSize(20).Bold().FontColor(Colors.Grey.Darken4);
                    col.Item().Text(projectName).FontSize(12).Medium().FontColor(Colors.Grey.Darken1);
                });

                // Spec 022 v2: no Plano de Negócios, anexar a tabela DRE (Resumo Financeiro),
                // se o projeto já o gerou. Degradação graciosa: sem DRE, nada é anexado.
                var dreElement = docType == "business-plan" ? dreForPdf : null;

                page.Content().PaddingVertical(0.6f, Unit.Centimetre).Column(column =>
                {
                    column.Spacing(10);
                    RenderMarkdownBlocks(column, markdown);

                    if (dreElement != null)
                    {
                        column.Item().PaddingTop(12).Text("Demonstração de Resultado (DRE) — Projeção 12 meses")
                            .FontSize(13).Bold().FontColor(IdeorPurple);
                        column.Item().PaddingTop(4).Element(c => RenderDreTable(c, dreElement.Value));
                    }
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text(text =>
                    {
                        text.DefaultTextStyle(s => s.FontSize(9).FontColor(Colors.Grey.Medium));
                        text.Span("Página ");
                        text.CurrentPageNumber();
                        text.Span(" de ");
                        text.TotalPages();
                    });
                    row.RelativeItem().AlignRight().Text("IdeorAI - ideoria.ai")
                        .FontSize(9).FontColor(Colors.Grey.Medium);
                });
            });
        });

        return document.GeneratePdf();
    }

    /// <summary>
    /// Spec 022 — PDF SÓ do Resumo Financeiro (tabela DRE atualizada).
    /// Lê a task resumo_financeiro e renderiza a DRE via RenderDreTable.
    /// </summary>
    public async Task<byte[]> GenerateFinancialSummaryPdfAsync(string projectId, string userId, CancellationToken ct)
    {
        _logger.LogInformation("[FinSummaryPdf] project {ProjectId}", projectId);

        // .Single() lança exceção bruta (→ 500) se 0 linhas. Usar Get()+FirstOrDefault()
        // e lançar KeyNotFoundException explícito (→ 404), como no resto do método.
        var projectResp = await _supabase
            .From<ProjectModel>()
            .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
            .Limit(1)
            .Get();
        var project = projectResp.Models?.FirstOrDefault();

        if (project == null)
            throw new KeyNotFoundException($"Project {projectId} not found");
        if (!string.Equals(project.OwnerId, userId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("User is not owner of this project");

        // Carrega a DRE da task resumo_financeiro (mais recente).
        var finResp = await _supabase
            .From<TaskModel>()
            .Filter("project_id", Supabase.Postgrest.Constants.Operator.Equals, projectId)
            .Filter("phase", Supabase.Postgrest.Constants.Operator.Equals, "resumo_financeiro")
            .Order("created_at", Supabase.Postgrest.Constants.Ordering.Descending)
            .Limit(1)
            .Get();

        var dre = DreCalculator.TryExtractDre(finResp.Models?.FirstOrDefault()?.Content);
        if (dre == null)
            throw new KeyNotFoundException($"Resumo Financeiro não gerado para project {projectId}");

        var projectName = project.Name ?? "Projeto";
        var dreValue = dre.Value;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.5f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(11).FontColor(Colors.Grey.Darken4));

                page.Header().Column(col =>
                {
                    col.Spacing(2);
                    col.Item().Text("IdeorAI").FontSize(14).Bold().FontColor(IdeorPurple);
                    col.Item().Text("Resumo Financeiro").FontSize(20).Bold().FontColor(Colors.Grey.Darken4);
                    col.Item().Text(projectName).FontSize(12).Medium().FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingVertical(0.6f, Unit.Centimetre).Column(column =>
                {
                    column.Spacing(10);
                    column.Item().Text("Demonstração de Resultado (DRE) — Projeção 12 meses")
                        .FontSize(13).Bold().FontColor(IdeorPurple);
                    column.Item().PaddingTop(4).Element(c => RenderDreTable(c, dreValue));
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text(text =>
                    {
                        text.DefaultTextStyle(s => s.FontSize(9).FontColor(Colors.Grey.Medium));
                        text.Span("Página ");
                        text.CurrentPageNumber();
                        text.Span(" de ");
                        text.TotalPages();
                    });
                    row.RelativeItem().AlignRight().Text("IdeorAI - ideoria.ai")
                        .FontSize(9).FontColor(Colors.Grey.Medium);
                });
            });
        });

        return document.GeneratePdf();
    }

    private void RenderInlineMarkdown(TextDescriptor text, string content, float fontSize)
    {
        // Bold com **texto** — demais inline como texto normal
        var regex = new Regex(@"\*\*(.+?)\*\*");
        int lastIdx = 0;
        foreach (Match m in regex.Matches(content))
        {
            if (m.Index > lastIdx)
            {
                text.Span(content.Substring(lastIdx, m.Index - lastIdx)).FontSize(fontSize);
            }
            text.Span(m.Groups[1].Value).FontSize(fontSize).Bold();
            lastIdx = m.Index + m.Length;
        }
        if (lastIdx < content.Length)
        {
            text.Span(content.Substring(lastIdx)).FontSize(fontSize);
        }
    }

    /// <summary>
    /// Renderiza a tabela DRE (descrição + 12 meses) no PDF via QuestPDF.
    /// Fonte compacta para caber em A4 retrato; linhas de total em negrito/realçadas.
    /// Valores em milhares de R$ (ex.: "12,5k") para reduzir largura.
    /// </summary>
    private void RenderDreTable(IContainer container, JsonElement dre)
    {
        var linhas = DreCalculator.BuildLinhasView(dre);
        if (linhas == null || linhas.Count == 0)
        {
            container.Text("Projeção financeira indisponível.").FontSize(9).Italic().FontColor(Colors.Grey.Medium);
            return;
        }

        const float fs = 6.5f;

        // O `container` é single-child (vem de .Element(...)). Atribuir Table E Text
        // direto nele lança DocumentComposeException ("multiple child elements to a
        // single-child container"). Envolvemos os dois num Column, que aceita N filhos.
        container.Column(col =>
        {
            col.Spacing(4);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3.2f); // descrição
                    for (var m = 0; m < 12; m++) cols.RelativeColumn(1f);
                });

                // Cabeçalho.
                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).AlignLeft().Text("Conta").FontSize(fs).Bold().FontColor(Colors.White);
                    for (var m = 1; m <= 12; m++)
                        header.Cell().Element(HeaderCell).AlignRight().Text($"M{m}").FontSize(fs).Bold().FontColor(Colors.White);
                });

                foreach (var l in linhas)
                {
                    var isTotal = string.Equals(l.Tipo, "calculado", StringComparison.OrdinalIgnoreCase);
                    var desc = table.Cell().Element(c => BodyCell(c, isTotal)).AlignLeft()
                        .Text(l.Descricao).FontSize(fs);
                    if (isTotal) desc.Bold();

                    foreach (var v in l.Valores)
                    {
                        var cell = table.Cell().Element(c => BodyCell(c, isTotal)).AlignRight()
                            .Text(FormatK(v)).FontSize(fs)
                            .FontColor(v < 0 ? Colors.Red.Medium : Colors.Grey.Darken3);
                        if (isTotal) cell.Bold();
                    }
                }
            });

            col.Item().Text("Valores em milhares de R$ (k). Projeção para os 12 primeiros meses.")
                .FontSize(7).Italic().FontColor(Colors.Grey.Medium);
        });

        static IContainer HeaderCell(IContainer c) =>
            c.Background(IdeorPurple).PaddingVertical(2).PaddingHorizontal(2);

        static IContainer BodyCell(IContainer c, bool isTotal) =>
            c.Background(isTotal ? Colors.Grey.Lighten3 : Colors.White)
             .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
             .PaddingVertical(1.5f).PaddingHorizontal(2);
    }

    /// <summary>Formata um valor em milhares (ex.: 12500 → "12,5k", 0 → "0").</summary>
    private static string FormatK(decimal v)
    {
        if (v == 0) return "0";
        var k = v / 1000m;
        return k.ToString("0.#", CultureInfo.GetCultureInfo("pt-BR")) + "k";
    }
}

/// <summary>
/// Interface do serviço de exportação de PDF
/// </summary>
public interface IPdfExportService
{
    Task<byte[]?> ExportProjectDocumentsAsync(Guid projectId, Guid userId);
    Task<byte[]?> ExportSinglePhaseDocumentAsync(Guid projectId, Guid userId, string phase);
    Task<byte[]> GenerateStagePdfAsync(string projectId, string taskId, string userId, CancellationToken ct);
    Task<byte[]> GenerateFinalDocumentPdfAsync(string projectId, string docType, string userId, CancellationToken ct);
    Task<byte[]> GenerateFinancialSummaryPdfAsync(string projectId, string userId, CancellationToken ct);
}
