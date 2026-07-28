FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /src
COPY . .
RUN dotnet restore YouTubeProcessing.slnx
RUN dotnet publish src/Api/YouTubeProcessing.Api/YouTubeProcessing.Api.csproj \
    --no-restore --configuration Release --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10
WORKDIR /app
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "YouTubeProcessing.Api.dll"]
