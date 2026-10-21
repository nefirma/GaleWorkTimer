#!/usr/bin/env bash
set -euo pipefail

version="${1:?Usage: build-windows-candidate.sh <numeric-version> [output-label]}"
label="${2:-${version}}"
if [[ ! "${version}" =~ ^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?$ || ! "${label}" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ || "${label}" == *".."* ]]; then
    exit 2
fi
output_dir="artifacts/windows-candidate/${label}"
dotnet_command="${DOTNET:-dotnet}"

if [[ -e "${output_dir}" ]]; then
    exit 2
fi

mkdir -p "${output_dir}/app-publish" "${output_dir}/setup-publish"

"${dotnet_command}" publish src/GaleWorkTimer/GaleWorkTimer.csproj \
    --configuration Release --runtime win-x64 --self-contained true \
    -p:Version="${version}" -p:AssemblyVersion="${version}" \
    -p:FileVersion="${version}" -p:InformationalVersion="${version}" \
    -p:DebugType=None -p:DebugSymbols=false \
    --output "${output_dir}/app-publish"

"${dotnet_command}" publish packaging/GaleWorkTimer.Setup/GaleWorkTimer.Setup.csproj \
    --configuration Release --runtime win-x64 --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:Version="${version}" -p:AssemblyVersion="${version}" \
    -p:FileVersion="${version}" -p:InformationalVersion="${version}" \
    --output "${output_dir}/setup-publish"

"${dotnet_command}" run --project packaging/GaleWorkTimer.Setup/GaleWorkTimer.Setup.csproj \
    --configuration Release -- --pack "${output_dir}/app-publish" \
    "${output_dir}/GaleWorkTimer-win-x64.zip" "${version}"

cp "${output_dir}/setup-publish/GaleWorkTimer.Setup.exe" "${output_dir}/GaleWorkTimer.Setup.exe"

"${dotnet_command}" run --project packaging/GaleWorkTimer.Setup/GaleWorkTimer.Setup.csproj \
    --configuration Release -- --sha256 "${output_dir}/GaleWorkTimer.Setup.exe"

"${dotnet_command}" run --project packaging/GaleWorkTimer.Setup/GaleWorkTimer.Setup.csproj \
    --configuration Release -- --verify "${output_dir}/GaleWorkTimer-win-x64.zip"
