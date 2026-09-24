#!/usr/bin/env bash
# Uploads every image in a directory tree to FaceMatch in batches.
#   ./scripts/bulk-upload.sh ./photos [http://localhost:8080] [batch-size]
set -euo pipefail

dir="${1:?usage: bulk-upload.sh <directory> [api-url] [batch-size]}"
api="${2:-http://localhost:8080}"
batch="${3:-25}"

mapfile -d '' images < <(find "$dir" -type f \( -iname '*.jpg' -o -iname '*.jpeg' -o -iname '*.png' -o -iname '*.webp' -o -iname '*.bmp' -o -iname '*.gif' -o -iname '*.tif' -o -iname '*.tiff' \) -print0)
echo "Found ${#images[@]} image(s) in $dir"

sent=0
for ((i = 0; i < ${#images[@]}; i += batch)); do
  args=()
  for f in "${images[@]:i:batch}"; do
    args+=(-F "files=@${f}")
    sent=$((sent + 1))
  done
  curl -fsS -X POST "$api/api/images" "${args[@]}" -o /dev/null
  echo "uploaded $sent/${#images[@]}"
done

echo "Done. Indexing runs in the background; progress: curl $api/api/stats"
