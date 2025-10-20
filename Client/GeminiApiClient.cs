using System.Text;
using System;
using IdeorAI.Model;
using IdeorAI.Model.ContentResponse;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using Microsoft.Extensions.Configuration;

namespace IdeorAI.Client
{
    public class GeminiApiClient
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly BackendMetrics _metrics;
        private readonly ILogger<GeminiApiClient> _logger;
        private readonly ActivitySource _activitySource;

        public GeminiApiClient(
       HttpClient http,                     // vem do HttpClientFactory
       BackendMetrics metrics,
       ILogger<GeminiApiClient> logger,
       IConfiguration config                // para ler a API key
   )
        {
            _http = http;

            // (fallback) timeout local — também deixaremos configurado no Program.cs
            if (_http.Timeout == Timeout.InfiniteTimeSpan)
                _http.Timeout = TimeSpan.FromSeconds(20);

            // Aceitar JSON por padrão
            _http.DefaultRequestHeaders.Accept.Clear();
            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json")
            );

            // Se quiser, pode definir a BaseAddress aqui ou no Program.cs
            // _http.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");

            // Lê a API key - PRIORIDADE: Variável de ambiente > appsettings
            // Isso permite sobrescrever a key em produção sem alterar código
            var rawApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                      ?? config["Gemini:ApiKey"]
                      ?? throw new InvalidOperationException("Gemini API key not configured.");

            // Log para debug (apenas primeiros e últimos 4 caracteres por segurança)
            logger.LogInformation("=== GEMINI API KEY DEBUG ===");
            logger.LogInformation("Raw API Key length: {Length}", rawApiKey?.Length ?? 0);
            logger.LogInformation("Raw API Key first 10 chars: {First}", rawApiKey?.Length >= 10 ? rawApiKey.Substring(0, 10) : rawApiKey);
            logger.LogInformation("Raw API Key last 10 chars: {Last}", rawApiKey?.Length >= 10 ? rawApiKey.Substring(rawApiKey.Length - 10) : rawApiKey);
            logger.LogInformation("Raw API Key has whitespace: {HasWhitespace}", rawApiKey?.Any(char.IsWhiteSpace) ?? false);
            logger.LogInformation("Raw API Key has newline: {HasNewline}", rawApiKey?.Contains('\n') ?? false);

            // Sanitizar a API key removendo espaços, newlines, tabs (problema comum em plataformas de deploy)
            _apiKey = System.Text.RegularExpressions.Regex.Replace(rawApiKey, @"[\s\n\r\t]", "");

            // Log da key sanitizada
            logger.LogInformation("Sanitized API Key length: {Length}", _apiKey?.Length ?? 0);
            logger.LogInformation("Sanitized API Key first 10 chars: {First}", _apiKey?.Length >= 10 ? _apiKey.Substring(0, 10) : _apiKey);
            logger.LogInformation("Sanitized API Key last 10 chars: {Last}", _apiKey?.Length >= 10 ? _apiKey.Substring(_apiKey.Length - 10) : _apiKey);
            logger.LogInformation("Characters removed: {Removed}", (rawApiKey?.Length ?? 0) - (_apiKey?.Length ?? 0));
            logger.LogInformation("===========================");

            _metrics = metrics;
            _logger = logger;
            _activitySource = new ActivitySource("IdeorAI.Service");
        }

        public async Task<string> GenerateContentAsync(string prompt, CancellationToken ct = default)

        {
            using var activity = _activitySource.StartActivity("Gemini.GenerateContent");
            // Removido o using problemático do RequestsInFlight
            _metrics.RequestsInFlight.Add(1);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                const string model = "gemini-2.5-flash";
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_apiKey}";

                var request = new ContentRequest
                {
                    contents = new[]
                    {
                        new Model.Content
                        {
                            parts = new[]
                            {
                                new Model.Part
                                {
                                    text = prompt
                                }
                            }
                        }
                    }
                };

                string jsonRequest = JsonConvert.SerializeObject(request);
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _http.PostAsync(url, content, ct);
                stopwatch.Stop();

                // Registrar métricas
                _metrics.GeminiDuration.Record(stopwatch.Elapsed.TotalSeconds,
                    new KeyValuePair<string, object?>("status", ((int)response.StatusCode).ToString()));

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var gemini = JsonConvert.DeserializeObject<IdeorAI.Model.ContentResponse.ContentResponse>(jsonResponse);
                    return gemini?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? string.Empty;
                }
                else
                {
                    _metrics.GeminiErrors.Add(1,
                        new KeyValuePair<string, object?>("code", response.StatusCode.ToString()));
                    _logger.LogError("Gemini API error: {StatusCode}", response.StatusCode);
                    throw new Exception("Error communicating with Gemini API.");
                }
            }
            catch (HttpRequestException ex)
            {
                stopwatch.Stop();
                _metrics.GeminiErrors.Add(1,
                    new KeyValuePair<string, object?>("code", "http_exception"));
                _logger.LogError(ex, "Erro de rede ao chamar API do Gemini. Verifique conexão com a internet e configurações de proxy/firewall.");
                throw new Exception($"Erro de rede ao conectar com API do Gemini: {ex.Message}. Verifique sua conexão com a internet.", ex);
            }
            catch (TaskCanceledException ex)
            {
                stopwatch.Stop();
                _metrics.GeminiErrors.Add(1,
                    new KeyValuePair<string, object?>("code", "timeout"));
                _logger.LogError(ex, "Timeout ao chamar API do Gemini");
                throw new Exception("Timeout ao conectar com API do Gemini. A requisição demorou muito tempo.", ex);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _metrics.GeminiErrors.Add(1,
                    new KeyValuePair<string, object?>("code", "exception"));
                _logger.LogError(ex, "Gemini API call failed");
                throw;
            }
            finally
            {
                _metrics.RequestsInFlight.Add(-1);
            }
        }

        public async Task<List<string>> GenerateStartupIdeasAsync(string seedIdea, string segmentDescription, CancellationToken ct = default)
        {
            using var activity = _activitySource.StartActivity("Gemini.GenerateIdeas");
            // Removido o using problemático do RequestsInFlight
            _metrics.RequestsInFlight.Add(1);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // 1) segurança das entradas (hard cap)
                string seed = (seedIdea ?? "").Trim();
                if (seed.Length > 400) seed = seed.Substring(0, 400);
                string segment = (segmentDescription ?? "").Trim();
                if (segment.Length > 400) segment = segment.Substring(0, 400);

                // 2) prompt para saída JSON
                var systemInstruction =
                @"Você é um gerador de ideias de startups. 
                Seu trabalho é corrigir a ideia_segmento e propor 3 versões, 
                cada uma com no máximo 400 caracteres.
                Baseie-se na ideia semente e na descrição do segmento informadas.
                Retorne APENAS JSON com o formato:
                { ""ideas"": [""..."",""..."" , ""...""] } 
                Sem comentários, sem markdown.";

                var userText = $@"
                IDEIA_SEMENTE: ""{seed}""
                SEGMENTO: ""{segment}""
                ";

                // 3) request do Gemini (v1beta generateContent)
                var requestObj = new
                {
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new[] { new { text = userText } }
                        }
                    },
                    systemInstruction = new
                    {
                        parts = new[] { new { text = systemInstruction } }
                    },
                    generationConfig = new
                    {
                        temperature = 0.3,
                        topP = 0.6,
                        maxOutputTokens = 2048,
                        response_mime_type = "application/json"
                    }
                };

                const string model = "gemini-2.5-flash";
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_apiKey}";

                // Debug: Log da URL (sem mostrar key completa)
                _logger.LogInformation("Calling Gemini API - Key length in URL: {KeyLength}, URL length: {UrlLength}", _apiKey?.Length ?? 0, url?.Length ?? 0);
                _logger.LogInformation("URL first 100 chars: {UrlStart}", url?.Length >= 100 ? url.Substring(0, 100) : url);

                string jsonRequest = JsonConvert.SerializeObject(requestObj);
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                var response = await _http.PostAsync(url, content, ct);
                stopwatch.Stop();

                // Registrar métricas
                _metrics.GeminiDuration.Record(stopwatch.Elapsed.TotalSeconds,
                    new KeyValuePair<string, object?>("status", ((int)response.StatusCode).ToString()));

                if (!response.IsSuccessStatusCode)
                {
                    _metrics.GeminiErrors.Add(1,
                        new KeyValuePair<string, object?>("code", response.StatusCode.ToString()));
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Gemini API error: {StatusCode}, Body: {ErrorBody}", response.StatusCode, errorBody);
                    throw new Exception($"Gemini retornou {response.StatusCode}: {errorBody}");
                }

                string jsonResponse = await response.Content.ReadAsStringAsync();

                // Log da resposta para debug
                _logger.LogInformation("Gemini raw response (GenerateStartupIdeasAsync): {Response}", jsonResponse);

                var gemini = JsonConvert.DeserializeObject<IdeorAI.Model.ContentResponse.ContentResponse>(jsonResponse);

                // Verificar se há conteúdo válido
                if (gemini?.Candidates == null || gemini.Candidates.Length == 0)
                {
                    _logger.LogError("Gemini retornou resposta sem candidates");
                    throw new Exception("Resposta do Gemini não contém candidatos.");
                }

                var candidate = gemini.Candidates[0];

                // Verificar finishReason
                if (candidate.FinishReason == "MAX_TOKENS")
                {
                    _logger.LogWarning("Gemini atingiu MAX_TOKENS. Resposta pode estar incompleta.");
                }

                var text = candidate?.Content?.Parts?[0]?.Text ?? "";

                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogError("Gemini retornou resposta sem texto. FinishReason: {FinishReason}", candidate?.FinishReason);
                    throw new Exception($"Resposta do Gemini vazia. Razão: {candidate?.FinishReason ?? "desconhecida"}");
                }

                _logger.LogInformation("Gemini text (GenerateStartupIdeasAsync): {Text}", text);

                text = StripCodeFences(text);
                var parsed = JsonConvert.DeserializeObject<GenerateIdeasResponse>(text);

                if (parsed?.Ideas == null || parsed.Ideas.Count == 0)
                    throw new Exception("Resposta do modelo não pôde ser interpretada.");

                // 5) pós-processamento defensivo
                var ideas = parsed.Ideas
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .Select(s => s.Length > 400 ? s.Substring(0, 400) : s)
                    .Take(3)
                    .ToList();

                if (ideas.Count != 3)
                    throw new Exception("O modelo não retornou 3 ideias.");

                _logger.LogDebug($"Resposta bruta do Gemini: {jsonResponse}");
                return ideas;
            }
            catch (HttpRequestException ex)
            {
                stopwatch.Stop();
                _metrics.GeminiErrors.Add(1,
                    new KeyValuePair<string, object?>("code", "http_exception"));
                _logger.LogError(ex, "Erro de rede ao chamar API do Gemini (GenerateStartupIdeasAsync). Verifique conexão com a internet e configurações de proxy/firewall.");
                throw new Exception($"Erro de rede ao conectar com API do Gemini: {ex.Message}. Verifique sua conexão com a internet.", ex);
            }
            catch (TaskCanceledException ex)
            {
                stopwatch.Stop();
                _metrics.GeminiErrors.Add(1,
                    new KeyValuePair<string, object?>("code", "timeout"));
                _logger.LogError(ex, "Timeout ao chamar API do Gemini (GenerateStartupIdeasAsync)");
                throw new Exception("Timeout ao conectar com API do Gemini. A requisição demorou muito tempo.", ex);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _metrics.GeminiErrors.Add(1,
                    new KeyValuePair<string, object?>("code", "exception"));
                _logger.LogError(ex, "Gemini API call failed");
                throw;
            }
            finally
            {
                _metrics.RequestsInFlight.Add(-1);
            }
        }

        private static string StripCodeFences(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            // remove cercas tipo ```json ... ``` ou ``` ...
            var rx = new Regex("^```(?:json)?\\s*|\\s*```$", RegexOptions.Multiline);
            return rx.Replace(text, "");
        }

        /// <summary>
        /// Fluxo comum para pedir N ideias em JSON e parsear em List&lt;string&gt;.
        /// </summary>
        private async Task<List<string>> GenerateIdeasCoreAsync(
            string systemInstruction,
            string userText,
            int expectedCount,
            int maxCharsPerIdea = 400,
            CancellationToken ct = default)
        {
            var requestObj = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = userText } }
                    }
                },
                systemInstruction = new
                {
                    parts = new[] { new { text = systemInstruction } }
                },
                generationConfig = new
                {
                    temperature = 0.9,
                    topP = 0.9,
                    maxOutputTokens = 768,
                    response_mime_type = "application/json"
                }
            };

            const string model = "gemini-2.5-flash";
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_apiKey}";

            string jsonRequest = JsonConvert.SerializeObject(requestObj);
            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(url, content, ct);
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Gemini retornou {response.StatusCode}");

            string jsonResponse = await response.Content.ReadAsStringAsync();

            var gemini = JsonConvert.DeserializeObject<IdeorAI.Model.ContentResponse.ContentResponse>(jsonResponse);
            var rawText = gemini?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? "";
            var text = StripCodeFences(rawText);

            var parsed = JsonConvert.DeserializeObject<GenerateIdeasResponse>(text);
            if (parsed?.Ideas == null || parsed.Ideas.Count == 0)
                throw new Exception("Resposta do modelo não pôde ser interpretada.");

            var ideas = parsed.Ideas
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Select(s => s.Length > maxCharsPerIdea ? s.Substring(0, maxCharsPerIdea) : s)
                .ToList();

            if (ideas.Count < expectedCount)
                throw new Exception($"O modelo retornou apenas {ideas.Count} ideias, mas eram esperadas {expectedCount}.");

            if (ideas.Count > expectedCount)
                ideas = ideas.Take(expectedCount).ToList();

            _logger.LogDebug("Gemini raw: {Json}", jsonResponse);
            _logger.LogDebug("Gemini text: {Text}", text);
            _logger.LogDebug("Ideias geradas: {Count}", ideas.Count);

            return ideas;
        }

        /// <summary>
        /// Gera 4 ideias inovadoras a partir do segmento.
        /// Retorna string[] no formato "Título — Subtítulo",
        /// com título limitado a 6 palavras e total ≤ 400 caracteres.
        /// Parser tolera string[], objetos {title, subtitle} e strings com objetos JSON dentro.
        /// </summary>
        public async Task<List<string>> GenerateSegmentIdeasAsync(string segmentDescription, int count = 4, CancellationToken ct = default)
        {
            string segment = (segmentDescription ?? "").Trim();
            if (segment.Length > 400) segment = segment.Substring(0, 400);

            var systemInstruction = $@"Você é um gerador de ideias de startups.
Gere {count} ideias inovadoras para o segmento fornecido.
Retorne APENAS um JSON válido no formato:
{{""ideas"":[{{""title"":""Título curto"",""subtitle"":""Descrição breve""}}]}}
Cada título deve ter no máximo 6 palavras.
Cada subtítulo deve ter 1-2 frases curtas.
Total por ideia: máximo 400 caracteres.";

            var userText = $@"SEGMENTO: ""{segment}""";

            return await GenerateIdeasCoreFlexibleAsync(systemInstruction, userText, expectedCount: count, ct: ct);
        }

        // ========= Núcleo "flexível" para objetos/string, com pós-processamento =========

        private async Task<List<string>> GenerateIdeasCoreFlexibleAsync(
            string systemInstruction,
            string userText,
            int expectedCount,
            int maxCharsPerIdea = 400,
            CancellationToken ct = default)
        {
            var requestObj = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = userText } }
                    }
                },
                systemInstruction = new
                {
                    parts = new[] { new { text = systemInstruction } }
                },
                generationConfig = new
                {
                    temperature = 0.5,
                    topP = 0.9,
                    maxOutputTokens = 2048,
                    response_mime_type = "application/json"
                }
            };

            const string model = "gemini-2.5-flash";
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_apiKey}";

            string jsonRequest = JsonConvert.SerializeObject(requestObj);
            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(url, content, ct);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Gemini retornou {response.StatusCode}");

            string jsonResponse = await response.Content.ReadAsStringAsync();

            // Log da resposta completa do Gemini para debug
            _logger.LogInformation("Gemini raw response: {Response}", jsonResponse);

            var gemini = JsonConvert.DeserializeObject<IdeorAI.Model.ContentResponse.ContentResponse>(jsonResponse);

            // Verificar se há conteúdo válido
            if (gemini?.Candidates == null || gemini.Candidates.Length == 0)
            {
                _logger.LogError("Gemini retornou resposta sem candidates");
                throw new Exception("Resposta do Gemini não contém candidatos.");
            }

            var candidate = gemini.Candidates[0];

            // Verificar finishReason
            if (candidate.FinishReason == "MAX_TOKENS")
            {
                _logger.LogWarning("Gemini atingiu MAX_TOKENS. Resposta pode estar incompleta.");
            }

            var rawText = candidate?.Content?.Parts?[0]?.Text ?? "";

            if (string.IsNullOrWhiteSpace(rawText))
            {
                _logger.LogError("Gemini retornou resposta sem texto. FinishReason: {FinishReason}", candidate?.FinishReason);
                throw new Exception($"Resposta do Gemini vazia. Razão: {candidate?.FinishReason ?? "desconhecida"}");
            }

            _logger.LogInformation("Gemini raw text (before strip): {RawText}", rawText);

            var text = StripCodeFences(rawText);

            _logger.LogInformation("Gemini text (after strip): {Text}", text);

            var combined = ParseFlexibleIdeasToCombinedStrings(text, expectedCount, maxCharsPerIdea);

            // Não explodir se vierem menos de expectedCount; devolve o que deu.
            // (o frontend preenche com fallbacks)
            if (combined.Count > expectedCount)
                combined = combined.Take(expectedCount).ToList();

            _logger.LogDebug("Gemini text: {Text}", text);
            _logger.LogDebug("Ideias (combined): {Ideas}", string.Join(" | ", combined));


            if (combined.Count == 0)
                throw new Exception("Resposta do modelo não pôde ser interpretada.");

            return combined;
        }

        private List<string> ParseFlexibleIdeasToCombinedStrings(string text, int expectedCount, int maxCharsPerIdea)
        {
            var results = new List<string>();

            // 1) Tenta como JObject com 'ideas'
            try
            {
                var token = JToken.Parse(text);
                if (token is JObject jobj && jobj["ideas"] is JArray arr1)
                {
                    results = ParseIdeasArray(arr1, maxCharsPerIdea);
                    if (results.Count > 0) return results;
                }
                else if (token is JArray arr2)
                {
                    results = ParseIdeasArray(arr2, maxCharsPerIdea);
                    if (results.Count > 0) return results;
                }
            }
            catch
            {
                // 2) Regex para extrair só o array de ideas
                var m = Regex.Match(text, "\"ideas\"\\s*:\\s*(\\[.*?\\])", RegexOptions.Singleline);
                if (m.Success)
                {
                    try
                    {
                        var arr = JArray.Parse(m.Groups[1].Value);
                        results = ParseIdeasArray(arr, maxCharsPerIdea);
                        if (results.Count > 0) return results;
                    }
                    catch { /* ignore */ }
                }

                // 3) Tenta array raiz direto
                try
                {
                    var arr = JArray.Parse(text);
                    results = ParseIdeasArray(arr, maxCharsPerIdea);
                    if (results.Count > 0) return results;
                }
                catch { /* ignore */ }
            }

            // 4) Último recurso: tenta desserializar em string[] padrão
            try
            {
                var parsed = JsonConvert.DeserializeObject<GenerateIdeasResponse>(text);
                if (parsed?.Ideas != null && parsed.Ideas.Count > 0)
                {
                    results = parsed.Ideas
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(TryCombineFromRawString)
                        .Select(s => s.Length > maxCharsPerIdea ? s.Substring(0, maxCharsPerIdea).TrimEnd() : s)
                        .ToList();
                }
            }
            catch { /* ignore */ }

            return results;
        }

        private List<string> ParseIdeasArray(JArray arr, int maxCharsPerIdea)
        {
            var list = new List<string>();
            foreach (var item in arr)
            {
                if (item is JObject o)
                {
                    var title = (o["title"]?.ToString() ?? "").Trim();
                    var subtitle = (o["subtitle"]?.ToString() ?? "").Trim();
                    var combined = CombineTitleSubtitle(title, subtitle, maxCharsPerIdea);
                    if (!string.IsNullOrWhiteSpace(combined))
                        list.Add(combined);
                }
                else if (item is JValue v && v.Type == JTokenType.String)
                {
                    var s = (v.Value<string>() ?? "").Trim();
                    // Pode ser "Título — Subtítulo" OU um objeto JSON em string
                    var fromObj = TryParseIdeaObjectFromString(s);
                    if (fromObj != null)
                    {
                        var combined = CombineTitleSubtitle(fromObj.Title ?? "", fromObj.Subtitle ?? "", maxCharsPerIdea);
                        if (!string.IsNullOrWhiteSpace(combined))
                            list.Add(combined);
                    }
                    else
                    {
                        var combined = TryCombineFromRawString(s);
                        if (!string.IsNullOrWhiteSpace(combined))
                        {
                            if (combined.Length > maxCharsPerIdea)
                                combined = combined.Substring(0, maxCharsPerIdea).TrimEnd();
                            list.Add(combined);
                        }
                    }
                }
            }
            return list;
        }

        /// <summary>Se a string contiver um objeto JSON {title, subtitle}, parseia; senão retorna null.</summary>
        private IdeaObject? TryParseIdeaObjectFromString(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var candidate = StripCodeFences(s.Trim());
            // Alguns modelos enviam objetos como texto com aspas escapadas. Tentar parse direto.
            try
            {
                var tok = JToken.Parse(candidate);
                if (tok is JObject obj)
                {
                    var t = (obj["title"]?.ToString() ?? "").Trim();
                    var st = (obj["subtitle"]?.ToString() ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(t) || !string.IsNullOrWhiteSpace(st))
                        return new IdeaObject { Title = t, Subtitle = st };
                }
            }
            catch { /* ignore */ }
            return null;
        }

        /// <summary>
        /// Tenta formar "Título — Subtítulo" a partir de uma string solta.
        /// Se não houver separador, limita 6 palavras e retorna só o título.
        /// </summary>
        private string TryCombineFromRawString(string s)
        {
            s = s.Trim();
            if (string.IsNullOrWhiteSpace(s)) return "";

            // Separadores comuns usados pelo frontend
            var sepCandidates = new[] { " — ", ": ", " – ", " - ", ". " };
            foreach (var sep in sepCandidates)
            {
                var idx = s.IndexOf(sep, StringComparison.Ordinal);
                if (idx > 0)
                {
                    var title = s.Substring(0, idx).Trim();
                    var subtitle = s.Substring(idx + sep.Length).Trim();
                    return CombineTitleSubtitle(title, subtitle, 400);
                }
            }

            // Sem separador: tudo é título
            var tOnly = LimitWords(s, 6);
            return tOnly;
        }

        /// <summary>Limita o título a 6 palavras; monta "Título — Subtítulo" e corta total a maxChars.</summary>
        private string CombineTitleSubtitle(string title, string subtitle, int maxChars)
        {
            var t = LimitWords(title ?? "", 6);
            var st = (subtitle ?? "").Trim();

            var combined = string.IsNullOrWhiteSpace(st) ? t : $"{t} — {st}";
            if (combined.Length > maxChars)
                combined = combined.Substring(0, maxChars).TrimEnd();

            return combined;
        }

        /// <summary>Limita o texto a N palavras; adiciona reticências se cortar.</summary>
        private static string LimitWords(string text, int maxWords)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var words = Regex.Split(text.Trim(), "\\s+");
            if (words.Length <= maxWords) return string.Join(" ", words);
            return string.Join(" ", words.Take(maxWords)) + "…";
        }

        // Tipos auxiliares locais
        private class IdeaObject
        {
            [JsonProperty("title")]
            public string? Title { get; set; }

            [JsonProperty("subtitle")]
            public string? Subtitle { get; set; }
        }




    }
}
