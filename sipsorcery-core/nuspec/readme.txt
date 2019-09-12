==> To build SIPSorcery nupkg
1. Update versions in:
 C:\Dev\sipsorcery\sipsorcery-public\sipsorcery-core\SIPSorcery.Net\AssemblyInfo.cpp
 C:\Dev\sipsorcery\sipsorcery-public\sipsorcery-core\SIPSorcery.SIP.App\AssemblyInfo.cpp
 C:\Dev\sipsorcery\sipsorcery-public\sipsorcery-core\SIPSorcery.SIP.Core\AssemblyInfo.cpp
 C:\Dev\sipsorcery\sipsorcery-public\sipsorcery-core\SIPSorcery.SIP.Sys\AssemblyInfo.cpp
2. Build SIPSorcery-Core Release AnyCPU build:
 c:\Dev\sipsorcery\sipsorcery-public\sipsorcery-core> msbuild /m SIPSorcery-Core.sln /p:Configuration=Release /p:Platform="Any CPU" /t:clean,build
3. Update version, releaseNotes, copyright date in C:\Dev\sipsorcery\sipsorcery-public\sipsorcery-core\nuspec\SIPSorcery.nuspec
4. Pack the nuget package: c:\Dev\sipsorcery\sipsorcery-public\sipsorcery-core\nuspec> c:\tools\nuget pack SIPSorcery.nuspec
5. Test install of package in the SIPSorcery-SoftPhone sample project, in the nuget package manager console: 
 Uninstall-Package SIPSorcery
 Install-Package SIPSorcery -Source ..\sipsorcery-core\nuspec
6. Publish to nuget.org:
 c:\tools\nuget setApiKey Your-API-Key
 c:\tools\nuget push SIPSorcery.2.0.0.nupkg -Source https://api.nuget.org/v3/index.json