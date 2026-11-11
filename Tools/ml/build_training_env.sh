#!/usr/bin/env bash
# Build the NN training game (the four NN scenes) with the Unity Editor in batch mode (macOS / Linux).
# Same as Unity menu Tools > NN Training > Build Training Environment. The project must not be open in the Editor.
# Shortcut for: Tools/ml/nntools build [--output PATH]   (Windows: Tools\ml\nntools.cmd build)
exec "$(dirname "$0")/nntools" build "$@"
