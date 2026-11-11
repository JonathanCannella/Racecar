#!/usr/bin/env bash
# Create a Python virtual environment with the ML-Agents trainer (mlagents 1.1.0) for this project.
# mlagents 1.1.0 requires Python 3.10.1 - 3.10.12.
#
# Usage: Tools/ml/setup_mlagents_env.sh
#   PYTHON=/path/to/python3.10  use a specific interpreter
#   VENV=/path/to/venv          venv location (default: <project>/.venv-mlagents)
#
# If you don't have Python 3.10: `pyenv install 3.10.12` (macOS/Linux), or use conda:
#   conda create -n mlagents python=3.10.12 && conda activate mlagents && PYTHON=python Tools/ml/setup_mlagents_env.sh
set -euo pipefail

PROJECT="$(cd "$(dirname "$0")/../.." && pwd)"
VENV="${VENV:-$PROJECT/.venv-mlagents}"
PY="${PYTHON:-}"

if [ -z "$PY" ]; then
  if command -v pyenv >/dev/null 2>&1 && pyenv prefix 3.10.12 >/dev/null 2>&1; then
    PY="$(pyenv prefix 3.10.12)/bin/python3"
  elif command -v python3.10 >/dev/null 2>&1; then
    PY="python3.10"
  else
    echo "Python 3.10.x not found. Install it (e.g. 'pyenv install 3.10.12') or set PYTHON=/path/to/python3.10" >&2
    exit 1
  fi
fi

"$PY" - <<'PYEOF'
import sys
v = sys.version_info
if v[:2] != (3, 10) or v[2] > 12:
    sys.exit(f"mlagents 1.1.0 needs Python 3.10.1-3.10.12, found {sys.version.split()[0]}")
PYEOF

echo "Creating venv at $VENV using $("$PY" --version)"
"$PY" -m venv "$VENV"
"$VENV/bin/python" -m pip install --upgrade pip
# ML-Agents 4.x constrains torch to <= 2.8
"$VENV/bin/python" -m pip install "torch>=2.1.1,<=2.8.0"
if [ "$(uname -s)" = "Darwin" ] && [ "$(uname -m)" = "arm64" ]; then
  # Apple Silicon: grpcio <= 1.48.2 (required by mlagents 1.1.0) has no arm64 wheel and fails to build
  # from source. Install the other dependencies within their stated limits, use the oldest grpcio
  # with an arm64 wheel, then install mlagents without letting pip re-resolve grpcio.
  "$VENV/bin/python" -m pip install \
    "grpcio==1.54.3" "protobuf>=3.6,<3.21" "numpy>=1.23.5,<1.24.0" \
    "h5py>=2.9.0" "Pillow>=4.2.1" "pyyaml>=3.1.0" "tensorboard>=2.14" "six>=1.16" "attrs>=19.3.0" \
    "huggingface-hub>=0.14" "onnx==1.15.0" "cattrs>=1.1.0,<1.7" \
    "cloudpickle" "gym>=0.21.0" "pettingzoo==1.15.0" "filelock>=3.4.0"
  "$VENV/bin/python" -m pip install --no-deps "mlagents==1.1.0" "mlagents-envs==1.1.0"
else
  "$VENV/bin/python" -m pip install "mlagents==1.1.0"
fi

"$VENV/bin/mlagents-learn" --help >/dev/null
echo "Done. Activate with: source \"$VENV/bin/activate\""
