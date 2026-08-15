namespace Detent.Cli.Tests;

/// <summary>
/// Forces every test class that redirects <see cref="Console"/> via
/// <see cref="CliInvoker"/> into one xUnit collection, so they run
/// sequentially relative to each other rather than in parallel.
/// </summary>
/// <remarks>
/// xUnit parallelises different test classes by default, and
/// <c>Console.SetOut</c>/<c>SetError</c> are process-wide mutable state -
/// without this, two Console-redirecting classes running concurrently corrupt
/// each other's captured output. <see cref="CliInvoker"/>'s own remarks
/// warned this would be needed the moment a second such class existed; this is
/// that fix, found by the full suite failing while each class passed alone.
/// </remarks>
[CollectionDefinition(nameof(ConsoleTests), DisableParallelization = true)]
public sealed class ConsoleTests;
