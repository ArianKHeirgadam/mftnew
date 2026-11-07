namespace Mft.Domain.Exams;

/// <summary>
/// IDs supplied for one matching answer. RightItemId is an opaque, attempt-specific
/// right-side identifier; it must not be the database ID that reveals the answer key.
/// </summary>
public readonly record struct MatchingPairSelection(long LeftItemId, long RightItemId);

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

    /// <summary>
    /// Scores matching by stable left IDs and opaque right-side IDs.
    /// The mapping is generated for the current attempt after the right side is shuffled.
    /// </summary>
    public static decimal CalculateMatching(
        decimal questionPoints,
        IReadOnlyDictionary<long, long> correctRightIdByLeftId,
        IReadOnlyCollection<MatchingPairSelection> selections)
    {
        ArgumentNullException.ThrowIfNull(correctRightIdByLeftId);
        ArgumentNullException.ThrowIfNull(selections);

        if (questionPoints < 0m)
            throw new ArgumentOutOfRangeException(nameof(questionPoints));

        var validLeftIds = correctRightIdByLeftId.Keys.ToHashSet();
        var validRightIds = correctRightIdByLeftId.Values.ToHashSet();

        if (validRightIds.Count != correctRightIdByLeftId.Count)
            throw new ArgumentException("Right-side matching IDs must be unique.", nameof(correctRightIdByLeftId));

        if (correctRightIdByLeftId.Count == 0 || selections.Count == 0)
            return 0m;

        if (selections.Any(x => !validLeftIds.Contains(x.LeftItemId) || !validRightIds.Contains(x.RightItemId)))
            throw new ArgumentException("A matching item ID does not belong to this question and attempt.", nameof(selections));

        if (selections.Select(x => x.LeftItemId).Distinct().Count() != selections.Count)
            throw new ArgumentException("A left matching item can only be used once.", nameof(selections));

        if (selections.Select(x => x.RightItemId).Distinct().Count() != selections.Count)
            throw new ArgumentException("A right matching item can only be used once.", nameof(selections));

        var correctCount = selections.Count(x =>
            correctRightIdByLeftId[x.LeftItemId] == x.RightItemId);

        return RoundScore(questionPoints * correctCount / correctRightIdByLeftId.Count);
    }

    /// <summary>Clamps the sum of all question scores in one section to zero or above.</summary>
    public static decimal CalculateSectionTotal(IEnumerable<decimal> questionScores)
    {
        ArgumentNullException.ThrowIfNull(questionScores);
        return RoundScore(Math.Max(0m, questionScores.Sum()));
    }

    /// <summary>Final exam scores cannot be negative, even if section inputs are malformed.</summary>
    public static decimal CalculateFinalTotal(IEnumerable<decimal> sectionTotals)
    {
        ArgumentNullException.ThrowIfNull(sectionTotals);
        return RoundScore(Math.Max(0m, sectionTotals.Sum()));
    }
}
