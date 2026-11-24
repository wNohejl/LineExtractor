# Build context is the repository root, not this directory, because central package
# management (Directory.Packages.props) and the project references live above it.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy manifests first so `restore` is cached independently of source changes —
# editing a .razor file then rebuilds without re-downloading every package.
COPY Directory.Packages.props ./
COPY src/XLines.Core/XLines.Core.csproj               src/XLines.Core/
COPY src/XLines.Desk/XLines.Desk.csproj               src/XLines.Desk/
COPY src/XLines.Desk.Hosting/XLines.Desk.Hosting.csproj src/XLines.Desk.Hosting/
COPY src/XLines.Data/XLines.Data.csproj               src/XLines.Data/
COPY src/XLines.Reliability/XLines.Reliability.csproj src/XLines.Reliability/
COPY src/XLines.Ingestion/XLines.Ingestion.csproj     src/XLines.Ingestion/
COPY src/XLines.Observability/XLines.Observability.csproj src/XLines.Observability/
COPY src/XLines.Web/XLines.Web.csproj                 src/XLines.Web/

RUN dotnet restore src/XLines.Web/XLines.Web.csproj

COPY src/ src/
# Embedded into XLines.Reliability; the build fails without it.
COPY docs/runbook.md docs/
# Publish restores again, now that the source is here. The restore above only sees project
# files, and the SDK adds Blazor's framework scripts (_framework/blazor.web.js, from
# Microsoft.AspNetCore.App.Internal.Assets) only for a project it can see Razor components
# in — so a --no-restore publish shipped a page whose script 404'd and whose circuit never
# started. The packages are already in the layer above; this pass only adds that one.
RUN dotnet publish src/XLines.Web/XLines.Web.csproj \
    -c Release -o /app

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

ENTRYPOINT ["dotnet", "XLines.Web.dll"]
