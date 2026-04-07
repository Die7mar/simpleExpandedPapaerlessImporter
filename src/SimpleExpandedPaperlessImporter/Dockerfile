# ── Build stage ──────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj first for better layer caching
COPY ["SimpleExpandedPaperlessImporter.csproj", "./"]
RUN dotnet restore

# Copy remaining sources and publish (restore runs again to resolve all transitive deps)
COPY . .
RUN dotnet publish -c Release -o /app/publish

# ── Runtime stage ─────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install fonts for iText PDF generation
RUN apt-get update && apt-get install -y --no-install-recommends \
    fontconfig \
    fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Create data directories
RUN mkdir -p /data/inbox /data/done /data/error /app/logs

VOLUME ["/data", "/app/logs"]

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "SimpleExpandedPaperlessImporter.dll"]
