# Stage 1: build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore as distinct layers for caching
COPY *.csproj ./
COPY local-nuget/ ./local-nuget/

RUN dotnet restore --source ./local-nuget --source https://api.nuget.org/v3/index.json 

# Copy rest of source and publish
COPY . .
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: runtime
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app

# Copy published app
COPY --from=build /app/publish ./

# Expose SIP ports
EXPOSE 5060/udp
EXPOSE 5061/tcp

# Run the server
ENTRYPOINT ["dotnet", "SIPCloudCallServer.dll"]
