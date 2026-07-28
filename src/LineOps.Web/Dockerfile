# Build context is the repository root, not this directory, because central package
# management (Directory.Packages.props) and the project references live above it.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy manifests first so `restore` is cached independently of source changes —
# editing a .razor file then rebuilds without re-downloading every package.
COPY Directory.Packages.props ./
COPY src/LineOps.Core/LineOps.Core.csproj               src/LineOps.Core/
COPY src/LineOps.Data/LineOps.Data.csproj               src/LineOps.Data/
COPY src/LineOps.Reliability/LineOps.Reliability.csproj src/LineOps.Reliability/
COPY src/LineOps.Ingestion/LineOps.Ingestion.csproj     src/LineOps.Ingestion/
COPY src/LineOps.Web/LineOps.Web.csproj                 src/LineOps.Web/

RUN dotnet restore src/LineOps.Web/LineOps.Web.csproj

COPY src/ src/
RUN dotnet publish src/LineOps.Web/LineOps.Web.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Npgsql probes for GSSAPI/Kerberos during connection setup even when using password auth,
# and logs a bare "Error: libgssapi_krb5.so.2" line on every boot without it. The connection
# works regardless, but a platform whose whole argument is that log noise causes alert
# fatigue should not print a spurious error on startup.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

# Created and chowned before the filesystem goes read-only, so the mounted volumes are
# writable by the runtime user without ever running as root.
RUN mkdir -p /keys /https && chown -R $APP_UID:$APP_UID /keys /https

# Non-root. Combined with read_only, cap_drop and no-new-privileges in compose, a
# compromise inside the app has almost nothing to escalate with.
USER $APP_UID

COPY --from=build /app ./

# HTTPS only. The certificate is supplied at runtime via a read-only volume mount —
# never COPYed into the image, which would break dev/prod parity and risk disclosing it.
# Port and cert path come from compose; see ASPNETCORE_URLS and Kestrel__Certificates.
EXPOSE 9443

ENTRYPOINT ["dotnet", "LineOps.Web.dll"]
