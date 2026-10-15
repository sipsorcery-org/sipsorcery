using BenchmarkDotNet.Running;
using IceBenchmarks;

BenchmarkRunner.Run(typeof(Program).Assembly, config: new Config(), args);
