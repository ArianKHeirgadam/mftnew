using Mft.Domain.Exams;
using Xunit;

namespace Mft.Tests;

public sealed class ExamScoringTests
{
    [Fact]
    public void MultipleChoice_CorrectAnswer_GetsFullPoints()
    {
        Assert.Equal(2.50m, ExamScoring.CalculateMultipleChoice(2.50m, true, true, false, null));
    }

    [Fact]
    public void MultipleChoice_WrongAnswer_WithoutNegativeMarking_GetsZero()
    {
        Assert.Equal(0m, ExamScoring.CalculateMultipleChoice(2m, true, false, false, 0.5m));
    }

    [Fact]
    public void MultipleChoice_EmptyAnswer_AlwaysGetsZero()
    {
        Assert.Equal(0m, ExamScoring.CalculateMultipleChoice(2m, false, false, true, 0.5m));
    }

    [Fact]
    public void MultipleChoice_WrongAnswer_WithNegativeMarking_GetsConfiguredPenalty()
    {
        Assert.Equal(-0.50m, ExamScoring.CalculateMultipleChoice(2m, true, false, true, 0.5m));
    }

    [Fact]
    public void SectionTotal_NeverFallsBelowZero()
    {
        Assert.Equal(0m, ExamScoring.CalculateSectionTotal(new[] { -0.75m, 0.25m }));
    }

    [Fact]
    public void FinalTotal_NeverFallsBelowZero()
    {
        Assert.Equal(0m, ExamScoring.CalculateFinalTotal(new[] { -2m, 1m }));
    }

    [Fact]
    public void Matching_NoCorrectPairs_GetsZero()
    {
        var correctMap = new Dictionary<long, long> { [10] = 101, [20] = 102, [30] = 103 };
        var selections = new[] {
            new MatchingPairSelection(10, 102),
            new MatchingPairSelection(20, 103),
            new MatchingPairSelection(30, 101)
        };

        Assert.Equal(0m, ExamScoring.CalculateMatching(3m, correctMap, selections));
    }

    [Fact]
    public void Matching_AllCorrectPairs_GetsFullPoints()
    {
        var correctMap = new Dictionary<long, long> { [10] = 101, [20] = 102, [30] = 103 };
        var selections = new[] {
            new MatchingPairSelection(10, 101),
            new MatchingPairSelection(20, 102),
            new MatchingPairSelection(30, 103)
        };

        Assert.Equal(3m, ExamScoring.CalculateMatching(3m, correctMap, selections));
    }

    [Fact]
    public void Matching_PartialCorrectPairs_GetsProportionalPoints()
    {
        var correctMap = new Dictionary<long, long> { [10] = 101, [20] = 102, [30] = 103 };
        var selections = new[] {
            new MatchingPairSelection(10, 101),
            new MatchingPairSelection(20, 103),
            new MatchingPairSelection(30, 102)
        };

        Assert.Equal(0.33m, ExamScoring.CalculateMatching(1m, correctMap, selections));
    }

    [Fact]
    public void Matching_RejectsRepeatedLeftOrRightIds()
    {
        var correctMap = new Dictionary<long, long> { [10] = 101, [20] = 102 };
        var repeatedLeft = new[] {
            new MatchingPairSelection(10, 101),
            new MatchingPairSelection(10, 102)
        };
        var repeatedRight = new[] {
            new MatchingPairSelection(10, 101),
            new MatchingPairSelection(20, 101)
        };

        Assert.Throws<ArgumentException>(() => ExamScoring.CalculateMatching(2m, correctMap, repeatedLeft));
        Assert.Throws<ArgumentException>(() => ExamScoring.CalculateMatching(2m, correctMap, repeatedRight));
    }

    [Fact]
    public void Matching_RejectsIdsFromAnotherQuestionOrAttempt()
    {
        var correctMap = new Dictionary<long, long> { [10] = 101, [20] = 102 };

        Assert.Throws<ArgumentException>(() => ExamScoring.CalculateMatching(
            1m, correctMap, new[] { new MatchingPairSelection(999, 101) }));

        Assert.Throws<ArgumentException>(() => ExamScoring.CalculateMatching(
            1m, correctMap, new[] { new MatchingPairSelection(10, 999) }));
    }

    [Fact]
    public void Matching_RejectsDuplicateRightIdsInCorrectMap()
    {
        var invalidMap = new Dictionary<long, long> { [10] = 101, [20] = 101 };
        Assert.Throws<ArgumentException>(() => ExamScoring.CalculateMatching(
            2m, invalidMap, Array.Empty<MatchingPairSelection>()));
    }


    [Fact]
    public void Matching_EmptySelection_GetsZero()
    {
        var correctMap = new Dictionary<long, long> { [10] = 101, [20] = 102 };
        Assert.Equal(0m, ExamScoring.CalculateMatching(
            2m, correctMap, Array.Empty<MatchingPairSelection>()));
    }

    [Fact]
    public void Matching_OneCorrectPair_RoundsAwayFromZero()
    {
        var correctMap = new Dictionary<long, long> { [10] = 101, [20] = 102 };
        var selections = new[] { new MatchingPairSelection(10, 101) };
        Assert.Equal(0.01m, ExamScoring.CalculateMatching(0.01m, correctMap, selections));
    }

    [Fact]
    public void RoundScore_UsesAwayFromZeroToTwoDecimals()
    {
        Assert.Equal(1.01m, ExamScoring.RoundScore(1.005m));
        Assert.Equal(-1.01m, ExamScoring.RoundScore(-1.005m));
    }
}
