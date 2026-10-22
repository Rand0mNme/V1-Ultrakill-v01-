#!/bin/bash
# Builds everything the mod needs from your own ULTRAKILL install: the v1assets bundle and the Sounds/*.wav files.
#
#   tools/build_assets.sh --ultrakill "D:/SteamLibrary/steamapps/common/ULTRAKILL" \
#       --assetripper "C:/Tools/AssetRipper/AssetRipper.GUI.Free.exe" \
#       --unity "C:/Program Files/Unity/Hub/Editor/2021.3.33f1/Editor/Unity.exe"
#
# Steps (each one is skipped or reused when its output already exists, so the script can be re-run):
#   1. Export ULTRAKILL as a Unity project with AssetRipper (headless, ~12 GB, takes a while) into --export
#      (default: <repo>/_export). If the export already exists, --ultrakill and --assetripper aren't needed.
#   2. Build the helper tools (tools/UkDeps, tools/UkScene, tools/OggToWav).
#   3. Cut three objects out of ULTRAKILL prefabs/scenes (first room, Whiplash arm, crosshair) with UkScene.
#   4. Copy every asset BuildV1.cs needs, with its dependencies, into V1Unity/Assets/UK with UkDeps.
#   5. Convert the sounds listed in tools/sounds.tsv to V1Mod/Assets/Sounds/*.wav.
#   6. Build V1Unity/AssetBundles/v1assets with Unity (BuildV1.Build).
# Then build the mod: cd V1Mod && dotnet build -c Release
#
# Needs: bash (Git Bash on Windows), .NET SDK 8, Unity 2021.3.x, AssetRipper 2.x (only for step 1), curl.
# Tested with the ULTRAKILL build from October 2026 and AssetRipper 2.0; object ids in step 3 can change between
# ULTRAKILL versions (the script stops with a message if they do; see docs/ASSETS.md for how to find new ones).
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
UK_GAME=""
EXPORT="$ROOT/_export"
UNITY="${UNITY_EXE:-}"
RIPPER="${ASSETRIPPER_EXE:-}"
while [ $# -gt 0 ]; do
  case "$1" in
    --ultrakill) UK_GAME="$2"; shift 2 ;;
    --export) EXPORT="$2"; shift 2 ;;
    --unity) UNITY="$2"; shift 2 ;;
    --assetripper) RIPPER="$2"; shift 2 ;;
    -h|--help) sed -n 2,23p "$0"; exit 0 ;;
    *) echo "unknown argument: $1"; exit 1 ;;
  esac
done

winpath() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else echo "$1"; fi; }
step() { echo; echo "=== $*"; }

ASSETS="$EXPORT/ExportedProject/Assets"
UNITY_UK="$ROOT/V1Unity/Assets/UK"

# 1. AssetRipper export
if [ ! -d "$ASSETS" ]; then
  step "1/6 Exporting ULTRAKILL with AssetRipper (this takes a long time)"
  [ -n "$UK_GAME" ] && [ -f "$UK_GAME/ULTRAKILL.exe" ] || { echo "Pass --ultrakill <folder with ULTRAKILL.exe>"; exit 1; }
  [ -n "$RIPPER" ] && [ -f "$RIPPER" ] || { echo "Pass --assetripper <path to AssetRipper.GUI.Free.exe> (AssetRipper 2.x from https://github.com/AssetRipper/AssetRipper/releases)"; exit 1; }
  mkdir -p "$EXPORT"
  "$RIPPER" --port 7781 --headless > "$EXPORT/assetripper.log" 2>&1 &
  RIPPER_PID=$!
  for i in $(seq 1 60); do curl -s -o /dev/null http://127.0.0.1:7781/ && break; sleep 1; done
  echo "loading game files..."
  curl -s -o /dev/null --max-time 3500 -d "path=$(winpath "$UK_GAME")" http://127.0.0.1:7781/LoadFolder
  echo "exporting (several minutes to an hour)..."
  curl -s -o /dev/null --max-time 7200 -d "path=$(winpath "$EXPORT")" http://127.0.0.1:7781/Export/UnityProject
  kill "$RIPPER_PID" 2>/dev/null || true
  [ -d "$ASSETS" ] || { echo "Export failed, see $EXPORT/assetripper.log"; exit 1; }
else
  step "1/6 Using the existing export in $EXPORT"
fi

# 2. tools
step "2/6 Building tools"
for t in UkDeps UkScene OggToWav; do
  dotnet build "$ROOT/tools/$t" -c Release -o "$ROOT/tools/$t/out" -v q -nologo | grep -E "error|Error" && exit 1 || true
done
UKDEPS="$ROOT/tools/UkDeps/out/UkDeps.dll"
UKSCENE="$ROOT/tools/UkScene/out/UkScene.dll"
OGG2WAV="$ROOT/tools/OggToWav/out/OggToWav.dll"
# UkDeps caches guid -> file next to its binary; rebuild it for this export
rm -f "$ROOT/tools/UkDeps/out/guidmap.txt"
GUIDMAP="$ROOT/tools/UkDeps/out/guidmap.txt"

# 4a. assets listed in BuildV1.cs (prefabs, textures, sprites, font) + the weapon icons it whitens
step "3/6 Copying ULTRAKILL assets into V1Unity/Assets/UK"
rm -rf "$UNITY_UK" "$UNITY_UK.meta" "$ROOT/V1Unity/Assets/V1/UI"
mkdir -p "$UNITY_UK" "$ROOT/V1Unity/Assets/V1/UI"
EXTRACTED="V1FirstRoomRoom.prefab _V1WhiplashArm.prefab"
mapfile -t ROOTS < <(grep -o '"Assets/UK/[^"]*\.[a-zA-Z]*"' "$ROOT/V1Unity/Assets/Editor/BuildV1.cs" | tr -d '"' | sed 's|^Assets/UK/||' | sort -u)
mapfile -t ICONS < <(grep -o 'V1Icon_[A-Za-z0-9]*\.png' "$ROOT/V1Unity/Assets/Editor/BuildV1.cs" | sed 's/^V1Icon_//' | sort -u)
LIST=()
for r in "${ROOTS[@]}" "${ICONS[@]}"; do
  case " $EXTRACTED " in *" $r "*) continue ;; esac
  [ -f "$ASSETS/$r" ] || { echo "MISSING in export: $r"; exit 1; }
  LIST+=("$r")
done
dotnet "$UKDEPS" "$(winpath "$ASSETS")" "$(winpath "$UNITY_UK")" "${LIST[@]}" | tail -1

# 3. objects that only exist inside other prefabs/scenes (UkScene keeps their original ids)
step "4/6 Cutting the first room, Whiplash arm and crosshair out of ULTRAKILL files"
extract() { # source file, root transform id, output name
  grep -q -- "--- !u!\(4\|224\) &$2\b" "$ASSETS/$1" || { echo "Object $2 not found in $1 (different ULTRAKILL version?). See docs/ASSETS.md."; exit 1; }
  dotnet "$UKSCENE" "$(winpath "$ASSETS/$1")" "$(winpath "$GUIDMAP")" extract "$2" "$(winpath "$ASSETS/$3")"
}
extract "FirstRoom.prefab" 4145039172186485 "V1FirstRoomRoom.prefab"           # FirstRoom -> "Room" branch
extract "FirstRoom Encore.prefab" 4077724941760488 "_V1WhiplashArm.prefab"     # Whiplash arm model
extract "005a4f2ce549277458596ee0f0d6e88c.unity" 24705 "UKCrosshair.prefab"    # Player/Canvas/Crosshair Filler/Crosshair
dotnet "$UKDEPS" "$(winpath "$ASSETS")" "$(winpath "$UNITY_UK")" V1FirstRoomRoom.prefab _V1WhiplashArm.prefab UKCrosshair.prefab | tail -1
mv "$UNITY_UK/UKCrosshair.prefab" "$ROOT/V1Unity/Assets/V1/UI/UKCrosshair.prefab"
rm -f "$UNITY_UK/UKCrosshair.prefab.meta"

# 5. sounds
step "5/6 Converting sounds"
SND_TMP="$EXPORT/_sounds_ogg"
rm -rf "$SND_TMP"; mkdir -p "$SND_TMP" "$ROOT/V1Mod/Assets/Sounds"
while IFS=$'\t' read -r name clip; do
  [ -n "$name" ] || continue
  [ -f "$ASSETS/$clip" ] || { echo "MISSING sound in export: $clip"; exit 1; }
  cp "$ASSETS/$clip" "$SND_TMP/$name.ogg"
done < "$ROOT/tools/sounds.tsv"
dotnet "$OGG2WAV" "$(winpath "$SND_TMP")" "$(winpath "$ROOT/V1Mod/Assets/Sounds")" | wc -l | sed 's/$/ sounds converted/'
rm -rf "$SND_TMP"

# 6. bundle
step "6/6 Building the asset bundle with Unity"
if [ -z "$UNITY" ]; then
  UNITY=$(ls -d "/c/Program Files/Unity/Hub/Editor/2021.3."*/Editor/Unity.exe 2>/dev/null | tail -1 || true)
fi
[ -n "$UNITY" ] && [ -f "$UNITY" ] || { echo "Pass --unity <path to Unity 2021.3.x Unity.exe>"; exit 1; }
"$UNITY" -batchmode -quit -projectPath "$(winpath "$ROOT/V1Unity")" -executeMethod BuildV1.Build -logFile "$(winpath "$EXPORT/unity_build.log")" || { echo "Unity build failed, see $EXPORT/unity_build.log"; exit 1; }
[ -f "$ROOT/V1Unity/AssetBundles/v1assets" ] || { echo "No bundle produced, see $EXPORT/unity_build.log"; exit 1; }
grep -h "^V1: " "$EXPORT/unity_build.log" | tail -5 || true

echo
echo "Done: V1Unity/AssetBundles/v1assets and V1Mod/Assets/Sounds/*.wav."
echo "Next: set GameDir/R2ProfileDir in V1Mod/V1Mod.csproj, then: cd V1Mod && dotnet build -c Release"
