# The standalone worker. Optional: the web app hosts the same schedule in-process by
# default, so this image exists for running ingestion as its own scalable process.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Packages.props ./
COPY src/LineOps.Core/LineOps.Core.csproj               src/LineOps.Core/
COPY src/LineOps.Data/LineOps.Data.csproj               src/LineOps.Data/
COPY src/LineOps.Reliability/LineOps.Reliability.csproj src/LineOps.Reliability/
COPY src/LineOps.Ingestion/LineOps.Ingestion.csproj     src/LineOps.Ingestion/
COPY src/LineOps.Worker/LineOps.Worker.csproj           src/LineOps.Worker/

RUN dotnet restore src/LineOps.Worker/LineOps.Worker.csproj

COPY src/ src/
RUN dotnet publish src/LineOps.Worker/LineOps.Worker.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# See the note in src/LineOps.Web/Dockerfile: keeps Npgsql from logging a spurious
# GSSAPI error on every startup.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

USER $APP_UID

COPY --from=build /app ./

ENTRYPOINT ["dotnet", "LineOps.Worker.dll"]
