# Sourced by the other scripts. Where state lives: $ONERENDER_HOME (default ~/onerender), outside the repository.
export ONERENDER_HOME="${ONERENDER_HOME:-$HOME/onerender}"
export ONERENDER_ARTIFACTS="${ONERENDER_ARTIFACTS:-$ONERENDER_HOME/artifacts}"
if [ -d "$HOME/dotnet10" ]; then export PATH="$HOME/dotnet10:$PATH" DOTNET_ROOT="$HOME/dotnet10"; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 MSBUILDDISABLENODEREUSE=1
GEOMDIFF_SCRIPTS="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GEOMDIFF_REPO="$(cd "$GEOMDIFF_SCRIPTS/../../.." && pwd)"
