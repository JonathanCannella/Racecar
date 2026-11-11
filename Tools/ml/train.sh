#!/usr/bin/env bash
# Train an ML-Agents racecar behavior (macOS / Linux). Shortcut for: Tools/ml/nntools train [options] [-- mlagents-learn args]
# (Windows: Tools\ml\nntools.cmd train). See `Tools/ml/nntools train --help`.
exec "$(dirname "$0")/nntools" train "$@"
