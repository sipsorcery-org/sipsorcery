using CppSharp;
using CppSharp.AST;
using CppSharp.Generators;

namespace Vpx.AutoGen
{
    public class VpxGenerator : ILibrary
    {
        public void Postprocess(Driver driver, ASTContext ctx)
        {
            //throw new NotImplementedException();
        }

        public void Preprocess(Driver driver, ASTContext ctx)
        {
            //throw new NotImplementedException();
        }

        public void Setup(Driver driver)
        {
            var options = driver.Options;
            options.GeneratorKind = GeneratorKind.CSharp;
            var module = options.AddModule("vpxmd");
            module.IncludeDirs.Add(@"C:\Dev\github\libvpx\vpx\");
            module.Headers.Add("vpx_encoder.h");
            module.Headers.Add("vpx_decoder.h");
            module.Headers.Add("vp8cx.h");
            module.Headers.Add("vp8dx.h");
            module.LibraryDirs.Add(@"C:\Dev\sipsorcery\SIPSorceryMedia.Windows\lib\x64");
            module.Libraries.Add("vpxmd.dll");
        }

        public void SetupPasses(Driver driver)
        {
            //throw new NotImplementedException();
        }
    }
}
