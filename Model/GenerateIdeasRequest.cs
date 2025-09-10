// Model/GenerateIdeasRequest.cs
namespace IdeorAI.Model
{
    public class GenerateIdeasRequest
    {
        public string SeedIdea { get; set; } = "";
        public string SegmentDescription { get; set; } = "";
    }
}

// Model/GenerateIdeasResponse.cs
namespace IdeorAI.Model
{
    public class GenerateIdeasResponse
    {
        public List<string> Ideas { get; set; } = new();
    }
}
