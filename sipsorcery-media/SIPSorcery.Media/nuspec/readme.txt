==> To build SIPSorceryMedia nupkg
1. Update version in C:\Dev\sipsorcery\sipsorcery-public\sipsorcery-media\SIPSorcery.Media\AssemblyInfo.cpp
2. Build SIPSorceryMedia x86 and x64 release builds:
 c:\Dev\sipsorcery\sipsorcery-public\sipsorcery-media\SIPSorcery.Media>msbuild /m SIPSorceryMedia.sln /p:Configuration=Release /p:Platform=Win32 /t:clean,build
 c:\Dev\sipsorcery\sipsorcery-public\sipsorcery-media\SIPSorcery.Media>msbuild /m SIPSorceryMedia.sln /p:Configuration=Release /p:Platform=x64 /t:clean,build
3. Update version, releaseNotes, copyright date in  C:\Dev\sipsorcery\sipsorcery-public\sipsorcery-media\SIPSorcery.Media\nuspec\SIPSorceryMedia.nuspec
4. Pack the nuget package: c:\Dev\sipsorcery\sipsorcery-public\sipsorcery-media\SIPSorcery.Media\nuspec>c:\tools\nuget pack SIPSorceryMedia.nuspec
5. Test install of package in the WebRtcDaemon sample project, in the nuget package manager console: 
 Uninstall-Package SIPSorceryMedia
 Install-Package SIPSorceryMedia -Source ..\..\SIPSorcery.Media\nuspec
6. Publish to nuget.org:
 c:\tools\nuget setApiKey Your-API-Key
 c:\tools\nuget push SIPSorceryMedia.2.0.0.nupkg -Source https://api.nuget.org/v3/index.json 