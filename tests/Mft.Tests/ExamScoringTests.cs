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
    public void Matching_NoCorrectPairs_GetsZero()
    {
        var score = ExamScoring.CalculateMatching(
            3m,
            new long[] { 10, 20, 30 },
            new[] {
                new MatchingPairSelection(10, 20),
                new MatchingPairSelection(20, 30),
                new MatchingPairSelection(30, 10)
            });

        Assert.Equal(0m, score);
    }

    [Fact]
    public void Matching_AllCorrectPairs_GetsFullPoints()
    {
        var score = ExamScoring.CalculateMatching(
            3m,
            new long[] { 10, 20, 30 },
            new[] {
                new MatchingPairSelection(10, 10),
                new MatchingPairSelection(20, 20),
                new MatchingPairSelection(30, 30)
            });

        Assert.Equal(3m, score);
    }

    [Fact]
    public void Matching_PartialCorrectPairs_GetsProportionalPoints()
    {
        var score = ExamScoring.CalculateMatching(
            1m,
            new long[] { 10, 20, 30 },
            new[] {
                new MatchingPairSelection(10, 10),
                new MatchingPairSelection(20, 30),
                new MatchingPairSelection(30, 20)
            });

        Assert.Equal(0.33m, score);
    }

    [Fact]
    public void Matching_RejectsRepeatedLeftOrRightIds()
    {
        var repeatedLeft = new[] {
            new MatchingPairSelection(10, 10),
            new MatchingPairSelection(10, 20)
        };
        var repeatedRight = new[] {
            new MatchingPairSelection(10, 20),
            new MatchingPairSelection(20, 20)
        };

        Assert.Throws<ArgumentException>(() =>
            ExamScoring.CalculateMatching(2m, new long[] { 10, 20 }, repeatedLeft));
        Assert.Throws<ArgumentException>(() =>
            ExamScoring.CalculateMatching(2m, new long[] { 10, 20 }, repeatedRight));
    }

    [Fact]
    public void Matching_RejectsIdsFromAnotherQuestion()
    {
        Assert.Throws<ArgumentException>(() =>
            ExamScoring.CalculateMatching(
                1m,
                new long[] { 10, 20 },
                new[] { new MatchingPairSelection(10, 999) }));
    }

    [Fact]
    public void RoundScore_UsesAwayFromZeroToTwoDecimals()
    {
        Assert.Equal(1.01m, ExamScoring.RoundScore(1.005m));
        Assert.Equal(-1.01m, ExamScoring.RoundScore(-1.005m));
    }
}
