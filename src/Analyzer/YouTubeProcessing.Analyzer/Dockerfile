FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /src
COPY . .
RUN dotnet restore YouTubeProcessing.slnx
RUN dotnet publish src/Analyzer/YouTubeProcessing.Analyzer/YouTubeProcessing.Analyzer.csproj \
    --no-restore --configuration Release --output /app

FROM node:24.7.0-bookworm-slim AS codex
RUN npm install --global @openai/codex@0.147.0

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10
WORKDIR /app
COPY --from=build /app .
COPY --from=codex /usr/local/bin/node /usr/local/bin/node
COPY --from=codex /usr/local/lib/node_modules/@openai /usr/local/lib/node_modules/@openai
ENV CODEX_HOME=/var/lib/codex
RUN ln -s /usr/local/lib/node_modules/@openai/codex/bin/codex.js /usr/local/bin/codex \
    && mkdir -p "$CODEX_HOME" \
    && chown "$APP_UID:$APP_UID" "$CODEX_HOME"
USER $APP_UID
ENTRYPOINT ["dotnet", "YouTubeProcessing.Analyzer.dll"]
