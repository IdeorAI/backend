using IdeorAI.Model.Entities;
using IdeorAI.Model.SupabaseModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text.Json;

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
            EvaluationResult = model.EvaluationResult.HasValue
                ? JsonDocument.Parse(model.EvaluationResult.Value.GetRawText())
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
}

/// <summary>
/// Interface do serviço de exportação de PDF
/// </summary>
public interface IPdfExportService
{
    Task<byte[]?> ExportProjectDocumentsAsync(Guid projectId, Guid userId);
    Task<byte[]?> ExportSinglePhaseDocumentAsync(Guid projectId, Guid userId, string phase);
}
