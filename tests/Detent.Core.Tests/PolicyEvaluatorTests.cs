using Detent.Core.Diff;
using Detent.Core.Policy;

namespace Detent.Core.Tests;

public sealed class PolicyEvaluatorTests
{
    private static Finding Finding(Severity severity, string id = "MCPC000") => new()
    {
        Id = id,
        Severity = severity,
        Path = "tools/x",
        Message = "test finding",
    };

    [Fact]
    public void Default_policy_fails_on_breaking_and_security()
    {
        var result = PolicyEvaluator.Evaluate(
            [Finding(Severity.Breaking), Finding(Severity.Security)],
            GatePolicy.Default);

        Assert.Equal(ExitCode.PolicyViolation, result.ExitCode);
        Assert.Equal(2, result.Failures.Count);
    }

    [Fact]
    public void Default_policy_warns_but_does_not_fail_on_behavioural()
    {
        var result = PolicyEvaluator.Evaluate([Finding(Severity.Behavioural)], GatePolicy.Default);

        Assert.Equal(ExitCode.Pass, result.ExitCode);
        Assert.Single(result.Warnings);
        Assert.Empty(result.Failures);
    }

    [Theory]
    [InlineData(Severity.Notice)]
    [InlineData(Severity.Unanalysable)]
    public void Default_policy_warns_on_notice_and_unanalysable(Severity severity)
    {
        var result = PolicyEvaluator.Evaluate([Finding(severity)], GatePolicy.Default);

        Assert.Equal(ExitCode.Pass, result.ExitCode);
        Assert.Single(result.Warnings);
    }

    [Theory]
    [InlineData(Severity.Additive)]
    [InlineData(Severity.Cosmetic)]
    public void Default_policy_passes_additive_and_cosmetic_without_warning(Severity severity)
    {
        var result = PolicyEvaluator.Evaluate([Finding(severity)], GatePolicy.Default);

        Assert.Equal(ExitCode.Pass, result.ExitCode);
        Assert.Empty(result.Warnings);
        Assert.Empty(result.Failures);
        Assert.Single(result.Passed);
    }

    [Fact]
    public void No_findings_at_all_passes()
    {
        var result = PolicyEvaluator.Evaluate([], GatePolicy.Default);

        Assert.Equal(ExitCode.Pass, result.ExitCode);
        Assert.Empty(result.Failures);
        Assert.Empty(result.Warnings);
        Assert.Empty(result.Passed);
    }

    /// <summary>
    /// A user's fail_on can widen the default, e.g. failing the build on any
    /// behavioural change for a consumer that cannot tolerate one.
    /// </summary>
    [Fact]
    public void Custom_policy_can_fail_on_a_normally_warn_only_severity()
    {
        var policy = new GatePolicy
        {
            FailOn = new HashSet<Severity> { Severity.Behavioural },
            WarnOn = new HashSet<Severity>(),
        };

        var result = PolicyEvaluator.Evaluate([Finding(Severity.Behavioural)], policy);

        Assert.Equal(ExitCode.PolicyViolation, result.ExitCode);
        Assert.Single(result.Failures);
    }

    /// <summary>
    /// A severity listed in both sets is a contradictory policy file, but the
    /// evaluator does not reject it - it resolves the contradiction toward the
    /// stronger claim rather than picking arbitrarily based on set order.
    /// </summary>
    [Fact]
    public void A_severity_in_both_sets_fails_rather_than_warns()
    {
        var policy = new GatePolicy
        {
            FailOn = new HashSet<Severity> { Severity.Breaking },
            WarnOn = new HashSet<Severity> { Severity.Breaking },
        };

        var result = PolicyEvaluator.Evaluate([Finding(Severity.Breaking)], policy);

        Assert.Equal(ExitCode.PolicyViolation, result.ExitCode);
        Assert.Single(result.Failures);
        Assert.Empty(result.Warnings);
    }

    /// <summary>
    /// One breaking finding among many passing ones still fails the whole
    /// build. Policy is evaluated over the set, not averaged.
    /// </summary>
    [Fact]
    public void A_single_failure_among_many_passes_still_fails_the_build()
    {
        var findings = Enumerable.Range(0, 20)
            .Select(_ => Finding(Severity.Additive))
            .Append(Finding(Severity.Breaking))
            .ToList();

        var result = PolicyEvaluator.Evaluate(findings, GatePolicy.Default);

        Assert.Equal(ExitCode.PolicyViolation, result.ExitCode);
        Assert.Single(result.Failures);
        Assert.Equal(20, result.Passed.Count);
    }

    [Fact]
    public void Every_finding_is_accounted_for_exactly_once()
    {
        var findings = Enum.GetValues<Severity>().Select(s => Finding(s)).ToList();

        var result = PolicyEvaluator.Evaluate(findings, GatePolicy.Default);

        var total = result.Failures.Count + result.Warnings.Count + result.Passed.Count;
        Assert.Equal(findings.Count, total);
    }
}
