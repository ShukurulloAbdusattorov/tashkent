#!/bin/sh
# usage: run_unity.sh <ExecuteMethod|-> <logname> [extra args]
METHOD="$1"; NAME="$2"; shift 2
mkdir -p "/d/Tashkent city/data/logs"
LOG="/d/Tashkent city/data/logs/$NAME.log"
WLOG="D:\Tashkent city\data\logs\$NAME.log"
if [ "$METHOD" != "-" ]; then
  "/d/Unity downloads/6000.3.9f1/Editor/Unity.exe" -batchmode -quit -projectPath "D:\Tashkent city\AmirTemurSquare" -logFile "$WLOG" -executeMethod "$METHOD" "$@"
else
  "/d/Unity downloads/6000.3.9f1/Editor/Unity.exe" -batchmode -quit -projectPath "D:\Tashkent city\AmirTemurSquare" -logFile "$WLOG" "$@"
fi
CODE=$?
echo "Unity exit code: $CODE"
echo "=== compiler errors ==="; grep -E "error CS[0-9]+" "$LOG" | sed 's/^.*Assets/Assets/' | sort -u | head -80
echo "=== build log / exceptions ==="; grep -E "^\[BuildAll\]|^\[AmirTemur|Exception|Error:|error:" "$LOG" | grep -v "error CS" | head -60
exit $CODE
