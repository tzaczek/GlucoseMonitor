using FluentAssertions;
using GlucoseAPI.Domain.Services;
using Xunit;

namespace GlucoseAPI.Tests.Domain;

/// <summary>
/// Unit tests for <see cref="ClassificationParser"/>.
/// Pure domain logic — no I/O or mocks.
/// </summary>
public class ClassificationParserTests
{
    [Fact]
    public void Parse_GreenClassification_ExtractsCorrectly()
    {
        var raw = "[CLASSIFICATION: green]\nThis was a well-controlled glucose response.";

        var (analysis, classification) = ClassificationParser.Parse(raw);

        classification.Should().Be("green");
        analysis.Should().Be("This was a well-controlled glucose response.");
    }

    [Fact]
    public void Parse_YellowClassification_ExtractsCorrectly()
    {
        var raw = "[CLASSIFICATION: yellow]\nSome concerning glucose patterns observed.";

        var (analysis, classification) = ClassificationParser.Parse(raw);

        classification.Should().Be("yellow");
        analysis.Should().Be("Some concerning glucose patterns observed.");
    }

    [Fact]
    public void Parse_RedClassification_ExtractsCorrectly()
    {
        var raw = "[CLASSIFICATION: red]\nSignificant glucose spike detected.";

        var (analysis, classification) = ClassificationParser.Parse(raw);

        classification.Should().Be("red");
        analysis.Should().Be("Significant glucose spike detected.");
    }

    [Fact]
    public void Parse_CaseInsensitive_ExtractsCorrectly()
    {
        var raw = "[CLASSIFICATION: GREEN]\nWell controlled.";

        var (analysis, classification) = ClassificationParser.Parse(raw);

        classification.Should().Be("green");
        analysis.Should().Be("Well controlled.");
    }

    [Fact]
    public void Parse_NoClassification_ReturnsNullClassification()
    {
        var raw = "This is just analysis text without a classification tag.";

        var (analysis, classification) = ClassificationParser.Parse(raw);

        classification.Should().BeNull();
        analysis.Should().Be(raw);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyWithNullClassification()
    {
        var (analysis, classification) = ClassificationParser.Parse("");

        classification.Should().BeNull();
        analysis.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NullString_ReturnsEmptyWithNullClassification()
    {
        var (analysis, classification) = ClassificationParser.Parse(null!);

        classification.Should().BeNull();
        analysis.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ClassificationWithExtraSpaces_HandledCorrectly()
    {
        var raw = "  [CLASSIFICATION:   green]  \nAnalysis text here.";

        var (analysis, classification) = ClassificationParser.Parse(raw);

        classification.Should().Be("green");
        analysis.Should().Be("Analysis text here.");
    }

    [Fact]
    public void Parse_ClassificationInMiddle_NotExtracted()
    {
        // Classification must be at the beginning
        var raw = "Some text before\n[CLASSIFICATION: green]\nAnalysis text.";

        var (analysis, classification) = ClassificationParser.Parse(raw);

        classification.Should().BeNull();
        analysis.Should().Be(raw);
    }

    [Fact]
    public void Parse_InvalidClassification_NotExtracted()
    {
        var raw = "[CLASSIFICATION: blue]\nSome analysis.";

        var (analysis, classification) = ClassificationParser.Parse(raw);

        classification.Should().BeNull();
        analysis.Should().Be(raw);
    }

    [Fact]
    public void Parse_MultilineAnalysis_PreservesFormatting()
    {
        var raw = "[CLASSIFICATION: green]\nLine 1.\n\nLine 2.\n\n**Bold text** and more.";

        var (analysis, classification) = ClassificationParser.Parse(raw);

        classification.Should().Be("green");
        analysis.Should().Contain("Line 1.");
        analysis.Should().Contain("Line 2.");
        analysis.Should().Contain("**Bold text**");
    }
}
