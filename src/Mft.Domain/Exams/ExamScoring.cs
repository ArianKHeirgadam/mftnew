namespace Mft.Domain.Exams;

/// <summary>
/// Stable IDs submitted by the client for one matching answer.
/// LeftPairId identifies the left item; RightPairId identifies the selected right item.
/// </summary>
public readonly record struct MatchingPairSelection(long LeftPairId, long RightPairId);

/// <summary>
/// Pure scoring rules shared by the API and automated tests.
/// No EF Core or database dependencies are allowed here.
/// </summary>
public static class ExamScoring
{
    public static decimal RoundScore(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal CalculateMultipleChoice(
        decimal questionPoints,
        bool hasAnswer,
        bool isCorrect,
        bool negativeMarkingEnabled,
        decimal? negativeMarkingPoints)
    {
        if (questionPoints < 0m)
            throw new ArgumentOutOfRangeException(nameof(questionPoints));

        if (!hasAnswer)
            return 0m;

        if (isCorrect)
            return RoundScore(questionPoints);

        if (!negativeMarkingEnabled)
            return 0m;

        var penalty = negativeMarkingPoints.GetValueOrDefault();
        if (penalty < 0m)
            throw new ArgumentOutOfRangeException(nameof(negativeMarkingPoints));

        return -RoundScore(penalty);
    }

    public static decimal CalculateMatching(
        decimal questionPoints,
        IReadOnlyCollection<long> validPairIds,
        IReadOnlyCollection<MatchingPairSelection> selections)
    {
        ArgumentNullException.ThrowIfNull(validPairIds);
        ArgumentNullException.ThrowIfNull(selections);

        if (questionPoints < 0m)
            throw new ArgumentOutOfRangeException(nameof(questionPoints));

        if (validPairIds.Count == 0 || selections.Count == 0)
            return 0m;

        var validIds = validPairIds.ToHashSet();
        if (validIds.Count != validPairIds.Count)
            throw new ArgumentException("Matching pair IDs must be unique.", nameof(validPairIds));

        if (selections.Any(x => !validIds.Contains(x.LeftPairId) || !validIds.Contains(x.RightPairId)))
            throw new ArgumentException("A matching pair ID does not belong to this question.", nameof(selections));

        if (selections.Select(x => x.LeftPairId).Distinct().Count() != selections.Count)
            throw new ArgumentException("A left matching item can only be used once.", nameof(selections));

        if (selections.Select(x => x.RightPairId).Distinct().Count() != selections.Count)
            throw new ArgumentException("A right matching item can only be used once.", nameof(selections));

        var correctCount = selections.Count(x => x.LeftPairId == x.RightPairId);
        return RoundScore(questionPoints * correctCount / validIds.Count);
    }

    /// <summary>Clamps the sum of all question scores in one section to zero or above.</summary>
    public static decimal CalculateSectionTotal(IEnumerable<decimal> questionScores)
    {
        ArgumentNullException.ThrowIfNull(questionScores);
        return RoundScore(Math.Max(0m, questionScores.Sum()));
    }

    public static decimal CalculateFinalTotal(IEnumerable<decimal> sectionTotals)
    {
        ArgumentNullException.ThrowIfNull(sectionTotals);
        return RoundScore(sectionTotals.Sum());
    }
}
