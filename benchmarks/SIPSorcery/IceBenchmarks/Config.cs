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

namespace IceBenchmarks;

internal sealed class Config : ManualConfig
{
    private const string CurrentJobId = "Current";

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
                    .WithId(isBaseline ? version : CurrentJobId)
                    .WithBaseline(isBaseline));
            }
        }

        AddFilter(new SimpleFilter(static benchmark =>
        {
            var methodName = benchmark.Descriptor.WorkloadMethod.Name;
            var isCurrentOnly = methodName.EndsWith("WriteString", StringComparison.Ordinal);
            var isCurrentJob = string.Equals(CurrentJobId, benchmark.Job.Id, StringComparison.Ordinal);

            return !isCurrentOnly || isCurrentJob;
        }));

        WithOrderer(new RuntimeGroupedOrderer());

        AddExporter(BenchmarkDotNet.Exporters.MarkdownExporter.GitHub);

        AddColumnProvider(DefaultColumnProviders.Instance);

        HideColumns(Column.Arguments, Column.Error, Column.Median, Column.StdDev, Column.RatioSD);

        WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(int.MaxValue));

        AddDiagnoser(BenchmarkDotNet.Diagnosers.MemoryDiagnoser.Default);

        AddLogger(BenchmarkDotNet.Loggers.ConsoleLogger.Default);
    }

    private sealed class RuntimeGroupedOrderer : IOrderer
    {
        private static string GetMethodGroupKey(BenchmarkCase benchmarkCase)
        {
            var methodName = benchmarkCase.Descriptor.WorkloadMethod.Name;
            var underscoreIndex = methodName.IndexOf('_');
            return underscoreIndex >= 0 ? methodName[..underscoreIndex] : methodName;
        }

        private static string GetInputGroupKey(BenchmarkCase benchmarkCase)
            => benchmarkCase.Parameters.Items
                .FirstOrDefault(p => p.Name == "Input")
                ?.Value?.ToString() ?? string.Empty;

        private static string GetGroupKey(BenchmarkCase benchmarkCase)
            => $"{GetMethodGroupKey(benchmarkCase)}|{GetInputGroupKey(benchmarkCase)}";

        public IEnumerable<BenchmarkCase> GetExecutionOrder(
            ImmutableArray<BenchmarkCase> benchmarksCase,
            IEnumerable<BenchmarkLogicalGroupRule> order = null)
            => benchmarksCase;

        public IEnumerable<BenchmarkCase> GetSummaryOrder(
            ImmutableArray<BenchmarkCase> benchmarksCases,
            Summary summary)
            => benchmarksCases
                .OrderBy(GetMethodGroupKey)
                .ThenBy(GetInputGroupKey)
                .ThenBy(b => b.Job.Id)
                .ThenBy(b => b.Descriptor.WorkloadMethod.Name);

        public string GetHighlightGroupKey(BenchmarkCase benchmarkCase)
            => GetGroupKey(benchmarkCase);

        public string GetLogicalGroupKey(
            ImmutableArray<BenchmarkCase> allBenchmarksCases,
            BenchmarkCase benchmarkCase)
            => GetGroupKey(benchmarkCase);

        public IEnumerable<IGrouping<string, BenchmarkCase>> GetLogicalGroupOrder(
            IEnumerable<IGrouping<string, BenchmarkCase>> logicalGroups,
            IEnumerable<BenchmarkLogicalGroupRule> order = null)
            => logicalGroups.OrderBy(g => g.Key);

        public bool SeparateLogicalGroups => true;
    }
}
