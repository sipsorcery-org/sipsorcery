FROM mcr.microsoft.com/dotnet/core/runtime:3.1

COPY bin/Debug/netcoreapp3.1/publish/ App/
WORKDIR /App
ENTRYPOINT ["dotnet", "sipcmdline.dll"]