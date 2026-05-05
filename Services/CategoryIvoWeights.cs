namespace IdeorAI.Services;

/// <summary>
/// Pesos das 5 dimensões IVO (O/M/V/E/T) por categoria.
/// Cada categoria tem pesos somando 1.0 — duas dimensões dominantes (0.30 + 0.25)
/// e três secundárias (0.15 cada). Usado pelo ScoreService (Opção A).
///
/// Dimensões:
///   O = Originalidade
///   M = Mercado
///   V = Validação
///   E = Execução
///   T = Timing
/// </summary>
public static class CategoryIvoWeights
{
    public readonly record struct Weights(decimal O, decimal M, decimal V, decimal E, decimal T);

    private static readonly Weights Balanced = new(0.20m, 0.20m, 0.20m, 0.20m, 0.20m);

    private static readonly IReadOnlyDictionary<string, Weights> Map = new Dictionary<string, Weights>
    {
        // Software/IA — originalidade e timing dominam
        ["software-ia-dados"]              = new(0.30m, 0.15m, 0.15m, 0.15m, 0.25m),
        // Finanças/Seguros — mercado e execução dominam
        ["financas-seguros"]               = new(0.15m, 0.30m, 0.15m, 0.25m, 0.15m),
        // Saúde/Ciências — validação e execução são críticas
        ["saude-ciencias-vida"]            = new(0.15m, 0.15m, 0.30m, 0.25m, 0.15m),
        // Varejo/E-commerce — mercado e timing dominam
        ["varejo-ecommerce-marketing"]     = new(0.15m, 0.30m, 0.15m, 0.15m, 0.25m),
        // Indústria/IoT — execução e validação dominam
        ["industria-manufatura-iot"]       = new(0.15m, 0.15m, 0.25m, 0.30m, 0.15m),
        // Logística/Mobilidade — execução e mercado dominam
        ["logistica-mobilidade-transporte"] = new(0.15m, 0.25m, 0.15m, 0.30m, 0.15m),
        // Energia/Clima — originalidade e timing (regulatório) dominam
        ["energia-clima-sustentabilidade"] = new(0.30m, 0.15m, 0.15m, 0.15m, 0.25m),
        // Imóveis/Construção — mercado e execução dominam
        ["imoveis-construcao"]             = new(0.15m, 0.30m, 0.15m, 0.25m, 0.15m),
        // Educação/RH — validação e mercado dominam
        ["educacao-rh"]                    = new(0.15m, 0.25m, 0.30m, 0.15m, 0.15m),
        // Segurança Digital — originalidade e execução dominam
        ["seguranca-infraestrutura-digital"] = new(0.30m, 0.15m, 0.15m, 0.25m, 0.15m),
        // Governo/Jurídico — execução e validação dominam
        ["governo-juridico-setor-publico"] = new(0.15m, 0.15m, 0.25m, 0.30m, 0.15m),
        // Mídia/Entretenimento — originalidade e timing dominam
        ["midia-entretenimento-criadores"] = new(0.30m, 0.15m, 0.15m, 0.15m, 0.25m),
    };

    public static Weights For(string? category) =>
        category != null && Map.TryGetValue(category, out var w) ? w : Balanced;

    /// <summary>Calcula a média ponderada do IVO (escala 0-10) para a categoria.</summary>
    public static decimal WeightedIvo(string? category, decimal o, decimal m, decimal v, decimal e, decimal t)
    {
        var w = For(category);
        return o * w.O + m * w.M + v * w.V + e * w.E + t * w.T;
    }
}
