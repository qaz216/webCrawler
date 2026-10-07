# Build context: repository root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first, from project files only, so the layer is cached until dependencies change.
COPY global.json Directory.Build.props ./
COPY src/Crawler.Domain/Crawler.Domain.csproj src/Crawler.Domain/
COPY src/Crawler.Contracts/Crawler.Contracts.csproj src/Crawler.Contracts/
COPY src/Crawler.Application/Crawler.Application.csproj src/Crawler.Application/
COPY src/Crawler.Infrastructure/Crawler.Infrastructure.csproj src/Crawler.Infrastructure/
COPY src/Crawler.Worker/Crawler.Worker.csproj src/Crawler.Worker/
RUN dotnet restore src/Crawler.Worker/Crawler.Worker.csproj

COPY src/ src/
RUN dotnet publish src/Crawler.Worker/Crawler.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Crawler.Worker.dll"]
