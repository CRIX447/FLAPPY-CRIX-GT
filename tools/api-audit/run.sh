#!/bin/sh
# Checks every UnityEngine call in FlappyCrix.dll against Unity's real API (see check.py).
#   tools/api-audit/run.sh [path/to/FlappyCrix.dll] [unity branch, default 2022.3]
# Needs: ikdasm (mono-devel), python3, git. Fetches Unity's C# reference source once
# (github.com/Unity-Technologies/UnityCsReference, reference-only licence; it is only
# read here, never copied into the mod).
# Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
set -e
HERE=$(dirname "$(realpath "$0")")
DLL=$(realpath "${1:-$HERE/../../mod/FlappyCrix/FlappyCrix.dll}")
cd "$HERE"
BRANCH=${2:-2022.3}
UCS=${UNITY_CS_REFERENCE:-${TMPDIR:-/tmp}/UnityCsReference-$BRANCH}
[ -d "$UCS/Runtime" ] || git clone -q --depth 1 -b "$BRANCH" https://github.com/Unity-Technologies/UnityCsReference.git "$UCS"
python3 check.py "$DLL" "$UCS"
