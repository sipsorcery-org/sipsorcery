using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CppSharp;

namespace OpenH264.AutoGen
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("OpenH264 C# bindings auto-generator.");
            ConsoleDriver.Run(new OpenH264Generator());
            Console.WriteLine("Finished.");
            Console.ReadLine();
        }
    }
}
