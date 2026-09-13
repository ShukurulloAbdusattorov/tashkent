#!/bin/sh
T="$GITHUB_TOKEN"  # export GITHUB_TOKEN before running
curl -s -m 10800 -X POST -H "Authorization: Bearer $T" -H "Content-Type: application/zip" -T "/d/Tashkent city/AmirTemurSquare_Win64.zip" "https://uploads.github.com/repos/ShukurulloAbdusattorov/tashkent/releases/388004504/assets?name=AmirTemurSquare_Win64.zip" -o upload_result.json -w "http %{http_code} uploaded %{size_upload} bytes in %{time_total}s\n"
python -c "import json; j=json.load(open('upload_result.json')); print('asset url:', j.get('browser_download_url'), '| state:', j.get('state'), '| msg:', j.get('message'))"
echo "UPLOAD2 DONE"
