"""Воспроизводимый перенос экспериментального набора освещения Starlight."""
import concurrent.futures
import json
from pathlib import Path
import re
import urllib.request
import yaml

REVISION = 'fff9583f6280f260d4df3c3a60318131feb54a87'
SOURCE = 'https://raw.githubusercontent.com/ss14Starlight/space-station-14/' + REVISION + '/'
CACHE = Path('dev/vps/.cache/starlight-lighting/import')
TARGET = Path('Resources/Prototypes/_Wega/StarlightLighting')


def fetch(path):
    cached = CACHE / path
    if not cached.exists():
        cached.parent.mkdir(parents=True, exist_ok=True)
        cached.write_bytes(urllib.request.urlopen(SOURCE + path, timeout=45).read())
    return cached.read_bytes()


def main():
    sources = {
        'wall.yml': 'Resources/Prototypes/_Starlight/Entities/Structures/Lighting/base_lighting.yml',
        'floor.yml': 'Resources/Prototypes/_Starlight/Entities/Structures/Lighting/ground_lighting.yml',
        'street.yml': 'Resources/Prototypes/_Starlight/Entities/Structures/Lighting/streetlamp.yml',
        'bulbs.yml': 'Resources/Prototypes/_Starlight/Entities/Objects/Power/lights.yml',
        'floor_graphs.yml': 'Resources/Prototypes/_Starlight/Recipes/Construction/Graphs/Structures/lighting.yml',
        'street_graph.yml': 'Resources/Prototypes/_Starlight/Recipes/Construction/Graphs/Structures/streetlamp.yml',
    }
    texts = {name: fetch(path).decode('utf-8-sig') for name, path in sources.items()}
    ids = set()
    entities = []
    for text in texts.values():
        for proto in yaml.load(text, Loader=yaml.CBaseLoader):
            ids.add(proto['id'])
            if proto['type'] == 'entity':
                entities.append(proto)
    renames = {name: 'WegaStarlight' + name for name in ids}
    renames.update({'LightFloorLayers': 'WegaStarlightLightFloorLayers',
                    'LightFloor': 'WegaStarlightLightFloor'})
    pattern = re.compile(r'(?<![\w])(' + '|'.join(sorted(map(re.escape, renames), key=len, reverse=True)) + r')(?![\w])')
    sprites = {
        '_Starlight/Structures/Lighting/streetlamp.rsi': '_Wega/StarlightLighting/streetlamp.rsi',
        '_Paradise/Structures/Lighting/floorlights.rsi': '_Wega/StarlightLighting/floorlights.rsi',
        '_Paradise/Tiles/light_floor.rsi': '_Wega/StarlightLighting/light_floor.rsi',
    }
    TARGET.mkdir(parents=True, exist_ok=True)
    for name, text in texts.items():
        # Эти компоненты относятся к чужим расам/искрам и отсутствующему старому радиоканалу.
        text = re.sub(r'(?m)^\s*- type: (?:DarkLight|ESSparkOnHit)\s*\n', '\n', text)
        text = re.sub(r'(?m)^    - type: WirelessNetworkConnection\n      range: 200\n', '', text)
        text = re.sub(r'(?m)^    dimmable: false\n', '', text)
        text = re.sub(r'(?m)^    damageContainer: Inorganic\n', '', text)
        text = re.sub(r'(?m)^  (?:name|description): .*\n', '', text)
        # Поле короткого имени стека — LocId, а не произвольный текст.
        text = text.replace('  name: stack-light-tile\n', '  name: wega-starlight-stack-light-tile\n')
        text = text.replace('  suffix: black light', '  suffix: Starlight, violet')
        text = pattern.sub(lambda match: renames[match[0]], text)
        for old, new in sprites.items():
            text = text.replace(old, new)
        if name == 'street.yml':
            text = text.replace('      empty: empty', '      Empty: empty').replace('      off: base', '      Off: base')
            text = text.replace('      on: base', '      On: base').replace('      broken: light_broken', '      Broken: light_broken')
            text = text.replace('      burned: base', '      Burned: base')
        if name == 'floor.yml':
            # Item не имеет in-hand состояний: используем штатные спрайты плитки.
            text = text.replace('      heldPrefix: white', '      sprite: Objects/Tiles/tile.rsi')
            text = text.replace('  id: WegaStarlightFloorTileLight\n',
                                '  id: WegaStarlightFloorTileLight\n  name: wega-starlight-stack-light-tile\n')
        (TARGET / name).write_text('# Starlight ' + REVISION + '; см. README.md и LICENSE*.txt.\n' + text,
                                   encoding='utf-8', newline='\n')

    def asset(pair):
        old, new = pair
        meta = fetch('Resources/Textures/' + old + '/meta.json')
        folder = Path('Resources/Textures') / new
        folder.mkdir(parents=True, exist_ok=True)
        (folder / 'meta.json').write_bytes(meta)
        for state in json.loads(meta)['states']:
            name = state['name'] + '.png'
            (folder / name).write_bytes(fetch('Resources/Textures/' + old + '/' + name))
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as executor:
        list(executor.map(asset, sprites.items()))

    for name in ['LICENSE.TXT', 'LICENSE-Starlight.TXT']:
        (TARGET / name.replace('.TXT', '.txt')).write_bytes(fetch(name))
    print('Imported entity IDs:', len(entities), '; revision:', REVISION)
    # Список для интеграционного теста и документации; генератор не меняет чужие карты.
    (TARGET / 'entities.txt').write_text('\n'.join(renames[p['id']] for p in entities) + '\n', encoding='utf-8')

    colors = {'Warm': 'тёплый', 'Dim': 'тусклый', 'Led': 'светодиодный', 'Cyan': 'голубой',
              'Blue': 'синий', 'Yellow': 'жёлтый', 'Pink': 'розовый', 'Orange': 'оранжевый',
              'Black': 'тёмный', 'Red': 'красный', 'Green': 'зелёный', 'RandomColor': 'случайный цвет'}
    for locale, directory in [('ru-RU', '_wega'), ('en-US', '_Wega')]:
        entries = []
        for entity in entities:
            original = entity['id']
            if 'StreetLamp' in original:
                ru, en = 'уличный фонарь', 'street lamp'
            elif original == 'FloorTileItemLight':
                ru, en = 'плитка светящегося пола', 'light floor tile'
            elif 'LightFloor' in original:
                ru, en = 'светящийся пол', 'light floor'
            elif 'FloorLight' in original:
                ru, en = 'напольный светильник', 'floor light'
            elif original in ('BlackLightTube', 'LightTubeWarm'):
                ru, en = 'лампа-трубка', 'light tube'
            else:
                ru, en = 'настенный светильник', 'wall light'
            suffix = next((value for key, value in colors.items() if original.endswith(key)), '')
            if 'BlackLight' in original:
                suffix = 'фиолетовый'
            if original.endswith('Empty'):
                suffix = 'без лампы'
            if original.endswith('Broken'):
                suffix = 'сломанный'
            if original.endswith('Unfinished'):
                suffix = 'недостроенный'
            if 'AlwaysPowered' in original or original in ('FloorLight', 'LightStreetLamp'):
                suffix = (suffix + ', ' if suffix else '') + 'не требует питания'
            if locale == 'ru-RU':
                title, description = ru, 'Экспериментальное освещение Starlight.'
                suffix = 'Starlight' + (', ' + suffix if suffix else '')
            else:
                title, description = en, 'Experimental Starlight lighting.'
                suffix = 'Starlight' + (', ' + str(entity.get('suffix', '')) if entity.get('suffix') else '')
            entries.append(f'ent-{renames[original]} = {title}\n    .desc = {description}\n    .suffix = {suffix}\n')
        entries.append('wega-starlight-stack-light-tile = ' +
                       ('светящиеся плитки' if locale == 'ru-RU' else 'light floor tiles') + '\n')
        destination = Path('Resources/Locale') / locale / directory / 'starlight_lighting.ftl'
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text('\n'.join(entries), encoding='utf-8', newline='\n')


if __name__ == '__main__':
    main()
