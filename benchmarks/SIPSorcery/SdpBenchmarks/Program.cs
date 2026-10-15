using BenchmarkDotNet.Running;
using SdpBenchmarks;

BenchmarkRunner.Run(typeof(Program).Assembly, config: new Config());
