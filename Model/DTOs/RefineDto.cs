namespace IdeorAI.Model.DTOs;

public record RefineRequest(
    string ProjectId,
    string StageContent,
    string UserFeedback,
    string StageName
);

public record RefineResponse(
    Dictionary<string, string> ChangedSections
);

public record RefineErrorResponse(string Error, string Raw);

public record RefineSectionRequest(
    string ProjectId,
    string StageName,
    string SectionKey,
    string SectionTitle,
    string SectionContent,
    string UserFeedback
);

public record RefineSectionResponse(string RefinedContent);
