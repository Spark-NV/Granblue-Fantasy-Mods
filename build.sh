#!/usr/bin/env bash
set -euo pipefail

root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
mod_id=gbfr.spark.granblue-fantasy-mods
version=$(sed -n 's/.*"ModVersion": *"\([^"]*\)".*/\1/p' "$root/mod/ModConfig.json")
package="$root/package/$mod_id"

rm -rf "$root/package"
dotnet build "$root/src/GranblueFantasyMods.csproj" --configuration Release --output "$package"
cp "$root"/mod/* "$root/THIRD_PARTY_NOTICES.md" "$package/"

mkdir -p "$root/dist"
rm -f "$root/dist/$mod_id-$version.zip"
python3 -c 'import shutil, sys; shutil.make_archive(sys.argv[1], "zip", sys.argv[2], sys.argv[3])' \
  "$root/dist/$mod_id-$version" "$root/package" "$mod_id"

echo "Built $package"
echo "Release archive: $root/dist/$mod_id-$version.zip"
