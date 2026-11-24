# The standalone worker. Optional: the web app hosts the same schedule in-process by
# default, so this image exists for running ingestion as its own scalable process.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Packages.props ./
COPY src/XLines.Core/XLines.Core.csproj               src/XLines.Core/
COPY src/XLines.Data/XLines.Data.csproj               src/XLines.Data/
COPY src/XLines.Reliability/XLines.Reliability.csproj src/XLines.Reliability/
COPY src/XLines.Ingestion/XLines.Ingestion.csproj     src/XLines.Ingestion/
COPY src/XLines.Observability/XLines.Observability.csproj src/XLines.Observability/
COPY src/XLines.Worker/XLines.Worker.csproj           src/XLines.Worker/

RUN dotnet restore src/XLines.Worker/XLines.Worker.csproj

COPY src/ src/
# Embedded into XLines.Reliability; the build fails without it.
COPY docs/runbook.md docs/
RUN dotnet publish src/XLines.Worker/XLines.Worker.csproj \
    -c Release -o /app --no-restore

# aspnet, not runtime: XLines.Observability references OpenTelemetry's ASP.NET Core
# instrumentation, which brings the ASP.NET Core shared framework the plain runtime lacks.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# See the note in src/XLines.Web/Dockerfile: keeps Npgsql from logging a spurious
# GSSAPI error on every startup.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

USER $APP_UID

COPY --from=build /app ./

ENTRYPOINT ["dotnet", "XLines.Worker.dll"]
