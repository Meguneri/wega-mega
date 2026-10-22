"""Воспроизводимый импорт летнего Пилигрима Corvax NEXT с закреплённой ревизии."""
from pathlib import Path
import re
import urllib.request
import yaml
import argparse
import json
import shutil
from fix_map_tile_variants import normalize

REVISION = 'a27738b7433cf5bfafb384a34b7e3d1023ce967f'
BASE = f'https://raw.githubusercontent.com/space-syndicate/space-station-14-next/{REVISION}/'
CACHE = Path('dev/vps/.cache/pilgrim-summer')
DEST = Path('Resources/Maps/_Wega/PilgrimSummer')
REPLACEMENTS = {
    'AirlockExternalGlassShuttleMining': 'AirlockExternalGlassShuttleLavalandStation',
    'ComputerShuttleMiningLavaland': 'LavalandShuttleConsole',
    'OxygenAndNitrousOxideCanister': 'NitrousOxideOxygenCanister',
    'RandomVendingSnacksHighpop': 'RandomVendingSnacks',
    'CaptainDrobe': 'LockerCaptainFilled',
    'WeatherRainAdmeme': 'WeatherRain',
    'FloorAzureWaterEntity': 'WegaPilgrimSummerWater',
    'FitnessWeightsBench1': 'WegaPilgrimSummerWeightsBench',
    'FitnessWeightLifter': 'WegaPilgrimSummerWeightLifter',
    'FitnessPunchingBag': 'WegaPilgrimSummerPunchingBag',
    'ExecutionChair': 'WegaPilgrimSummerExecutionChair',
    'SpawnPointBoxer': 'SpawnPointServiceWorker',
    'SpawnPointZookeeper': 'SpawnPointServiceWorker',
    'WarpPointBombing': 'WarpPoint',
}


def adapt_map(text):
    text = text.replace('\r\n', '\n')
    for old, new in REPLACEMENTS.items():
        text = re.sub(r'^- proto: ' + re.escape(old) + r'$', '- proto: ' + new, text, flags=re.M)
    # В Wega другие системы окраски шлюзов и беспроводных сетей; маркер переноса гридов не нужен.
    text = re.sub(r'^    - type: (?:MovedGrids|PaintableAirlock|WirelessNetworkConnection|PhysicsMap)\n(?:^      .*\n)*',
                  '', text, flags=re.M)
    aliases = yaml.load(Path('Resources/Prototypes/Tiles/tile_migrations.yml').read_text(encoding='utf-8'),
                        Loader=yaml.CBaseLoader)
    aliases = {a['id']: a['target'] for a in aliases if a.get('type') == 'tileAlias'}
    def tile(match):
        value = match.group(2)
        visited = set()
        while value in aliases:
            if value in visited:
                raise ValueError('Tile alias cycle: ' + value)
            visited.add(value)
            value = aliases[value]
        return match.group(1) + value
    text = re.sub(r'^(  \d+: )(\w+)$', tile, text, flags=re.M)
    # В Wega нет отдельного слоя коллизий Blob NEXT; остальные слои ограждений сохраняются.
    text = re.sub(r'^\s+- BlobImpassable\n', '', text, flags=re.M)
    # Общие поля действий выделены движком из InstantAction в отдельный ActionComponent.
    # Сохраняем исходные UID и контейнеры, чтобы фонари и баллоны получили рабочие действия.
    text = re.sub(r'^    - type: InstantAction\n(?=      (?:originalIconColor|container):)',
                  '    - type: Action\n', text, flags=re.M)
    text = text.replace('parallax: PilgrimAiur', 'parallax: WegaPilgrimSummerWater')
    text = text.replace('/Maps/Next/Shuttles/Pilgrim/', '/Maps/_Wega/PilgrimSummer/')
    return text


def import_content():
    DEST.mkdir(parents=True, exist_ok=True)
    paths = ['Resources/Maps/Next/next_pilgrim.yml']
    for name in ['nt-pd_lodka_ls.yml', 'nt-pd_lodka_rs.yml', 'nt-sec_bobik.yml',
                 'nt-sec_stervyatnik.yml', 'emergency_nextPilgrim.yml']:
        paths.append('Resources/Maps/Next/Shuttles/Pilgrim/' + name)
    for path in paths:
        text = adapt_map(download(path).read_text(encoding='utf-8'))
        text, invalid_variants = normalize(text, fix=True)
        if invalid_variants:
            print(Path(path).name, 'normalized tile variants:', sum(invalid_variants.values()), flush=True)
        name = 'pilgrim_summer.yml' if path.endswith('/next_pilgrim.yml') else Path(path).name
        (DEST / name).write_text(text, encoding='utf-8', newline='\n')
    shutil.copyfile(download('LICENSE-AGPLv3.txt'), DEST / 'LICENSE.txt')
    proto = Path('Resources/Prototypes/Corvax/Maps/Corvax/pilgrim.yml').read_text(encoding='utf-8')
    proto = proto.replace('id: CorvaxPilgrim', 'id: WegaPilgrimSummer')
    proto = proto.replace("mapName: 'Pilgrim'", "mapName: 'Пилигрим (летний)'")
    proto = proto.replace('/Maps/Corvax/corvax_pilgrim.yml', '/Maps/_Wega/PilgrimSummer/pilgrim_summer.yml')
    proto = proto.replace('    Pilgrim:', '    NextPilgrim:')
    proto = proto.replace('/Maps/Corvax/Shuttles/emergency_pilgrim.yml',
                          '/Maps/_Wega/PilgrimSummer/emergency_nextPilgrim.yml')
    Path('Resources/Prototypes/_Wega/Maps/pilgrim_summer.yml').write_text(proto, encoding='utf-8', newline='\n')
    for folder, target, states in [
        ('_Goobstation/Structures/Specific/fitness.rsi', 'fitness.rsi',
         ['punchingbag', 'fitnesslifter', 'fitnessweight']),
        ('_CorvaxNext/Structures/Furniture/execution_chair.rsi', 'execution_chair.rsi', ['execution-chair']),
    ]:
        meta = json.loads(download('Resources/Textures/' + folder + '/meta.json').read_text(encoding='utf-8'))
        meta['states'] = [s for s in meta['states'] if s['name'] in states]
        dest = Path('Resources/Textures/_Wega/PilgrimSummer') / target
        dest.mkdir(parents=True, exist_ok=True)
        (dest / 'meta.json').write_text(json.dumps(meta, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        for state in states:
            shutil.copyfile(download('Resources/Textures/' + folder + '/' + state + '.png'), dest / (state + '.png'))
    water = Path('Resources/Textures/_Wega/PilgrimSummer/azure_water.png')
    shutil.copyfile(download('Resources/Textures/Corvax/Tiles/Planet/azure_water.rsi/shoreline_azure_water.png'), water)
    print('Imported map, five shuttles and licensed textures.', flush=True)


def download(path):
    dest = CACHE / path
    if not dest.exists():
        dest.parent.mkdir(parents=True, exist_ok=True)
        with urllib.request.urlopen(BASE + path, timeout=60) as response:
            dest.write_bytes(response.read())
    return dest


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--import-content', action='store_true')
    args = parser.parse_args()
    if args.import_content:
        import_content()
        raise SystemExit(0)
    source = download('Resources/Maps/Next/next_pilgrim.yml')
    data = yaml.load(source.read_text(encoding='utf-8'), Loader=yaml.CBaseLoader)
    ids = set()
    for path in Path('Resources/Prototypes').rglob('*.yml'):
        ids.update(re.findall(r'^  id: *(\S+)', path.read_text(encoding='utf-8'), re.M))
    protos = {g.get('proto', '') for g in data['entities']} - {'', 'null'}
    migrations = yaml.load(Path('Resources/migration.yml').read_text(encoding='utf-8'), Loader=yaml.CBaseLoader)
    print('Missing prototype candidates (excluding migrations):', sorted(protos - ids - migrations.keys()), flush=True)
    component_names = set()
    for root in ['Content.Shared', 'Content.Server', 'RobustToolbox/Robust.Shared', 'RobustToolbox/Robust.Server']:
        for path in Path(root).rglob('*Component.cs'):
            component_names.add(path.stem.removesuffix('Component'))
    components = {c['type'] for g in data['entities'] for e in g['entities'] for c in e.get('components', [])}
    print('Missing component candidates:', sorted(components - component_names), flush=True)
    print('Entities:', sum(len(g['entities']) for g in data['entities']))
    print('Referenced files:', sorted(set(re.findall(r'/Maps/[^\s\"\']+|/Textures/[^\s\"\']+', source.read_text(encoding='utf-8')))))
