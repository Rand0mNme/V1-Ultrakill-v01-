#!/bin/bash
# Adds new materials from Assets/UK to V1/shadermap.txt: file name -> original ULTRAKILL shader name
# works from any folder; GUIDMAP overrides the path to guidmap.txt written by tools/UkDeps
D=$(cd "$(dirname "$0")" && pwd)
cd "$D/../V1Unity/Assets"
M="${GUIDMAP:-$D/UkDeps/bin/Debug/net8.0/guidmap.txt}"
for f in UK/*.mat; do
  n=$(basename "$f" .mat)
  grep -q "^$n	" V1/shadermap.txt && continue
  g=$(grep -m1 "m_Shader:" "$f" | sed -n 's/.*guid: \([0-9a-f]*\).*/\1/p')
  s=""
  if [ -n "$g" ]; then
    p=$(grep -m1 "^$g" "$M" | cut -f2 | tr '\\' '/')
    p=$(cygpath -u "$p" 2>/dev/null || echo "$p")
    [ -f "$p" ] && s=$(grep -m1 -o 'Shader "[^"]*"' "$p" | cut -d'"' -f2)
  fi
  echo "$n	$s"
  printf '%s\t%s\n' "$n" "$s" >> V1/shadermap.txt
done
