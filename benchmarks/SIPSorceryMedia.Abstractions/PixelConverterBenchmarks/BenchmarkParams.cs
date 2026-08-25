namespace PixelConverterBenchmarks;

public class BenchmarkParams
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int Stride { get; init; }
    public required byte[] Bytes { get; init; }

    public override string ToString() => Width > 0 && Height > 0 ? $"({Width}, {Height})" : "()";
}
