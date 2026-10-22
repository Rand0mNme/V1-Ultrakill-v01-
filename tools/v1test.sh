#!/bin/bash
# Launches RoR2 with the V1 Dev profile and the autotest (BepInEx/V1Test/enabled), waits until spawned on a stage.
# Test commands go in BepInEx/V1Test/cmd.txt, state in status.txt, log in log.txt, screenshots in shots/.
P="$APPDATA/r2modmanPlus-local/RiskOfRain2/profiles/V1 Dev/BepInEx"
mkdir -p "$P/V1Test"; touch "$P/V1Test/enabled"; rm -f "$P/V1Test/cmd.txt" "$P/V1Test/log.txt" "$P/V1Test/status.txt"
cd "/d/Games/steamapps/common/Risk of Rain 2"
("./Risk of Rain 2.exe" --doorstop-enabled true --doorstop-target-assembly "$(cygpath -w "$P")\core\BepInEx.Preloader.dll" >/dev/null 2>&1 &)
for i in $(seq 1 100); do
  sleep 3
  # the switch is only needed at launch: remove it so a normal game runs without the autotest
  grep -q "autotest enabled" "$P/V1Test/log.txt" 2>/dev/null && rm -f "$P/V1Test/enabled"
  if grep -q "V1SpawnFall\|V1CharacterMain" "$P/V1Test/status.txt" 2>/dev/null && [ "$(sed -n 2p "$P/V1Test/status.txt")" != "scene lobby" ]; then break; fi
done
cat "$P/V1Test/log.txt"
