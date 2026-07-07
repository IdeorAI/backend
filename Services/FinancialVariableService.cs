using System.Text.Json;
using IdeorAI.Model.SupabaseModels;
using static Supabase.Postgrest.Constants;

namespace IdeorAI.Services;

/// <summary>
/// Spec 027 — fonte de verdade das variáveis financeiras.
/// Extrai os valores financeiros do JSON das etapas 4/5 (mapa da Fase 0),
/// normaliza e faz upsert em financial_variables. Lê as variáveis para ancorar
/// a DRE. Tolerante a falha — nunca quebra o fluxo da etapa.
/// </summary>
public interface IFinancialVariableService
{
    Task ExtractAndUpsertFromStageAsync(Guid projectId, string stage, string? contentJson);
    Task<List<FinancialVariableModel>> GetByProjectAsync(Guid projectId);

    /// <summary>
    /// Spec 027 (C1/C2) — write-back: ao editar a DRE, recalcula as variáveis-âncora
    /// a partir das 12 colunas, faz upsert com <c>locked=true</c> e reescreve os
    /// source_path na etapa 4 (marcando-a desatualizada). Tolerante a falha.
    /// </summary>
    Task SyncFromDreAsync(Guid projectId, string dreJson);
}

public class FinancialVariableService : IFinancialVariableService
{
    private readonly Supabase.Client _supabase;
    private readonly ILogger<FinancialVariableService> _logger;

    public FinancialVariableService(Supabase.Client supabase, ILogger<FinancialVariableService> logger)
    {
        _supabase = supabase;
        _logger = logger;
    }

    /// <summary>
    /// Mapa canônico (Fase 0): por etapa, lista (key, source_path, unidade-hint).
    /// Só as 7 variáveis isoláveis da v1. source_path usa notação a.b.c para o JSON.
    /// </summary>
    private static readonly Dictionary<string, (string Key, string Path, string Unit)[]> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["etapa4"] = new[]
        {
            ("receita_mensal_media", "projecao_financeira_simplificada.ano_1.receita_mensal_media", "BRL/mês"),
            ("custos_mensais",       "projecao_financeira_simplificada.ano_1.custos_mensais",       "BRL/mês"),
            ("margem_bruta_pct",     "projecao_financeira_simplificada.ano_1.margem_bruta",         "pct"),
            ("break_even_meses",     "projecao_financeira_simplificada.ano_1.break_even_months",    "meses"),
            ("cac",                  "unit_economics.cac_estimado",                                  "BRL"),
            ("ltv",                  "unit_economics.ltv_estimado",                                  "BRL"),
        },
        ["etapa5"] = new[]
        {
            ("custo_desenvolvimento_total", "custo_desenvolvimento.estimativa_total", "BRL"),
        },
    };

    public async Task ExtractAndUpsertFromStageAsync(Guid projectId, string stage, string? contentJson)
    {
        if (!Map.TryGetValue(stage, out var entries) || string.IsNullOrWhiteSpace(contentJson))
            return;

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(JsonSanitizer.ExtractJson(contentJson));
            root = doc.RootElement.Clone();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FinVars] JSON da {Stage} não parseável — sem extração", stage);
            return;
        }

        // Variáveis já travadas (editadas à mão na DRE) NÃO são sobrescritas (C2).
        var locked = (await GetByProjectAsync(projectId))
            .Where(v => v.Locked)
            .Select(v => v.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, path, unit) in entries)
        {
            if (locked.Contains(key)) continue;

            var raw = GetByPath(root, path);
            if (raw == null) continue;

            var parsed = FinancialValueParser.TryParse(raw, unit);
            if (parsed == null) continue;

            await UpsertAsync(projectId, key, parsed.Value, parsed.Unit, stage, path, locked: false);
        }
    }

    public async Task<List<FinancialVariableModel>> GetByProjectAsync(Guid projectId)
    {
        try
        {
            var resp = await _supabase
                .From<FinancialVariableModel>()
                .Filter("project_id", Operator.Equals, projectId.ToString())
                .Get();
            return resp.Models;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FinVars] Falha ao ler variáveis do projeto {ProjectId}", projectId);
            return new List<FinancialVariableModel>();
        }
    }

    /// <summary>Upsert por (project_id, key) — usa UNIQUE; atualiza se existir, insere se não.</summary>
    public async Task UpsertAsync(Guid projectId, string key, decimal value, string unit,
        string sourceStage, string? sourcePath, bool locked)
    {
        try
        {
            var existing = (await _supabase
                .From<FinancialVariableModel>()
                .Filter("project_id", Operator.Equals, projectId.ToString())
                .Filter("key", Operator.Equals, key)
                .Limit(1)
                .Get()).Models.FirstOrDefault();

            if (existing != null)
            {
                existing.Value = value;
                existing.Unit = unit;
                existing.SourceStage = sourceStage;
                existing.SourcePath = sourcePath;
                existing.Locked = locked || existing.Locked;
                existing.UpdatedAt = DateTime.UtcNow;
                await _supabase.From<FinancialVariableModel>()
                    .Filter("id", Operator.Equals, existing.Id)
                    .Update(existing);
            }
            else
            {
                await _supabase.From<FinancialVariableModel>().Insert(new FinancialVariableModel
                {
                    Id = Guid.NewGuid().ToString(),
                    ProjectId = projectId.ToString(),
                    Key = key,
                    Value = value,
                    Unit = unit,
                    SourceStage = sourceStage,
                    SourcePath = sourcePath,
                    Locked = locked,
                    UpdatedAt = DateTime.UtcNow,
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FinVars] Falha no upsert da variável {Key} (não crítico)", key);
        }
    }

    public async Task SyncFromDreAsync(Guid projectId, string dreJson)
    {
        // 1) Recalcula as médias mensais a partir das 12 colunas da DRE editada.
        var (receitaMedia, custosMedia) = ComputeDreAverages(dreJson);
        if (receitaMedia == null && custosMedia == null) return;

        // 2) Upsert das variáveis-âncora com locked=true (edição manual vence — C2).
        if (receitaMedia.HasValue)
            await UpsertAsync(projectId, "receita_mensal_media", receitaMedia.Value, "BRL/mês",
                "dre", "projecao_financeira_simplificada.ano_1.receita_mensal_media", locked: true);
        if (custosMedia.HasValue)
            await UpsertAsync(projectId, "custos_mensais", custosMedia.Value, "BRL/mês",
                "dre", "projecao_financeira_simplificada.ano_1.custos_mensais", locked: true);

        // 3) Reescreve os valores na etapa 4 e marca-a desatualizada (Spec 023).
        await WriteBackToStage4Async(projectId, receitaMedia, custosMedia);
    }

    /// <summary>Média mensal de receita_bruta e de (cpv+opex) a partir do JSON da DRE.</summary>
    private (decimal? Receita, decimal? Custos) ComputeDreAverages(string dreJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSanitizer.ExtractJson(dreJson));
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("dre", out var inner))
                root = inner;
            if (!root.TryGetProperty("linhas", out var linhas) || linhas.ValueKind != JsonValueKind.Array)
                return (null, null);

            decimal receitaTotal = 0, custosTotal = 0;
            var anyReceita = false; var anyCustos = false;
            foreach (var l in linhas.EnumerateArray())
            {
                var tipo = l.TryGetProperty("tipo", out var t) ? t.GetString() : "entrada";
                if (!string.Equals(tipo, "entrada", StringComparison.OrdinalIgnoreCase)) continue;
                var grupo = (l.TryGetProperty("grupo", out var g) ? g.GetString() : "")?.ToLowerInvariant() ?? "";
                var soma = SumValores(l);
                if (grupo == "receita") { receitaTotal += soma; anyReceita = true; }
                else if (grupo is "cpv" or "opex") { custosTotal += soma; anyCustos = true; }
            }
            return (anyReceita ? receitaTotal / 12m : null, anyCustos ? custosTotal / 12m : null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FinVars] Falha ao computar médias da DRE");
            return (null, null);
        }
    }

    private static decimal SumValores(JsonElement line)
    {
        if (!line.TryGetProperty("valores", out var arr) || arr.ValueKind != JsonValueKind.Array) return 0;
        decimal s = 0;
        foreach (var v in arr.EnumerateArray())
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) s += d;
        return s;
    }

    /// <summary>Reescreve os valores nos source_path da etapa 4 e marca a task desatualizada.</summary>
    private async Task WriteBackToStage4Async(Guid projectId, decimal? receitaMedia, decimal? custosMedia)
    {
        try
        {
            var task = (await _supabase
                .From<IdeorAI.Model.SupabaseModels.TaskModel>()
                .Filter("project_id", Operator.Equals, projectId.ToString())
                .Filter("phase", Operator.Equals, "etapa4")
                .Order("updated_at", Ordering.Descending)
                .Limit(1)
                .Get()).Models.FirstOrDefault();

            if (task?.Content == null) return;

            using var doc = JsonDocument.Parse(JsonSanitizer.ExtractJson(task.Content));
            var node = System.Text.Json.Nodes.JsonNode.Parse(doc.RootElement.GetRawText());
            var ano1 = node?["projecao_financeira_simplificada"]?["ano_1"];
            if (ano1 == null) return;

            if (receitaMedia.HasValue) ano1["receita_mensal_media"] = $"R$ {receitaMedia.Value:N2}";
            if (custosMedia.HasValue) ano1["custos_mensais"] = $"R$ {custosMedia.Value:N2}";

            task.Content = node!.ToJsonString();
            task.OutdatedAt = DateTime.UtcNow; // Spec 023 — badge "Desatualizada".
            task.Project = null;
            task.IaEvaluations = null;
            await _supabase.From<IdeorAI.Model.SupabaseModels.TaskModel>()
                .Filter("id", Operator.Equals, task.Id)
                .Update(task);

            _logger.LogInformation("[FinVars] Write-back na etapa4 do projeto {ProjectId} (marcada desatualizada)", projectId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FinVars] Falha no write-back da etapa4 (não crítico)");
        }
    }

    /// <summary>Navega um caminho "a.b.c" num JsonElement; retorna a string do valor ou null.</summary>
    private static string? GetByPath(JsonElement root, string path)
    {
        var cur = root;
        foreach (var part in path.Split('.'))
        {
            if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(part, out var next))
                return null;
            cur = next;
        }
        return cur.ValueKind switch
        {
            JsonValueKind.String => cur.GetString(),
            JsonValueKind.Number => cur.GetRawText(),
            _ => null,
        };
    }
}
