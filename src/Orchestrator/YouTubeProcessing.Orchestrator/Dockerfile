FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /src
COPY . .
RUN dotnet restore YouTubeProcessing.slnx
RUN dotnet publish src/Orchestrator/YouTubeProcessing.Orchestrator/YouTubeProcessing.Orchestrator.csproj \
    --no-restore --configuration Release --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "YouTubeProcessing.Orchestrator.dll"]
