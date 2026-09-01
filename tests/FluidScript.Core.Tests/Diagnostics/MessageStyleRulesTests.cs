using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Tests.Diagnostics;

/// <summary>
/// Proves the style gate works while the registry it guards is still empty.
/// </summary>
/// <remarks>
/// A checker applied to nothing passes for the wrong reason. These synthetic descriptors are the
/// evidence that the first real message will actually be checked, rather than the gate being found
/// inert on the day it was supposed to catch something.
/// </remarks>
public sealed class MessageStyleRulesTests
{
    private static DiagnosticDescriptor Descriptor(string template, string code = "FS1302") =>
        new(code, DiagnosticSeverity.Error, template);

    [Fact]
    [Trait("Category", "Unit")]
    public void Violations_AWellFormedMessage_IsClean()
    {
        var descriptor = Descriptor(
            "Cannot add two temperatures. To offset by a difference, write '20C + 30 dK'.");

        Assert.Empty(MessageStyleRules.Violations(descriptor));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Violations_APlaceholderNameIsNotTreatedAsProse()
    {
        // FS1105's template names its placeholder 'token'. The word never reaches the user, and a
        // checker reading the template rather than the message would ban a message it is fine with.
        var descriptor = Descriptor(
            "'{token}' looks like a parameter but has no value. Write '{token}=' with a value.", "FS1105");

        Assert.Empty(MessageStyleRules.Violations(descriptor));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Violations_MessageWithNoFullStop_IsReported()
    {
        Assert.Contains(
            MessageStyleRules.Violations(Descriptor("Unexpected input here")),
            violation => violation.Contains("ends in a period", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Violations_AQuestionIsASentence()
    {
        // Rule 5's own wording is a question, so the sentence-ending check must accept one.
        Assert.Empty(MessageStyleRules.Violations(Descriptor("Did you mean 'power'?")));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Violations_Exclamation_IsReported()
    {
        Assert.Contains(
            MessageStyleRules.Violations(Descriptor("Invalid!")),
            violation => violation.Contains("no exclamation", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("You forgot the closing quote.")]
    [InlineData("You must declare a pump before connecting it.")]
    public void Violations_BlamingTheUser_IsReported(string template)
    {
        Assert.Contains(
            MessageStyleRules.Violations(Descriptor(template)),
            violation => violation.Contains("no blame", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Violations_Shouting_IsReported()
    {
        Assert.Contains(
            MessageStyleRules.Violations(Descriptor("UNEXPECTED INPUT.")),
            violation => violation.Contains("sentence case", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("The binder could not resolve this name.")]
    [InlineData("This token is not valid here.")]
    [InlineData("The residual did not converge.")]
    public void Violations_InternalVocabulary_IsReported(string template)
    {
        Assert.Contains(
            MessageStyleRules.Violations(Descriptor(template)),
            violation => violation.Contains("rule 6", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Violations_ABannedTermInsideAnotherWord_IsNotReported()
    {
        // 'null' inside 'annulled', 'enum' inside 'enumerated', 'trivia' inside 'trivial'. A
        // substring match would fire on ordinary English and get the rule switched off.
        Assert.Empty(MessageStyleRules.Violations(
            Descriptor("The enumerated layers are annulled by a trivial change.")));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Violations_TheInternalRangeIsExemptFromTheJargonRule()
    {
        // FS90xx messages are bug reports about the tool, not statements about the script, and they
        // are the one place internal vocabulary belongs.
        var descriptor = Descriptor(
            "Something went wrong inside FluidScript: an exception escaped a stage.", "FS9001");

        Assert.Empty(MessageStyleRules.Violations(descriptor));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Violations_DoubleSpacing_IsReported()
    {
        Assert.Contains(
            MessageStyleRules.Violations(Descriptor("Cannot read this  line.")),
            violation => violation.Contains("single spaces", StringComparison.Ordinal));
    }
}
