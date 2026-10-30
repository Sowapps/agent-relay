"""Build the multi-size Windows icon from the small SVG source (Pillow, development only)."""
from pathlib import Path
import xml.etree.ElementTree as ET
from PIL import Image, ImageDraw


def main():
    assets = Path(__file__).resolve().parents[1] / 'src/AgentRelay.Windows/Assets'
    source = ET.parse(assets / 'agent-relay.svg').getroot()
    scale = 16
    image = Image.new('RGBA', (64 * scale, 64 * scale))
    draw = ImageDraw.Draw(image)
    for element in source:
        tag = element.tag.rsplit('}', 1)[-1]
        if tag == 'rect':
            x, y, width, height, radius = (float(element.get(key, '0')) * scale for key in ['x', 'y', 'width', 'height', 'rx'])
            draw.rounded_rectangle((x, y, x + width, y + height), radius, fill=element.get('fill'))
        elif tag == 'polyline':
            points = [tuple(float(n) * scale for n in point.split(',')) for point in element.get('points').split()]
            width = round(float(element.get('stroke-width')) * scale)
            color = element.get('stroke')
            draw.line(points, fill=color, width=width, joint='curve')
            for x, y in points:
                radius = width / 2
                draw.ellipse((x-radius, y-radius, x+radius, y+radius), fill=color)
        else:
            raise ValueError('Unsupported SVG primitive: ' + tag)
    image = image.resize((256, 256), Image.Resampling.LANCZOS)
    image.save(assets / 'agent-relay-logo.png')
    image.save(assets / 'agent-relay.ico', sizes=[(n, n) for n in [16, 20, 24, 32, 40, 48, 64, 128, 256]])
    print('Agent Relay icon regenerated (9 sizes).')


if __name__ == '__main__':
    main()
