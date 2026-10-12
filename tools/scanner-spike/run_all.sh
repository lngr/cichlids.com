#!/bin/sh
# Embeds all models one after another. Usage: ./run_all.sh [model ...]
cd "$(dirname "$0")"
export HF_HOME="$PWD/cache/hf"
export TOKENIZERS_PARALLELISM=false
MODELS="${*:-bioclip dinov2 clip bioclip2}"
mkdir -p logs
for m in $MODELS; do
  .venv/bin/python embed.py --model "$m" --cap "${CAP:-200}" >> "logs/embed_$m.log" 2>&1
done
