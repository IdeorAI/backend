using System.Text;
using System;
using IdeorAI.Model;
using IdeorAI.Model.ContentResponse;
using Newtonsoft.Json;

namespace IdeorAI.Client
{
    public class GeminiApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        public GeminiApiClient(string apiKey)
        {
            _httpClient = new HttpClient();
            _apiKey = apiKey;
        }
        public async Task<string> GenerateContentAsync(string prompt)
        {
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={_apiKey}";
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

            HttpResponseMessage response = await _httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                string jsonResponse = await response.Content.ReadAsStringAsync();
                // You can deserialize jsonResponse if needed
                var gemini = JsonConvert.DeserializeObject<IdeorAI.Model.ContentResponse.ContentResponse>(jsonResponse);
                return gemini.Candidates[0].Content.Parts[0].Text;
            }
            else
            {
                throw new Exception("Error communicating with Gemini API.");
            }
        }

        public async Task<List<string>> GenerateStartupIdeasAsync(string seedIdea, string segmentDescription)
        {
            // 1) segurança das entradas (hard cap)
            string seed = (seedIdea ?? "").Trim();
            if (seed.Length > 400) seed = seed.Substring(0, 400);
            string segment = (segmentDescription ?? "").Trim();
            if (segment.Length > 400) segment = segment.Substring(0, 400);

            // 2) prompt para saída JSON
            var systemInstruction =
            @"Você é um gerador de ideias de startups. 
            Gere exatamente 3 ideias curtas, cada uma com no máximo 400 caracteres.
            Baseie-se na ideia semente e na descrição do segmento informadas.
            Retorne APENAS JSON com o formato:
            { ""ideas"": [""..."",""..."" , ""...""] } 
            Sem comentários, sem markdown.";

            var userText = $@"
            IDEIA_SEMENTE: ""{seed}""
            SEGMENTO: ""{segment}""
            ";

            // 3) request do Gemini (v1beta generateContent)
            // Dica: se sua versão do endpoint aceitar, inclua generationConfig.response_mime_type
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
                    temperature = 0.7,
                    topP = 0.9,
                    maxOutputTokens = 512,
                    response_mime_type = "application/json"
                }
            };

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={_apiKey}";
            string jsonRequest = JsonConvert.SerializeObject(requestObj);
            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Gemini retornou {response.StatusCode}");

            string jsonResponse = await response.Content.ReadAsStringAsync();

            // Estrutura de resposta atual do seu projeto (Candidates -> Content -> Parts -> Text) :contentReference[oaicite:1]{index=1}
            var gemini = JsonConvert.DeserializeObject<IdeorAI.Model.ContentResponse.ContentResponse>(jsonResponse);
            var text = gemini?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? "";

            // 4) parse do JSON produzido pelo modelo
            // formato esperado: { "ideas": ["...", "...", "..."] }
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

            // No método GenerateStartupIdeasAsync, após obter a resposta:
            Console.WriteLine($"Resposta bruta do Gemini: {jsonResponse}");
            return ideas;
        }
    }
}