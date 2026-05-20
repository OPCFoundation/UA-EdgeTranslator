namespace Opc.Ua.Edge.Translator.Tests
{
    using Xunit;

    /// <summary>
    /// xUnit collection that serializes every test which mutates the
    /// process-global current working directory via
    /// <see cref="TestWorkingDirectory"/>. Without this, parallel xUnit test
    /// classes can race on <c>Directory.SetCurrentDirectory</c> and produce
    /// flaky results (e.g. one class deletes another class's temp tree before
    /// the second class finishes reading from it).
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class WorkingDirectoryCollection
    {
        public const string Name = "WorkingDirectory";
    }
}
