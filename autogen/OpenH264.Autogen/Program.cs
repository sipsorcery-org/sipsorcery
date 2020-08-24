using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CppSharp;

namespace Vpx.AutoGen
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("libvpx C# bindings auto-generator.");
            ConsoleDriver.Run(new VpxGenerator());
            Console.WriteLine("Finished.");
            Console.ReadLine();
        }
    }
}
