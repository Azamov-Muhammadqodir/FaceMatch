#!/usr/bin/env bash
# Downloads a small public set of face photos (used by the demo and the sample-based tests) into ./samples.
# Sources: github.com/ageitgey/face_recognition (MIT) and github.com/deepinsight/insightface (MIT).
set -euo pipefail

dest="${1:-$(dirname "$0")/../samples}"
mkdir -p "$dest"

fr=https://raw.githubusercontent.com/ageitgey/face_recognition/master
ins=https://raw.githubusercontent.com/deepinsight/insightface/master/python-package/insightface/data/images

declare -A files=(
  [obama.jpg]=$fr/examples/obama.jpg
  [obama2.jpg]=$fr/examples/obama2.jpg
  [obama3.jpg]=$fr/tests/test_images/obama3.jpg
  [biden.jpg]=$fr/examples/biden.jpg
  [obama_biden_pair.jpg]=$fr/examples/two_people.jpg
  [obama_biden_stage.jpg]=$fr/examples/knn_examples/test/obama_and_biden.jpg
  [kit_harington.jpeg]=$fr/examples/knn_examples/train/kit_harington/john1.jpeg
  [kit_with_rose.jpg]=$fr/examples/knn_examples/test/kit_with_rose.jpg
  [rose_leslie.jpg]=$fr/examples/knn_examples/train/rose_leslie/img1.jpg
  [alex_lacamoire1.jpg]=$fr/examples/knn_examples/train/alex_lacamoire/img1.jpg
  [alex_lacamoire2.jpg]=$fr/examples/knn_examples/test/alex_lacamoire1.jpg
  [group.jpg]=$ins/t1.jpg
  [masked.jpg]=$ins/mask_white.jpg
)

for name in "${!files[@]}"; do
  if [[ ! -s "$dest/$name" ]]; then
    curl -fsSL -o "$dest/$name" "${files[$name]}"
    echo "downloaded $name"
  fi
done

echo "Samples are in $(cd "$dest" && pwd)"
