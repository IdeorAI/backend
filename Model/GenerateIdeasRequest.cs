// Model/GenerateIdeasRequest.cs
using Microsoft.Extensions.Primitives;

namespace IdeorAI.Model
{
    public class GenerateIdeasRequest
    {
        public string SeedIdea { get; set; } = "";
        public string SegmentDescription { get; set; } = "";
    }

    public class SegmentIdeasRequest
    {
        public string SegmentDescription { get; set; } = "";
        /// <summary>
        /// Opcional — por padrão 4. Mantido flexível para A/B tests.
        /// </summary>
        public int? Count { get; set; }
        /// <summary>
        /// ID do projeto para salvar as ideias geradas (opcional)
        /// </summary>
        public string? ProjectId { get; set; }
        /// <summary>
        /// ID do owner do projeto (fallback se ProjectId não estiver presente)
        /// </summary>
        public string? OwnerId { get; set; }
        /// <summary>
        /// Categoria/segmento selecionado para salvar no projeto
        /// </summary>
        public string? Category { get; set; }
    }
}

// Model/GenerateIdeasResponse.cs
namespace IdeorAI.Model
{
    public class GenerateIdeasResponse
    {
        public List<string> Ideas { get; set; } = new List<string>();
        public string RequestId { get; set; } // Adicione esta propriedade
    }


}
