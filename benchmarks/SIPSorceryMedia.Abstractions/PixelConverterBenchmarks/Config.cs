using System.Collections.Immutable;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

#nullable disable

namespace PixelConverterBenchmarks;

internal sealed class Config : ManualConfig
{
    private const string _currentJobId = "Current";

    public Config()
    {
        Runtime[] targetRuntimes = [CoreRuntime.Core10_0];
        string[] targetVersions = ["10.0.15", ""];

        foreach (var version in targetVersions)
        {
            var isBaseline = string.Equals(version, targetVersions[0]);

            foreach (var targetRuntime in targetRuntimes)
            {
                AddJob(Job.MediumRun
                    .WithRuntime(targetRuntime)
                    .WithMsBuildArguments($"/p:LibVersion={version}")
                    .WithId(isBaseline ? version : _currentJobId)
                    .WithBaseline(isBaseline)
                );
            }
        }

        AddFilter(new SimpleFilter(static benchmark =>
        {
            var isBufferWriter = benchmark.Descriptor.WorkloadMethod.Name.EndsWith("BufferWriter", StringComparison.Ordinal);

            var isBaselineJob = string.Equals(_currentJobId, benchmark.Job.Id, StringComparison.Ordinal);

            return !isBufferWriter || isBaselineJob;
        }));

        WithOrderer(new RuntimeGroupedOrderer());

        AddExporter(BenchmarkDotNet.Exporters.MarkdownExporter.GitHub);

        AddColumnProvider(BenchmarkDotNet.Columns.DefaultColumnProviders.Instance);
        HideColumns(Column.Arguments, Column.Error, Column.Median, Column.StdDev, Column.RatioSD);

        WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(int.MaxValue));

        AddDiagnoser(BenchmarkDotNet.Diagnosers.MemoryDiagnoser.Default);

        AddLogger(BenchmarkDotNet.Loggers.ConsoleLogger.Default);
    }

    private sealed class RuntimeGroupedOrderer : IOrderer
    {
        public IEnumerable<BenchmarkCase> GetExecutionOrder(
            ImmutableArray<BenchmarkCase> benchmarksCase,
            IEnumerable<BenchmarkLogicalGroupRule> order = null)
            => benchmarksCase;

        public IEnumerable<BenchmarkCase> GetSummaryOrder(
            ImmutableArray<BenchmarkCase> benchmarksCases,
            Summary summary)
            => benchmarksCases
                .OrderBy(b => b.Job.Environment.Runtime?.Name)
                .ThenBy(b => b.Descriptor.WorkloadMethod.Name)
                .ThenBy(b => b.Job.Id == _currentJobId ? 0 : 1)
                .ThenByDescending(b => b.Job.Id);

        public string GetHighlightGroupKey(BenchmarkCase benchmarkCase)
            => benchmarkCase.Job.Environment.Runtime?.Name;

        public string GetLogicalGroupKey(
            ImmutableArray<BenchmarkCase> allBenchmarksCases,
            BenchmarkCase benchmarkCase)
            => benchmarkCase.Job.Environment.Runtime?.Name;

        public IEnumerable<IGrouping<string, BenchmarkCase>> GetLogicalGroupOrder(
            IEnumerable<IGrouping<string, BenchmarkCase>> logicalGroups,
            IEnumerable<BenchmarkLogicalGroupRule> order = null)
            => logicalGroups.OrderBy(g => g.Key);

        public bool SeparateLogicalGroups => true;
    }
}
