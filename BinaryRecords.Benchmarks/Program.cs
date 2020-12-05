using System;
using BenchmarkDotNet.Running;

namespace BinaryRecords.Benchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            BenchmarkRunner.Run<Benchmarks>();
        }
    }
}