namespace Conclave.Deliberation;

public interface ITerminationStrategy
{
    string Name { get; }
    Task<TerminationDecision> ShouldTerminateAsync(
        DeliberationState state,
        CancellationToken cancellationToken = default);
}

public class TerminationDecision
{
    public bool ShouldTerminate { get; init; }
    public TerminationReason Reason { get; init; }
    public string? Explanation { get; init; }
    public double Confidence { get; init; } = 1.0;

    public static TerminationDecision Continue() => new()
    {
        ShouldTerminate = false,
        Reason = TerminationReason.ManualStop,
        Confidence = 1.0
    };

    public static TerminationDecision Terminate(TerminationReason reason, string? explanation = null) => new()
    {
        ShouldTerminate = true,
        Reason = reason,
        Explanation = explanation,
        Confidence = 1.0
    };

    public static TerminationDecision TerminateWithConfidence(
        TerminationReason reason,
        double confidence,
        string? explanation = null) => new()
    {
        ShouldTerminate = true,
        Reason = reason,
        Explanation = explanation,
        Confidence = confidence
    };
}
