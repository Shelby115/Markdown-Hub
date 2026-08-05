FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY MarkdownHub.Api.csproj .
RUN dotnet restore MarkdownHub.Api.csproj
COPY . .
RUN dotnet publish MarkdownHub.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .

# Persistent storage mount points - see docker-compose.yml volumes
RUN mkdir -p /data/markdown /data/backups /data/db
VOLUME ["/data/markdown", "/data/backups", "/data/db"]

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MarkdownHub.Api.dll"]
