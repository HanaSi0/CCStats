#!/usr/bin/env bash
set -euo pipefail
# Prepare the existing isolated checkout; do not create another worktree.
[ "$(uname -s)" = Linux ] && [ "$(uname -m)" = x86_64 ] || { echo 'This cloud setup script requires Linux x86_64.' >&2; exit 1; }
export DOTNET_CLI_HOME=/workspace/.dotnet-cli
export NUGET_PACKAGES=/workspace/.nuget/packages
export DALAMUD_HOME=/workspace/.dalamud
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export PATH=/workspace/.dotnet:$PATH
mkdir -p /workspace/.dotnet /workspace/.cache/ccstats "$DALAMUD_HOME"
if [ ! -f /workspace/.dotnet/sdk/10.0.402/dotnet.dll ]; then
    curl --fail --show-error --silent --location --retry 2 \
      https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.402/dotnet-sdk-10.0.402-linux-x64.tar.gz \
      -o /workspace/.cache/ccstats/dotnet-sdk-10.0.402.tar.gz
    printf '%s  %s\n' \
      1904a98dda2db34ba78ba3c4fb1b224455329af91c341d83547843c1839e112b04ae699ee92fa2449a8f62249196a938723078c0c60b9f65c88539d5687729c2 \
      /workspace/.cache/ccstats/dotnet-sdk-10.0.402.tar.gz | sha512sum --check --status
    tar -xzf /workspace/.cache/ccstats/dotnet-sdk-10.0.402.tar.gz -C /workspace/.dotnet
fi
if [ ! -f /workspace/.cache/ccstats/dalamud-904e0b30.zip ]; then
    curl --fail --show-error --silent --location --retry 2 \
      https://raw.githubusercontent.com/goatcorp/dalamud-distrib/904e0b30d912e68c6de1812f2f143ee496b1b55d/latest.zip \
      -o /workspace/.cache/ccstats/dalamud-904e0b30.zip
fi
printf '%s  %s\n' \
  82d3efad4615d4b40fdabd458132473d6b69e341cd700293a6bb3ff2b5b77547 \
  /workspace/.cache/ccstats/dalamud-904e0b30.zip | sha256sum --check --status
unzip -q -o /workspace/.cache/ccstats/dalamud-904e0b30.zip -d "$DALAMUD_HOME"
cd /workspace/CCStats
python3 tools/generate_localization.py
dotnet restore PvpStats.sln --locked-mode -p:EnableWindowsTargeting=true
dotnet build PvpStats.sln -c Release --no-restore -p:EnableWindowsTargeting=true
dotnet build PvpStats.sln -c Debug --no-restore -p:EnableWindowsTargeting=true
dotnet restore tests/LocalizationChecks --locked-mode
dotnet run --project tests/LocalizationChecks -c Release --no-restore
