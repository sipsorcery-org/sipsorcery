using BenchmarkDotNet.Running;
using PixelConverterBenchmarks;

BenchmarkRunner.Run(typeof(Program).Assembly, config: new Config());
