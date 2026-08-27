namespace IceBenchmarks;

public sealed record BenchmarkInput(string Name, string Value)
{
    public override string ToString() => Name;
}
