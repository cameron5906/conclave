namespace Conclave.Samples.CodeReview.Models;

public class CodeReviewResult
{
    public string Summary { get; set; } = string.Empty;
    public OverallAssessment Overall { get; set; } = new();
    public List<ReviewFinding> Findings { get; set; } = new();
    public List<string> Strengths { get; set; } = new();
    public List<string> SuggestedImprovements { get; set; } = new();
    public bool ApprovalRecommended { get; set; }
    public int ConfidenceScore { get; set; }
}

public class OverallAssessment
{
    public int CodeQuality { get; set; }
    public int Security { get; set; }
    public int Performance { get; set; }
    public int Maintainability { get; set; }
    public int TestCoverage { get; set; }
}

public class ReviewFinding
{
    public string Severity { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
}
