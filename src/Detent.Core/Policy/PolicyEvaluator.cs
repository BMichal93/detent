using Detent.Core.Diff;

namespace Detent.Core.Policy;

/// <summary>
/// Turns findings plus a policy into an exit code: the last stage of the
/// capture, diff, policy pipeline.
/// </summary>
public static class PolicyEvaluator
{
    /// <summary>Partitions findings by outcome and decides the exit code.</summary>
    public static GateResult Evaluate(IReadOnlyList<Finding> findings, GatePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(policy);

        var failures = new List<Finding>();
        var warnings = new List<Finding>();
        var passed = new List<Finding>();

        foreach (var finding in findings)
        {
            // fail_on checked first: a severity listed in both sets, which is a
            // contradictory policy file but not this evaluator's job to reject,
            // fails rather than merely warns. Failing is the stronger claim.
            if (policy.FailOn.Contains(finding.Severity))
            {
                failures.Add(finding);
            }
            else if (policy.WarnOn.Contains(finding.Severity))
            {
                warnings.Add(finding);
            }
            else
            {
                passed.Add(finding);
            }
        }

        return new GateResult
        {
            ExitCode = failures.Count > 0 ? ExitCode.PolicyViolation : ExitCode.Pass,
            Failures = failures,
            Warnings = warnings,
            Passed = passed,
        };
    }
}

/// <summary>The outcome of evaluating a diff's findings against a policy.</summary>
public sealed record GateResult
{
    public required ExitCode ExitCode { get; init; }

    /// <summary>Findings whose severity is in the policy's <c>fail_on</c> set.</summary>
    public required IReadOnlyList<Finding> Failures { get; init; }

    /// <summary>Findings whose severity is in the policy's <c>warn_on</c> set.</summary>
    public required IReadOnlyList<Finding> Warnings { get; init; }

    /// <summary>
    /// Findings in neither set: additive by default, shown but never blocking.
    /// A cosmetic finding is still passed through here: hiding it is a
    /// rendering choice, per diff-rules.md §2, not something the policy
    /// evaluator decides.
    /// </summary>
    public required IReadOnlyList<Finding> Passed { get; init; }
}
