namespace IdeorAI.Model
{
    public class ContentRequest
    {
        public Content[] contents { get; set; }
        public GenerationConfig? generationConfig { get; set; }
    }
    public class Content
    {
        public Part[] parts { get; set; }
    }
    public class Part
    {
        public string text { get; set; }
    }
    public class GenerationConfig
    {
        public int? maxOutputTokens { get; set; }
        public ThinkingConfig? thinkingConfig { get; set; }
    }
    public class ThinkingConfig
    {
        public int thinkingBudget { get; set; }
    }
}