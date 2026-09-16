import json
import pathlib
import sys
# Install dependencies in the active environment: python -m pip install Pillow zxing-cpp
import zxingcpp
from PIL import Image

root = pathlib.Path(sys.argv[1])
checked = 0
for manifest_path in (root / 'exports').glob('*/分享清单.json'):
    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    for page in manifest['pages']:
        code = zxingcpp.read_barcode(Image.open(manifest_path.parent / page['file']))
        if page['original'] is None:
            assert code is None, 'Report cover must not imply a published report URL'
        else:
            assert code is not None and code.text == page['original'], page['file']
        checked += 1
(root / 'qr-verification.txt').write_text(f'PASS: {checked} exported PNGs verified; article URLs match, covers have no QR.', encoding='utf-8')
print(f'PASS: {checked} exported PNGs verified; article URLs match, covers have no QR.')
