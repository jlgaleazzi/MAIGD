#!/usr/bin/env bash
# Run the Unity editor in batch mode for the Biplanes crew.
# Usage: tools/unity.sh <compile|test|scenes|configure|webgl|windows|mac|all>
# Exit codes: 0 ok, 1 Unity error (see the log), 2 tests failed, 3 Unity not available, 4 bad usage.
set -u

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$ROOT/Biplanes"
LOGS="$PROJECT/Logs"
BUILDER="Biplanes.EditorTools.ProjectBuilder"

if [ -z "${UNITY_PATH:-}" ] || [ ! -x "$UNITY_PATH" ]; then
  echo "Unity not available (set UNITY_PATH to the Unity editor executable). Fall back to static checks."
  exit 3
fi
if [ ! -d "$PROJECT/Assets" ]; then
  echo "Unity project not found at $PROJECT. Create it first (see README.md)."
  exit 3
fi
mkdir -p "$LOGS"

run() { # $1 = log name, rest = extra Unity args
  local log="$LOGS/$1.log"; shift
  "$UNITY_PATH" -batchmode -nographics -projectPath "$PROJECT" -logFile "$log" "$@"
  local code=$?
  if [ $code -ne 0 ]; then
    echo "Unity exited with $code. Errors from $log:"
    grep -E "error CS[0-9]+|Exception|Error:|Scripts have compiler errors" "$log" | head -40
  else
    echo "OK ($log)"
  fi
  return $code
}

case "${1:-}" in
  compile)   run compile -quit ;;
  scenes)    run scenes -quit -executeMethod "$BUILDER.BuildScenes" ;;
  configure) run configure -quit -executeMethod "$BUILDER.ConfigureProject" ;;
  webgl)     run build-webgl -quit -executeMethod "$BUILDER.BuildWebGL" ;;
  windows)   run build-windows -quit -executeMethod "$BUILDER.BuildWindows" ;;
  mac)       run build-mac -quit -executeMethod "$BUILDER.BuildMac" ;;
  all)       run build-all -quit -executeMethod "$BUILDER.BuildAll" ;;
  test)
    # -runTests quits on its own; don't pass -quit. Exit code 2 means some tests failed.
    run test -runTests -testPlatform EditMode -testResults "$LOGS/editmode-results.xml"
    code=$?
    if [ -f "$LOGS/editmode-results.xml" ]; then
      grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' "$LOGS/editmode-results.xml" | head -1
      grep -o '<test-case [^>]*result="Failed"[^>]*' "$LOGS/editmode-results.xml" | grep -o 'fullname="[^"]*"' | head -20
    fi
    exit $code ;;
  *) echo "usage: tools/unity.sh <compile|test|scenes|configure|webgl|windows|mac|all>"; exit 4 ;;
esac
