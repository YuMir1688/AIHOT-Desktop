import json
import pathlib
import sys
sys.path.insert(0, r'C:\Users\Administrator\AppData\Local\AIHOT-DesignTools\python')
import zxingcpp
from PIL import Image

root = pathlib.Path(sys.argv[1])
sections = json.loads((root / 'long-sections.json').read_text(encoding='utf-8'))
with Image.open(root / '报告长图示例.png') as image:
    image.load()
    assert image.width == 1080 and image.height == sum(s['height'] for s in sections)
    top = 0
    for section in sections:
        code = zxingcpp.read_barcode(image.crop((0, top, image.width, top + section['height'])))
        assert (code is None) if section['original'] is None else (code is not None and code.text == section['original'])
        top += section['height']
    sample_size = image.size
with Image.open(root / '完整月报长图.png') as image:
    image.load()
    assert image.width == 1080 and image.height > sample_size[1]
    full_size = image.size
result = f'PASS: standalone PNG decode; sample {sample_size}, full month {full_size}; all sample original QR links match.'
(root / 'long-png-verification.txt').write_text(result, encoding='utf-8')
print(result)
