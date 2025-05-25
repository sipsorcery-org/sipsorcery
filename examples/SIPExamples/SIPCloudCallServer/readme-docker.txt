C:\dev\sipsorcery\src> dotnet pack SIPSorcery.csproj --configuration Debug --output c:\dev\local-nuget

docker build -t sipsorcery/sipcloudcallserver:0.10 .
docker push sipsorcery/sipcloudcallserver:0.10
