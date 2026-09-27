#!/usr/bin/env bash
# Pull the published images and restart onto them. Run on the server, from this directory.
#
#   ./deploy.sh                      # whatever IMAGE_TAG in .env says (main by default)
#   IMAGE_TAG=sha-1a2b3c4 ./deploy.sh  # pin one build: this is how a deploy is rolled back
#
# Migrations run when web and worker start (DatabaseInitializer); EF's migration lock keeps
# the two from racing, and migrations are additive by the project's rule (DESIGN §13), so the
# previous image still runs against the newer schema when rolling back.
set -euo pipefail
cd "$(dirname "$0")"

if [[ ! -f .env ]]; then
    echo "deploy: no .env here. Copy .env.example to .env and fill it in." >&2
    exit 1
fi

if [[ "$(stat -c %a .env)" != "600" ]]; then
    echo "deploy: .env is readable by others; chmod 600 .env" >&2
    exit 1
fi

mkdir -p backups

docker compose pull
docker compose up -d --remove-orphans
docker image prune -f >/dev/null

# Asked from outside, through Caddy, so the answer covers DNS, the certificate, the proxy and
# the database — everything a visitor's request crosses. /ready is anonymous in every mode.
host=$(grep -E '^LINEOPS_HOST=' .env | cut -d= -f2- | tr -d '"'"'"' \r')
echo "deploy: waiting for https://$host/ready ..."
for _ in $(seq 1 45); do
    if curl -fsS --max-time 5 "https://$host/ready" >/dev/null 2>&1; then
        echo "deploy: ready."
        docker compose ps
        exit 0
    fi
    sleep 2
done

echo "deploy: https://$host/ready did not answer within 90 seconds. Recent logs:" >&2
docker compose logs --tail 20 caddy >&2
docker compose logs --tail 40 web >&2
exit 1
