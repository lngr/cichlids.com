#!/usr/bin/env bash
# Installs ffmpeg and ffprobe into ~/.local/bin from the static build image that the API image
# also copies them from (app/api/Dockerfile). The video tests and the local API call both
# programs by name. Idempotent: binaries that report the pinned version are kept.
set -euo pipefail

image="mwader/static-ffmpeg:7.1.1@sha256:11a44711684c0b9f754c047dcd64235b8b52deab251bd0e0a86f22faa160749c"
version="${image#*:}"
version="${version%%@*}"
bin_dir="${FFMPEG_INSTALL_DIR:-$HOME/.local/bin}"

current=""
if [ -x "$bin_dir/ffmpeg" ]; then
  current="$("$bin_dir/ffmpeg" -version 2>/dev/null || true)"
fi
if [ -x "$bin_dir/ffprobe" ] && [[ "$current" == "ffmpeg version $version"* ]]; then
  echo "  ffmpeg $version present in $bin_dir"
  exit 0
fi

mkdir -p "$bin_dir"
container="$(docker create "$image" x)"
trap 'docker rm "$container" >/dev/null' EXIT
docker cp "$container:/ffmpeg" "$bin_dir/ffmpeg"
docker cp "$container:/ffprobe" "$bin_dir/ffprobe"
echo "  ffmpeg $version installed in $bin_dir"
