#!/bin/sh
T="$GITHUB_TOKEN"  # export GITHUB_TOKEN before running
R="ShukurulloAbdusattorov/tashkent"
ZIP="/d/Tashkent city/AmirTemurSquare_Win64.zip"
BODY='{"tag_name":"v0.1.0","target_commitish":"main","name":"Amir Temur Square v0.1.0 (Windows x64)","body":"First playable build. Unzip and run AmirTemurSquare.exe (Windows 10/11, DX12 GPU with 4 GB+ VRAM recommended). Controls: WASD move, Shift sprint, Space jump, mouse look, keys 1-4 time of day, F3 FPS, Esc release cursor.","draft":false,"prerelease":true}'
ID=$(curl -s -m 60 -X POST -H "Authorization: Bearer $T" -H "Accept: application/vnd.github+json" "https://api.github.com/repos/$R/releases" -d "$BODY" | python -c "import json,sys; j=json.load(sys.stdin); print(j.get('id',''), j.get('message',''))")
echo "release: $ID"
RID=$(echo "$ID" | cut -d' ' -f1)
if [ -z "$RID" ]; then echo "RELEASE FAILED"; exit 1; fi
curl -s -m 7200 -X POST -H "Authorization: Bearer $T" -H "Content-Type: application/zip" --data-binary "@$ZIP" "https://uploads.github.com/repos/$R/releases/$RID/assets?name=AmirTemurSquare_Win64.zip" -o upload_result.json -w "http %{http_code} uploaded %{size_upload} bytes in %{time_total}s\n"
python -c "import json; j=json.load(open('upload_result.json')); print('asset url:', j.get('browser_download_url'), '| state:', j.get('state'), '| msg:', j.get('message'))"
echo "UPLOAD DONE"
