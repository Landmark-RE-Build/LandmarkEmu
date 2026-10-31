#!/usr/bin/env python3
"""Writes the generated reference pages of the GitHub wiki (wiki/ in the repository) from the code.

    python tools/wiki_reference.py            # write the pages into wiki/
    python tools/wiki_reference.py --check    # only report what would change and the consistency findings
    python tools/wiki_reference.py --review   # also print dev command comments that cleaning shortened a lot

Python 3.8+, standard library only. Run it from anywhere: paths are taken relative to this file's repository.

Pages written (everything else in wiki/ is hand-written and never touched):

  Settings-Reference.md         every server setting: the .env name (.env.example), the setting name each service reads
                                (the JSON key, also usable as an environment variable), the default, the live value and
                                what it does; grouped like .env.example. Then the per-service JSON reference from the
                                *Configuration.cs classes, and the settings that have no .env line.
  Command-Documentation.md      the command overview: who may run what, how dev commands are run, the group index.
  Commands-<Group>.md           one page per command group: player slash commands, the retail client's admin slash
                                commands the server answers, and the world server's dev commands (DevCommands.cs).
  Database-Documentation.md     the database overview.
  Database-<Name>.md            one page per database, a section per table with its columns, from the EF Core model
                                snapshots (Migrations/*ModelSnapshot.cs), the model classes' comments, and the raw
                                CREATE TABLE statements of the commerce and world-channel services.

Where the text comes from:
  - Setting descriptions are the comments of .env.example (the public, owner-facing wording); settings with no .env line
    take a short note from EXTRA_SETTING_NOTES below or their class comment.
  - Dev command descriptions are the comment under each `case "name":` in DevCommands.cs; commands without one take
    DEV_EXTRA below. The command groups are DEV_GROUP below: a new command that is not in it lands in the Debug page and
    is reported, so add it there.
  - The admin and player slash commands are listed by hand (ADMIN_SLASH, PLAYER_SLASH): they are the client's own
    commands, recognised by sub-opcode in the server, so there is no table to read them from. The script checks that each
    sub constant it names still exists in the code.
  - Column descriptions are the model properties' doc comments; table notes are TABLE_NOTES below.

Every comment taken from the code is cleaned for the public wiki (internal research references, client addresses and
long quotations are dropped; see clean()), and the finished pages are scanned for local paths, addresses, e-mail
addresses and decompiler names before anything is written (scan_public()). The run fails rather than write a page that
does not pass.
"""

import json
import os
import re
import sys
from collections import OrderedDict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WIKI = os.path.join(ROOT, 'wiki')

findings = []          # consistency notes printed at the end (settings missing from .env.example, unmapped commands)


def read(*parts):
    with open(os.path.join(ROOT, *parts), encoding='utf-8-sig') as f:
        return f.read()


def exists(*parts):
    return os.path.exists(os.path.join(ROOT, *parts))


# ----------------------------------------------------------------------------------------------------------------------
# Cleaning text taken from the code for the public wiki
# ----------------------------------------------------------------------------------------------------------------------

# A parenthesis or sentence holding one of these is internal bookkeeping (research notes that are not in the public
# repository, client addresses, task ids) and is dropped.
INTERNAL = re.compile(
    r'FUN_[0-9a-fA-F]+|DAT_[0-9a-fA-F]+|PTR_\w+|\btrace/|OPEN-LEADS|\bTASKS\b|\baudit[- ]|\baudit\b|agent-|\bL\d{1,3}\b'
    r'|\bP\d{3,4}\b|\+0x[0-9a-fA-F]+|(?<![\w/])re/|\bdesign/|\bHXT\b|FEATURES #|\bharness\b|§|(?<!docs/)\b[\w-]+\.md\b'
    r'|\bforum \d+|\buser\b|\bSpec \d|\bthe [Ll]ua hook\b|\bE\.\d+\b|\bB\.\d+\b|\b[0-9]\.[0-9]{2} trace\b|\[[PIW]( dev)?\]'
    r'|\bCodeStringMappings\b|\bRTTI\b|\bvtable\b|\bGhidra\b|\bFrida\b|\bwriter\b|\breader\b|\bdecompil')

QUOTE = re.compile(r'([:,]?\s*)"([^"]*)"')
LONG_QUOTE_CHARS = 40
LAN_IP = re.compile(r'\b192\.168\.\d{1,3}\.\d{1,3}\b|\b10\.\d{1,3}\.\d{1,3}\.\d{1,3}\b')


def _drop_parentheses(text):
    """Removes parenthesised groups that hold internal references (innermost first)."""
    for _ in range(6):
        changed = re.sub(r'\s*\(([^()]*)\)', lambda m: '' if INTERNAL.search(m.group(1)) else m.group(0), text)
        if changed == text:
            break
        text = changed
    return text


def clean(text, keep_quotes=False, code=False, xml=True):
    """Makes a code comment fit for the public wiki: plain text, no internal references, no long quotations."""
    if not text:
        return ''
    text = re.sub(r'<see cref="([^"]+)"\s*/>', lambda m: '`' + m.group(1).split('.')[-1] + '`', text)
    text = re.sub(r'<paramref name="([^"]+)"\s*/>', r'`\1`', text)
    text = re.sub(r'<c>(.*?)</c>', r'`\1`', text)
    if xml:
        text = re.sub(r'</?(?:para|b|i|list|item|summary|remarks)(?:\s+\w+="[^"]*")*\s*/?>', ' ', text)
    text = text.replace('&lt;', '<').replace('&gt;', '>').replace('&amp;', '&')
    text = re.sub(r'\s*\(e\.g\.\s*(' + LAN_IP.pattern + r')\)', '', text)
    text = LAN_IP.sub('your LAN address', text)
    if not keep_quotes:
        # Quotations in pairs, left to right: a long one (third-party or client text) goes, short names stay.
        text = QUOTE.sub(lambda m: '' if len(m.group(2)) >= LONG_QUOTE_CHARS else m.group(0), text)
    text = _drop_parentheses(text)
    # Sentences that still name something internal are dropped whole.
    sentences = re.split(r'(?<=[.;!?])\s+(?=[A-Z0-9"`(])', text)
    text = ' '.join(s for s in sentences if not INTERNAL.search(s))
    text = re.sub(r'\(\s*[,;:]?\s*\)', '', text)
    text = re.sub(r'\(\s*[,;:]\s*', '(', text)
    text = re.sub(r'\s*[,;:]\s*\)', ')', text)
    text = re.sub(r'\s+([,.;:)])', r'\1', text)
    text = re.sub(r'([(])\s+', r'\1', text)
    text = re.sub(r'\s{2,}', ' ', text).strip()
    text = re.sub(r'[,;:]$', '.', text)
    return text if code else escape_html(text)


def escape_html(text):
    """'<' and '>' outside `code` spans, so GitHub does not take <text> or <Name> for HTML tags."""
    parts = re.split(r'(`[^`]*`)', text)
    return ''.join(p if p.startswith('`') else p.replace('<', '&lt;').replace('>', '&gt;') for p in parts)


def cell(text):
    """Text for a markdown table cell."""
    return (text or '').replace('|', '\\|')


# What the finished pages must never contain (the export to the public repository refuses these too).
PUBLIC_CHECKS = [
    (re.compile(r'\b[A-Za-z]:[\\/](?!Program Files|LandmarkEmu\b)[^\s`|)]*'), 'a local absolute path'),
    (re.compile(r'/[cC]/Users/|[A-Za-z]:\\Users\\', re.I), 'a user folder'),
    (re.compile(r'\b192\.168\.\d+\.\d+\b'), 'a LAN address'),
    (re.compile(r'[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+\.[A-Za-z]{2,}'), 'an e-mail address'),
    (re.compile(r'FUN_[0-9a-fA-F]{6,}|DAT_[0-9a-fA-F]{6,}|\bparam_\d+|\blocal_[0-9a-f]+\b|\bundefined[1248]\b'),
     'decompiler output'),
    (re.compile(r'discord\.(gg|com)', re.I), 'a contact link'),
    (re.compile(r'password=(?!<)[^;\s`|]+', re.I), 'a password'),
]


def scan_public(name, text):
    problems = []
    for number, line in enumerate(text.split('\n'), 1):
        for pattern, what in PUBLIC_CHECKS:
            m = pattern.search(line)
            if m:
                problems.append(f'{name}:{number}: {what}: {m.group(0)[:60]}')
    return problems


# ----------------------------------------------------------------------------------------------------------------------
# Settings
# ----------------------------------------------------------------------------------------------------------------------

# Docker service folder -> (the program's settings file, the configuration class).
SERVICES = OrderedDict([
    ('world', ('WorldServer.json', 'LandmarkEmulator.WorldServer/WorldServerConfiguration.cs')),
    ('auth', ('AuthServer.json', 'LandmarkEmulator.AuthServer/AuthServerConfiguration.cs')),
    ('social', ('SocialServer.json', 'LandmarkEmulator.SocialServer/SocialServerConfiguration.cs')),
    ('commerce', ('CommerceServer.json', 'LandmarkEmulator.CommerceServer/CommerceServerConfiguration.cs')),
    ('envserver', ('EnvServer.json', 'LandmarkEmulator.EnvServer/EnvServerConfiguration.cs')),
    ('gateway', ('GatewayServer.json', 'LandmarkEmulator.GatewayServer/GatewayServerConfiguration.cs')),
    ('worldchannel', ('WorldChannel.json', 'LandmarkEmulator.WorldChannel/WorldChannelConfiguration.cs')),
])

# Nested setting objects and the class that describes them.
NESTED = {
    'NetworkConfig': [('Host', 'string', ''), ('Port', 'ushort', '')],
    'DatabaseConfig': [('Auth__ConnectionString', 'string', ''), ('Auth__Provider', 'enum', 'MySql'),
                       ('Character__ConnectionString', 'string', ''), ('Character__Provider', 'enum', 'MySql'),
                       ('Zone__ConnectionString', 'string', ''), ('Zone__Provider', 'enum', 'MySql'),
                       ('Social__ConnectionString', 'string', ''), ('Social__Provider', 'enum', 'MySql')],
    'CharacterCreateRules': [('StrictName', 'bool', 'false')],
}

# The live value of a setting where the .env.example comment does not say it in the "x = live" form the script reads,
# and "—" for operational settings that have no live counterpart.
LIVE_OVERRIDES = {
    'FREE_BUILD': 'false', 'MOVEMENT_EFFECTS': 'true', 'MELEE_TARGETERS': 'true', 'BLOCK_STANCE_VISIBLE': 'true', 'DEAD_BODIES_VISIBLE': 'true',
    'SAFE_EJECT_RESET': 'true', 'LIVE_CHARACTER_STATS': 'true', 'PROP_STREAM_RANGE': 'not known',
    'TREE_STREAM_RANGE': 'not known (2016 videos: trees to at least 300-450 m)',
    'PLAYER_TELEPORT_REQUESTS': 'true', 'HOME_CONTINENT': '406 (the live capture)', 'TERRAIN_LOD_COPIES': 'true',
    'KEEP_VERTEX_ONLY_VOXELS': '—', 'LANDMARKS_WORLD': 'auto', 'CAVERN_LANDSCAPES': 'all', 'SURFACE_GRAVEYARDS': 'not known',
    'CAVERN_GRAVEYARD_STAMPS': 'true', 'CAVERN_ARRIVAL_RESCUE': 'not known', 'OUTPOST_PRELOAD': '—', 'OUTPOST_ROTATION_HOURS': 'not known',
    'OUTPOST_ROTATION_SHARE': 'not known', 'CREATURE_PATHING': 'true', 'SITE_PATHING': 'true', 'CREATURE_WEAPONS': 'not known',
    'PROCEDURAL_RESPAWN': 'minmax', 'PROCEDURAL_RESPAWN_MIN_SHARE': 'not known', 'PROCEDURAL_PLACEMENT': 'not known',
    'PROCEDURAL_PLACEMENT_DECOR': 'not known', 'PROCEDURAL_PLACEMENT_MASKS': 'not known', 'EVENT_ISLAND': 'auto',
    'EVENT_ISLAND_EXPIRE_ON_CLOSE': 'true', 'MOVE_OLD_PIONEER_SITES': '—', 'PIONEER_SLAB': 'site (a full square over the site, 2016 videos)',
    'PIONEER_SLAB_MATERIAL': 'a grey slate (exact one not known)', 'PIONEER_SURFACE_VEINS': 'flush patches on the ground (needs the exact ground; buried is the closest)',
    'PIONEER_SURFACE_IRON': 'false (from 26 Apr 2016)', 'LANDMARK_SEASON': 'auto', 'HAT_DROPS': 'season', 'FREE_STORE': 'false',
    'LUMENS_PER_DBC': '1.3', 'STORE_PRICES': '(empty)', 'PLAYER_STUDIO_CREATOR_SHARE': '40', 'PLAYER_STUDIO_LISTING_FEE': 'true',
    'PLAYER_STUDIO_APPROVALS': 'not known', 'PLAYER_STUDIO_REJECTIONS': 'not known', 'PLAYER_STUDIO_REVIEWER_MIN_SPEND': '1000',
    'PLAYER_STUDIO_REVIEWS_PER_DAY': '2', 'PLAYER_STUDIO_STAFF_REVIEW': 'true', 'PLAYER_STUDIO_REVIEWER_OVERRULES': '1 (the count is ours)',
    'FOUNDER_ENTITLEMENTS': 'false (live gave them to buyers and winners only)', 'ENTITLEMENT_PACKAGES': '—',
    'ENTITLEMENT_DELIVERY': 'auto', 'LANDSCAPE_CAPACITY': 'not known', 'MEMBERSHIP_PRICE_LUMENS': '— (live sold it for money)',
    'MEMBERSHIP_DAYS': '—', 'TRIAL_KEYS': 'true', 'TRIAL_KEY_DAYS': '10', 'VENDOR_SHOPS': 'false', 'BUILD_MENU_PROP_NAME': 'not known (no source shows it)',
    'CHEST_ACCOUNT_CAP': '0', 'FRIEND_TELEPORT_COOLDOWN_SECONDS': '0', 'TRAVEL_COOLDOWN_SECONDS': '0', 'FRIEND_REQUESTS_MUTUAL': 'true', 'STORY_POSE_PERSISTS': 'true',
    'PLACE_OPENS_BUILD_MODE': 'false', 'EMOTES_PUT_AWAY_HELD_ITEM': 'false', 'YELL_IS_EMOTE': 'true','DAY_CYCLE_SPEED': '8', 'SHOWCASE_FOLLOW_DIGEST': 'true',
    'SHOWCASE_DIGEST_HOUR_UTC': 'not known', 'FINDER_RETURN_HOME': 'not known', 'CAVERN_TRIPS_OTHER_WORLDS': 'true',
    'SHORT_TELEPORT_DISTANCE': '—', 'ITEM_DEFINITION_CHUNK_SIZE': '0 (one packet)', 'SHORT_TELEPORT_WAIT_SCREEN': 'not known',
    'MEMBER_MONTHLY_LUMENS': '0', 'MEMBER_STORE_DISCOUNT': '0', 'STORE_TRADE_WAIT_HOURS': '12 (up to)',
    'OVERFLOW_EXPIRY_HOURS': '24', 'MAIL_MAX_STACK': '9000', 'ITEM_AUTO_RESUPPLY': 'true', 'GRIEF_AWARDS': 'true',
    'GRIEF_DAMAGE_PER_POINT': 'not known', 'GRIEF_LEVEL_POINTS': 'not known', 'GRIEF_DECAY_PER_HOUR': 'not known',
    'GRIEF_SUSPENSION_MINUTES': 'not known', 'MAX_PLAYERS': 'not known (live queued logins)',
    'RESTART_SCHEDULE': 'Mon,Sat 03:00 (from June 2016)', 'RESTART_TIME_ZONE': 'America/Los_Angeles',
    'RESTART_WARNING_SECONDS': 'not known', 'RESTART_MESSAGE': 'not known', 'SHUTDOWN_BROADCASTS': 'false',
    'PENDING_KICK_PACKET': 'true', 'TARGET_NOT_ONLINE_PACKET': 'not known', 'CRAFTING_WINDOW_CLOSE_AWAY': 'true',
    'WORLD_NAME_ID': '—', 'CLIENT_WORLD_NAME_TEXT': '0 (each world its own name)', 'CLIENT_WORLD_ID': '—',
    'STRICT_NAMES': 'not known', 'CHARACTER_SELECT_FRIENDS': 'true', 'ENVSERVER_HEAL_QUEUE': 'true',
    'FINDER_RETURN_HOME ': 'not known', 'GATEWAY_MULTI_WORLD': 'auto', 'SOCIAL_PRESENCE_GRACE_SECONDS': 'not known',
}

# Descriptions written here where the .env.example comment is mostly quotations of the client's own messages.
ENV_DESC_OVERRIDES = {
    'STORE_TRADE_WAIT_HOURS': "Store purchases (bought for Lumens) can't be traded, mailed or put in a chest for this many "
                              "hours: live's \"fraud timer\" (the client has the matching refusal messages, and the item "
                              "tooltip counts the time down). 12 = a developer post of 26 Mar 2014 (\"up to 12 hours after "
                              "purchase\"); 0 = tradeable at once.",
}

# Docker variables used by the compose files but not listed in .env.example: what they are. All internal: the image tags
# exist so a separate test stack builds its own images and never replaces the running ones; owners leave them alone.
EXTRA_DOCKER_VARIABLES = {
    'WORLD_IMAGE': 'Internal (test stacks): the image tag the world service builds (default `landmarkemu-world`), so a '
                   'second stack can use its own.',
    'AUTH_IMAGE': 'Internal (test stacks): the auth server image tag (default `landmarkemu-auth`).',
    'SOCIAL_IMAGE': 'Internal (test stacks): the social service image tag (default `landmarkemu-social`).',
    'COMMERCE_IMAGE': 'Internal (test stacks): the commerce service image tag (default `landmarkemu-commerce`).',
    'ENVSERVER_IMAGE': 'Internal (test stacks): the Environment Server image tag (default `landmarkemu-envserver`).',
    'GATEWAY_IMAGE': 'Internal (test stacks): the gateway image tag (default `landmarkemu-gateway`).',
    'WORLDCHANNEL_IMAGE': 'Internal (test stacks): the world-channel service image tag (default `landmarkemu-worldchannel`).',
}

# Settings in the code that have no .env line on purpose (deployment details the container layout fixes, values that
# must not change, alternative forms of a .env line): shown as internal in "Settings without a .env line" and not
# reported by --check. Keyed by "File Key" for one program, or by the key alone for every program.
INTERNAL_SETTINGS = {
    'Network__Host': 'Internal: the address the program listens on. Docker leaves it at every interface inside the '
                     'container; the port comes from the .env port line.',
    'Database__Auth__Provider': 'Internal: always `MySql` (MySQL / MariaDB), the only database provider.',
    'Database__Character__Provider': 'Internal: always `MySql` (MySQL / MariaDB), the only database provider.',
    'WorldServer.json StoreLumenPrices': 'Internal form: owner store prices by item id as a JSON object or '
                                         '`StoreLumenPrices__<item id>` variables; with Docker use the one-line STORE_PRICES.',
    'AuthServer.json WebApi__Enabled': 'Internal: runs the account / launcher web API; the Docker auth service always does.',
    'AuthServer.json WebApi__LoginPort': "Internal under Docker: the login port LaunchPad sends the game to (0 = the login "
                                         "server's own port). Docker publishes LOGIN_PORT on the same number inside and "
                                         "outside, so 0 is right; change LOGIN_PORT instead.",
    'AuthServer.json Internal__Host': "Internal: the login service's internal listener (gateways, world channels), reached "
                                      'only over the Docker network.',
    'AuthServer.json Internal__Port': "Internal: the login service's internal port (20210; others reach it as "
                                      'LOGIN_INTERNAL_ADDRESS); never published.',
    'GatewayServer.json Database__Auth__ConnectionString': 'Internal: only for a gateway run outside Docker without '
                                                           'LoginInternalAddress; the Docker gateway asks the login service.',
    'GatewayServer.json Database__Character__ConnectionString': 'Internal: only for a gateway run outside Docker without '
                                                                'LoginInternalAddress.',
    'GatewayServer.json EncryptionKey': "Internal: the retail client's built-in RC4 key; any other value and no client can "
                                        "connect.",
}

# Environment variables read directly by the code that are not owner settings.
INTERNAL_ENVIRONMENT = {
    'GATEWAY_INTERNAL_ADDRESS': 'internal: the older spelling of GatewayInternalAddress (Docker sets that key in '
                                'docker/gateway/world.override.yml)',
    "ZONE_INTERNAL_PORT": "internal: set by docker/gateway/world.override.yml (the zone's port for the world channel)",
    'LANDMARK_NOSTREAM': 'internal test switch: 1 = no trees, plants or creatures are streamed',
}


def internal_note(json_name, key):
    return INTERNAL_SETTINGS.get(f'{json_name} {key}') or INTERNAL_SETTINGS.get(key)

# Short notes for settings that have no .env line (their class comments are written for developers).
EXTRA_SETTING_NOTES = {
    'Network__Host': 'The address the program listens on (0.0.0.0 = every interface).',
    'Network__Port': 'The port the program listens on.',
    'Internal__Host': 'The address of the internal (service-to-service) listener.',
    'Internal__Port': 'The port of the internal listener; never published outside the Docker network.',
    'GameDataPath': "The folder with the game's data tables (Docker: the setup service fills it from GAME_DIR).",
    'GameDirectory': 'The Landmark install, read only (cave pieces, templates, biome maps).',
    'StoreLumenPrices': 'Owner store prices by item id, as a JSON object or `StoreLumenPrices__<item id>` variables; '
                        'STORE_PRICES is the one-line form.',
    'HealthFile': 'A file the program touches while its main loop runs, for the container health check (empty = none).',
    'WebPagesHost': 'The address the in-game web pages listen on (empty = loopback; Docker 0.0.0.0).',
    'WebPagesUrl': 'The address of the in-game web pages that clients are given (empty = http://127.0.0.1:WebPagesPort).',
    'GatewayInternalAddress': '"host:port" of a gateway running as its own process; empty = the world runs the gateway '
                              'itself on its game port (Docker: docker/gateway/world.override.yml sets it).',
    'EncryptionKey': "The RC4 key the game client uses (the client's built-in key; do not change).",
    'WorldId': 'The world (its server-list id) this service belongs to.',
    'Database': 'The connection string of the service\'s own database.',
    'ImportFrom': 'Where the one-time import on the first start copies data from (empty = no copy).',
    'DataPath': 'The folder for the files the service keeps.',
    'WorldAddress': 'Forces one world address for every client after character select; empty = each world registers its own.',
    'WebApi__Enabled': 'Run the account / launcher web API.',
    'WebApi__BaseURI': 'The address and port the web API listens on.',
    'WebApi__LoginPort': 'The login port LaunchPad sends the game to (0 = the login server\'s own port).',
    'WebApi__NewsFile': "The JSON file with LaunchPad's news (re-read on every request).",
    'WebApi__TrustedProxy': 'The address of the account website proxy, the only one allowed to name the player\'s address.',
}


def parse_env_example():
    """.env.example -> sections [{title, blocks: [{lines, comment, vars: [(name, value, commented)]}]}]."""
    sections, section, lines, block = [], None, [], None
    for raw in read('.env.example').split('\n'):
        line = raw.rstrip()
        m = re.match(r'^# ---- (.+?) -+$', line)
        if m:
            section = {'title': m.group(1).strip(), 'blocks': []}
            sections.append(section)
            lines, block = [], None
            continue
        if section is None:
            continue
        var = re.match(r'^(#)?([A-Z][A-Z0-9_]*)=(.*)$', line)
        if var:
            if block is None or lines:
                block = {'lines': lines, 'comment': ' '.join(l.strip() for l in lines), 'vars': []}
                section['blocks'].append(block)
                lines = []
            block['vars'].append((var.group(2), var.group(3).strip(), bool(var.group(1))))
        elif line.startswith('#'):
            lines.append(line[2:] if line.startswith('# ') else line[1:])
        elif not line.strip():
            block = None if lines else block
    return sections


def comment_markdown(lines):
    """A .env.example comment as markdown: text paragraphs, '  - ' bullets, indented example lines as code."""
    out, text, code = [], [], []

    def flush_text():
        if text:
            out.append(clean(' '.join(text)))
            text.clear()

    def flush_code():
        if code:
            out.append('```\n' + '\n'.join(code) + '\n```')
            code.clear()

    for line in lines:
        if re.match(r'^\s{2,}-\s', line):
            flush_text(); flush_code()
            out.append('- ' + clean(line.strip()[2:]))
        elif re.match(r'^\s{2,}\S', line):
            flush_text()
            code.append(line.strip())
        else:
            flush_code()
            text.append(line.strip())
    flush_text(); flush_code()
    joined = ''
    for part in out:
        if joined:
            joined += '\n' if part.startswith('- ') and joined.split('\n')[-1].startswith('- ') else '\n\n'
        joined += part
    return joined


ROLE_LABELS = {'ports': 'published port', 'volumes': 'mounted folder', 'networks': 'network name / alias',
               'image': 'image tag', 'expose': 'internal port', 'command': 'start command', 'ipam': 'network range',
               'compose': 'compose file', 'services': 'compose file', 'config': 'network range'}

VAR_RE = re.compile(r'\$\{([A-Z][A-Z0-9_]*)(?:(:?[-?])((?:[^{}]|\$\{[^}]*\})*))?\}')


def parse_compose():
    """Every ${VAR} use in docker-compose.yml and docker/*/: VAR -> [(service, key or role, default)]."""
    uses = OrderedDict()
    files = ['docker-compose.yml'] + sorted(
        os.path.join('docker', d, f) for d in os.listdir(os.path.join(ROOT, 'docker'))
        if os.path.isdir(os.path.join(ROOT, 'docker', d))
        for f in os.listdir(os.path.join(ROOT, 'docker', d)) if f.endswith('.yml'))
    for rel in files:
        service_dir = rel.split(os.sep)[1] if rel.startswith('docker' + os.sep) else 'project'
        if rel.endswith('world.override.yml'):
            service_dir = 'world'
        section = ''
        for raw in read(rel).split('\n'):
            if raw.lstrip().startswith('#'):
                continue
            m = re.match(r'^    (\w+):', raw)
            if m:
                section = m.group(1)
            key = re.match(r'^\s{6}([A-Za-z_][\w]*):\s*(.*)$', raw)
            for v in VAR_RE.finditer(raw):
                name, default = v.group(1), v.group(3)
                if v.group(2) and '?' in v.group(2):
                    default = ''          # ${VAR:?message}: required, the text is an error message
                if section == 'environment' and key:
                    role = key.group(1)
                else:
                    role = section or 'compose'
                uses.setdefault(name, []).append((service_dir, role, default, rel.replace(os.sep, '/')))
    return uses


def strip_nested_types(text):
    """Removes the bodies of types nested in the file's first class (their properties are not top-level keys)."""
    outer = re.search(r'public\s+(?:sealed\s+)?(?:struct|class)\s+\w+[^{]*\{', text)
    if not outer:
        return text
    head, body = text[:outer.end()], text[outer.end():]
    while True:
        m = re.search(r'public\s+(?:struct|class)\s+\w+[^{;]*\{', body)
        if not m:
            return head + body
        depth, i = 1, m.end()
        while i < len(body) and depth:
            depth += {'{': 1, '}': -1}.get(body[i], 0)
            i += 1
        body = body[:m.start()] + body[i:]


def parse_config_class(rel):
    """Public settable properties of a configuration class: name -> (type, default text, summary)."""
    text = read(rel)
    # Properties of nested types (AuthServerConfiguration.CharacterCreateRules) belong to their parent key.
    text = strip_nested_types(text)
    props = OrderedDict()
    pattern = re.compile(
        r'((?:\s*///[^\n]*\n)*)\s*public\s+([\w.<>, ]+?)\s+(\w+)\s*\{\s*get;\s*(?:private\s+)?set;\s*\}\s*(?:=\s*([^;]+);)?')
    for m in pattern.finditer(text):
        doc, typ, name, default = m.group(1), m.group(2).strip(), m.group(3), (m.group(4) or '').strip()
        summary = ' '.join(l.strip()[3:].strip() for l in doc.strip().split('\n') if l.strip().startswith('///'))
        props[name] = (typ, default, summary)
    return props


def code_default(typ, default):
    if default:
        d = default.strip()
        if d == 'new()':
            return '(empty)'
        if d.startswith('new()') or d.startswith('new ') and '{' in d:
            return d
        d = re.sub(r'(?<=\d)[fdmuUL]+$', '', d)
        if d == 'DefaultWorldNameId':
            return '560916'
        if d.startswith('"'):
            d = d.strip('"')
            return d if d else '(empty)'
        if d == 'new()':
            return '(empty)'
        return d
    return {'bool': 'false', 'string': '(empty)', 'int': '0', 'uint': '0', 'ulong': '0', 'double': '0', 'float': '0'}.get(
        typ, '(empty)' if 'List' in typ or 'Dictionary' in typ else '—')


def direct_reads():
    """Settings the world server reads straight from the configuration: key -> (default, file)."""
    reads = OrderedDict()
    pattern = re.compile(r'(?:Configuration|config)\??\s*\.GetValue(?:<(\w+)>)?\("([A-Z]\w+)"(?:,\s*([^)]+?))?\)')
    for project in ('LandmarkEmulator.WorldServer', 'LandmarkEmulator.EnvServer.Core', 'LandmarkEmulator.Shared'):
        for folder, _, files in os.walk(os.path.join(ROOT, project)):
            if os.sep + 'obj' in folder or os.sep + 'bin' in folder:
                continue
            for f in files:
                if f.endswith('.cs'):
                    for m in pattern.finditer(read(os.path.join(folder, f))):
                        default = (m.group(3) or '').strip().strip('"')
                        default = re.sub(r'(?<=\d)[fdm]$', '', default)
                        rel = os.path.relpath(os.path.join(folder, f), ROOT).replace(os.sep, '/')
                        reads.setdefault(m.group(2), (default or '(empty)', rel))
    return reads


def environment_only():
    found = OrderedDict()
    for folder, _, files in os.walk(ROOT):
        if any(p in folder for p in (os.sep + 'obj', os.sep + 'bin', os.sep + '.git', os.sep + 'wiki')):
            continue
        for f in files:
            if f.endswith('.cs'):
                rel = os.path.relpath(os.path.join(folder, f), ROOT).replace(os.sep, '/')
                for m in re.finditer(r'GetEnvironmentVariable\("([A-Z_][A-Z0-9_]*)"\)', read(rel)):
                    found.setdefault(m.group(1), rel)
    return found


def live_value(name, comment):
    if name in LIVE_OVERRIDES:
        return LIVE_OVERRIDES[name]
    m = re.search(r'(?:^|[\s(;,])([\w.,"-]+?)\s*=\s*(?:as\s+)?live\b', comment)
    if m:
        return m.group(1).strip('"(),;')
    return '—'


def settings_page():
    sections = parse_env_example()
    compose = parse_compose()
    out = []
    w = out.append
    w('# Settings Reference')
    w('')
    w('> Generated by `tools/wiki_reference.py` from `.env.example`, the compose files in `docker/` and the')
    w('> `*Configuration.cs` classes. Edit those (or the script), not this page.')
    w('')
    w('Every server program reads its settings from a JSON file next to the program (`WorldServer.json`, `AuthServer.json`')
    w('...; each project ships a `*.example.json` with every key), then from **environment variables with the same names**,')
    w('which override the file, then from the command line. A nested key is written with `__` in a variable')
    w('(`Network__Port`, `Database__Auth__ConnectionString`, `Admins__0`).')
    w('')
    w('With Docker (see [Server Installation](Server-Installation)) you only edit **`.env`**: each compose file forwards a')
    w('`.env` line to the setting the program reads. The tables below give, for every `.env` line, the setting each service')
    w('receives (`service: Key`), the default, the value live Landmark used, and what it does.')
    w('')
    w('- **Default** is the value `.env.example` ships with; the program and compose defaults are the same unless noted.')
    w('- **Live** is how the official servers behaved: `true` / a value when that is known from the game\'s sources, *not')
    w('  known* when no source says, and *—* for settings with no live counterpart (ports, passwords, our own extras).')
    w('  Every gameplay setting defaults to live behaviour; extras beyond live are off.')
    w('- Settings marked *(commented out)* are written `#NAME=value` in `.env.example`: remove the `#` to use one.')
    w('')
    w('Contents: ' + ' · '.join(f'[{s["title"]}](#{anchor(s["title"])})' for s in sections)
      + ' · [Per-service JSON reference](#per-service-json-reference) · [Settings without a .env line](#settings-without-a-env-line)')
    w('')
    documented = set()
    for s in sections:
        w(f'## {s["title"]}')
        w('')
        for block in s['blocks']:
            names = [v[0] for v in block['vars']]
            documented.update(names)
            title = ', '.join(f'`{n}`' for n in names[:4]) + (' …' if len(names) > 4 else '')
            w(f'### {title}')
            w('')
            desc = ENV_DESC_OVERRIDES.get(names[0]) or comment_markdown(block['lines'])
            if desc:
                w(desc)
                w('')
            w('| `.env` | Service setting | Default | Live |')
            w('|---|---|---|---|')
            for name, value, commented in block['vars']:
                targets = []
                for service, role, default, _ in compose.get(name, []):
                    label = f'{service}: `{role}`' if role not in ROLE_LABELS else f'{service}: {ROLE_LABELS[role]}'
                    if service == 'project' and role not in ROLE_LABELS:
                        continue
                    if label not in targets:
                        targets.append(label)
                if name == 'COMPOSE_PROFILES':
                    targets = ['Docker Compose itself (which optional services start)']
                shown = f'`{value}`' if value else '(empty)'
                if 'PASSWORD' in name:
                    shown = '(empty: you must set it)'
                note = ' *(commented out)*' if commented else ''
                live = live_value(name, block['comment'])
                w(f'| `{name}`{note} | {"<br>".join(sorted(targets)) or "—"} | {cell(shown)} | {cell(live)} |')
            w('')
    # Docker variables the compose files use that .env.example doesn't list.
    extra = [n for n in compose if n not in documented]
    if extra:
        w('## Other Docker variables')
        w('')
        w('Used by the compose files but not listed in `.env.example` (they have working defaults; set them in `.env` only if')
        w('you need to).')
        w('')
        w('| Variable | Used by | Default | What it is |')
        w('|---|---|---|---|')
        for n in extra:
            uses = compose[n]
            where = sorted(set(f'{u[0]}: `{u[1]}`' if u[1] not in ('image', 'volumes', 'networks', 'ports') else f'{u[0]}: {u[1]}'
                               for u in uses))
            default = next((u[2] for u in uses if u[2]), '')
            if '${' in default:
                default = '(see note)'
            shown = f'`{default}`' if default and default != '(see note)' else (default or '(empty)')
            w(f'| `{n}` | {"<br>".join(where)} | {shown} | {EXTRA_DOCKER_VARIABLES.get(n, "—")} |')
            if n not in EXTRA_DOCKER_VARIABLES:
                findings.append(f'Docker variable {n} is used by a compose file but not in .env.example (no note in the script).')
        w('')

    # Per-service reference from the configuration classes.
    w('## Per-service JSON reference')
    w('')
    w('Every key each program reads, from its configuration class (and, for the world server, the keys read directly from')
    w('the configuration). "Docker" names the `.env` variable that sets it, or the fixed value the compose file gives it.')
    w('')
    missing_rows = []
    reads = direct_reads()
    for service, (json_name, rel) in SERVICES.items():
        props = parse_config_class(rel)
        envs = {}
        for name, uses in compose.items():
            for svc, role, default, _ in uses:
                if svc == service:
                    envs.setdefault(role, []).append(name)
        fixed = fixed_compose_values(service)
        example = example_json(json_name)
        w(f'### {json_name}')
        w('')
        w(f'Class `{os.path.basename(rel)[:-3]}` (`{rel}`).')
        w('')
        w('| Key | Type | Default | Docker |')
        w('|---|---|---|---|')
        rows = []
        for name, (typ, default, summary) in props.items():
            nested = NESTED.get(typ) or (NESTED['NetworkConfig'] if typ == 'NetworkConfig' else None)
            if typ == 'WebApiConfig':
                for sub, (styp, sdef, ssum) in parse_config_class('LandmarkEmulator.WebAPI/Configuration/WebApiConfig.cs').items():
                    rows.append((f'WebApi__{sub}', styp, code_default(styp, sdef), ssum))
            elif nested:
                defaults = dict(re.findall(r'(\w+)\s*=\s*"?([\w.]+)"?', default)) if default else {}
                for sub, styp, sdef in nested:
                    if typ == 'DatabaseConfig' and sub.split('__')[0] not in DATABASE_KEYS.get(service, ()):
                        continue
                    value = defaults.get(sub, sdef) or example.get(f'{name}__{sub}', '') or '(empty)'
                    rows.append((f'{name}__{sub}', styp, mask(value), ''))
            else:
                value = code_default(typ, default)
                if value == '(empty)' and example.get(name) not in (None, '', '[]', '{}'):
                    value = example[name] + ' (example file)'
                rows.append((name, typ, value, summary))
        if service == 'world':
            for key, (default, where) in reads.items():
                if key not in props and key != 'GameDataPath':
                    rows.append((key, 'read directly', default, ''))
        for key, typ, default, summary in rows:
            docker = envs.get(key) or envs.get(key.replace('__0', ''))
            if key == 'Admins':
                docker = ['ADMIN_ACCOUNT', 'ADMIN_2', 'ADMIN_3'] if service == 'world' else None
            if docker:
                docker_cell = ', '.join(f'`{d}`' for d in docker)
            elif key in fixed:
                docker_cell = f'fixed: `{mask(fixed[key])}`' if fixed[key] else 'fixed: (empty)'
            else:
                docker_cell = '—'
                if not key.startswith('Database__') or service in ('world', 'auth', 'gateway'):
                    missing_rows.append((json_name, key, typ, default, summary))
            default = mask(default) if key != 'EncryptionKey' else "(the client's built-in key)"
            shown_default = default if len(default) < 60 else default[:57] + '…'
            w(f'| `{key}` | `{typ.replace("System.Collections.Generic.", "")}` | `{shown_default}` | {docker_cell} |'.replace('`(empty)`', '(empty)'))
        w('')

    # Settings without any .env path.
    w('## Settings without a .env line')
    w('')
    w('These keys exist in the code but no `.env` line sets them: they keep their default under Docker unless you add the')
    w('environment variable to the service yourself (a `docker-compose.override.yml`), or set them in the JSON file when')
    w('you run a program without Docker.')
    w('')
    w('| File | Key | Default | What it is |')
    w('|---|---|---|---|')
    for json_name, key, typ, default, summary in missing_rows:
        if key.startswith('Database__') and ('Zone' in key or 'Social' in key):
            continue
        internal = internal_note(json_name, key)
        note = internal or EXTRA_SETTING_NOTES.get(key) or first_sentence(clean(summary)) or '—'
        w(f'| {json_name} | `{key}` | `{mask(default)}` | {cell(note)} |'.replace('`(empty)`', '(empty)'))
        if not internal:
            findings.append(f'Setting {json_name} {key} is in the code but not reachable from .env.example.')
    w('')
    env_only = environment_only()
    w('### Environment variables read directly')
    w('')
    w('Read with `Environment.GetEnvironmentVariable`, not through the settings files.')
    w('')
    w('| Variable | Read by | In .env.example |')
    w('|---|---|---|')
    for name, rel in env_only.items():
        shown = 'yes' if name in documented else f'no ({INTERNAL_ENVIRONMENT[name]})' if name in INTERNAL_ENVIRONMENT else 'no'
        w(f'| `{name}` | `{rel}` | {shown} |')
        if name not in documented and name not in INTERNAL_ENVIRONMENT:
            findings.append(f'Environment variable {name} ({rel}) is not in .env.example.')
    w('')
    w('See also: [Server Guide](Server-Guide), [Command Documentation](Command-Documentation) (several settings can be')
    w('changed while the world runs with a dev command), and `docs/docker.md` in the repository.')
    w('')
    return '\n'.join(out)


DATABASE_KEYS = {'world': ('Auth', 'Character', 'Zone'), 'auth': ('Auth', 'Character'), 'gateway': ('Auth', 'Character'),
                 'social': ('Social',)}


def example_json(json_name):
    """The project's <name>.example.json flattened to Key__Sub -> text (empty when there is none)."""
    project = {'WorldServer.json': 'LandmarkEmulator.WorldServer', 'AuthServer.json': 'LandmarkEmulator.AuthServer',
               'SocialServer.json': 'LandmarkEmulator.SocialServer', 'CommerceServer.json': 'LandmarkEmulator.CommerceServer',
               'EnvServer.json': 'LandmarkEmulator.EnvServer', 'GatewayServer.json': 'LandmarkEmulator.GatewayServer',
               'WorldChannel.json': 'LandmarkEmulator.WorldChannel'}[json_name]
    rel = os.path.join(project, json_name.replace('.json', '.example.json'))
    if not exists(rel):
        return {}
    flat = {}

    def walk(prefix, value):
        if isinstance(value, dict):
            for k, v in value.items():
                if not k.startswith('__'):
                    walk(f'{prefix}__{k}' if prefix else k, v)
        else:
            flat[prefix] = json.dumps(value) if isinstance(value, (list, bool)) else str(value)

    walk('', json.loads(read(rel)))
    return flat


def fixed_compose_values(service):
    """Compose environment keys of a service with a fixed value (no ${VAR})."""
    values = {}
    rel_files = [os.path.join('docker', service, 'docker-compose.yml')]
    if service == 'world':
        rel_files.append(os.path.join('docker', 'gateway', 'world.override.yml'))
    for rel in rel_files:
        if not exists(rel):
            continue
        in_env = False
        for raw in read(rel).split('\n'):
            if re.match(r'^    environment:', raw):
                in_env = True
                continue
            if re.match(r'^    \w', raw):
                in_env = False
            m = re.match(r'^\s{6}([A-Za-z_]\w*):\s*(.+)$', raw)
            if in_env and m and not raw.lstrip().startswith('#'):
                values[m.group(1)] = m.group(2).strip().strip('"')
    return values


def mask(value):
    """Connection strings without their passwords."""
    value = re.sub(r'password=\$\{[^}]*\}', 'password=<DB_PASSWORD>', value)
    return re.sub(r'password=(?!<)[^;]*', 'password=<password>', value, flags=re.I)


def first_sentence(text):
    m = re.match(r'(.+?[.!?])(\s|$)', text)
    return m.group(1) if m else text


def anchor(title):
    a = title.lower()
    a = re.sub(r'[^\w\- ]', '', a)
    return a.replace(' ', '-')


# ----------------------------------------------------------------------------------------------------------------------
# Commands
# ----------------------------------------------------------------------------------------------------------------------

GROUPS = OrderedDict([
    ('Character', 'Your character: health, stats, movement, achievements, appearance.'),
    ('Items', 'Items, inventory, equipment, crafting, chests and loot.'),
    ('Travel', 'Teleports, landscapes, the Spire, Chaos Caverns and outposts.'),
    ('Building', 'Build sites, props, designs (templates), copy and paste.'),
    ('Terrain', 'Voxels, digging, terrain cells, ore veins and the terrain store.'),
    ('World', 'The world clock, trees and gathering, creatures in the open world, events.'),
    ('Social', 'Emotes, duels, /who, friends, guilds and mail.'),
    ('Commerce', 'The Showcase store, Player Studio, contests, membership, vendors.'),
    ('Combat', 'Weapons, creatures, combat states and creature pathing.'),
    ('Games', 'Site games (instanced games) and the story tools.'),
    ('Server', 'Running the server: shutdown, kicks, broadcasts, limits, the admin menu.'),
    ('Debug', 'Developer probes and test switches. Not needed to run a server.'),
])

DEV_GROUP = {}
for group, names in {
    'Character': 'hurt revive reviveby god respawn setres calcstats charstats achreset objective resettitles clearunlocks fte '
                 'anim emotelead movement movementeffects safeeject portraittest weartint grief cancelbuff pos',
    'Items': 'give giveoverflow take coins finditem iteminfo equipped wield unwield itemstats lootbag lootfree activate use '
             'sortbag setwear durabilityof autoresupply itemaudit combatgear weapons abilities resupply founderchest vault '
             'vaultnear chestopen chestput chestpickup craft closecraft cancelcraft station',
    'Travel': 'tp tpo tpz tpto tpclaim drop spire cave cavetrip outpost landscape landscapes terminal graveyard tree chest',
    'Building': 'claimhere ensureclaim unclaim sitename expiresites lapsesite claims claimbasic beginclaim grantaccess sitemusic '
                'paletteslot prop giveprop propremove propdelete proptint propmenu propenable propmove propname proptext '
                'propread propshowtext propset propcost beginprop placeat clearprops propstream copyhere pasteat offgridpaste '
                'undo redo placedesign exportdesign designoffer psinstances pioneerslab pioneer launch',
    'Terrain': 'carve carvefwd dig vertexonly veinsweep lodbuild matname cellstats clearcell terrainmem regendelay nearvein '
               'veins wall ownblock cavecheck cavelands outpostrotate',
    'World': 'timeofday clock treepools treefall treestream outpostwake outpostuse eventexpire barrenrecipes pinata fxat composite',
    'Social': 'sysmail guildof ratevote',
    'Commerce': 'psoffer pstake psfee storespent psreview followdigest studiotake storetoken vendoropen contestphase contests '
                'contestenter',
    'Combat': 'fire projectile hitnear struck slay clearcreatures creature kobold npc creatures attackinfo weaponattack status '
              'states meleetargeters blockvisible blockstances pathing sitepathing pathquery sight creatureweapon casterinfo',
    'Games': 'game gamejoin gameleave gameedit gamestart gametp finderwaiting gamepoint gamescore gameend story storyfire '
             'storyfaction storyweapons storylink storyclick npctext playmode',
    'Server': 'shutdown broadcast kick maxplayers landscapecap friendtpcooldown gc',
    'Debug': 'send testcell livecell blockdmg blockcell digmode rimmode ringscale lua luacode luas sysmsg uievent lastmove '
             'whoami stream cellcompare observed propslide harvestfail veintest stampdry isair caveair durability portraitcrc '
             'abilitystats weaponkeywords ambients pinatafor spirefull gamelabels npcmode rezonecave guildpacket mailpacket '
             'itemslot slotitems',
}.items():
    for n in names.split():
        DEV_GROUP[n] = group

# Dev commands without a comment in DevCommands.cs: (syntax, what it does).
DEV_EXTRA = {
    'give': ('give <item id> <count>', 'Gives items to the player (into the bag; what does not fit goes to overflow).'),
    'send': ('send <hex bytes>', 'Sends a raw zone packet, opcode bytes included, to the player. For protocol tests only.'),
    'ringscale': ('ringscale <scale>', "Scales the gathering tools' harvest area and re-sends the player's calculated stats."),
    'rimmode': ('rimmode <n>', 'Test switch for how the rim of a dug hole is smoothed.'),
    'creatures': ('creatures', 'Writes the creatures around the player to the server log.'),
    'weaponkeywords': ('weaponkeywords [off]', 'Test switch: send weapon keywords with weapon attack info or not.'),
    'lastmove': ('lastmove', "Writes the player's last full movement packet to the log."),
    'whoami': ('whoami', "Writes the player's guid, landscape, position and target to the log."),
    'unclaim': ('unclaim', "Abandons the player's build site, as Build Site Management's abandon does."),
    'claims': ('claims', 'Sends the player every build site of the landscape again.'),
    'gamejoin': ('gamejoin', 'Joins the site game the player stands in.'),
    'gameleave': ('gameleave', 'Leaves the site game.'),
    'gametp': ('gametp [off]', "Switches the site games' spawn teleports on or off."),
    'spirefull': ('spirefull [off]', "Sends the Spire props' full data at once instead of waiting for the client to ask."),
    'gamelabels': ('gamelabels [off]', 'Switches the labels over site-game control points on or off.'),
    'story': ('story', "Describes the story-tool setup around the player (story props, links, creatures) in chat."),
    'storyclick': ('storyclick <guid hex>', 'As if the player clicked that story prop or story creature.'),
    'gamepoint': ('gamepoint', "Describes the site game's control points in chat."),
    'contests': ('contests', 'Lists the contests and their phases in chat.'),
    'gamescore': ('gamescore <points>', "Adds points to the player's score in the running site game."),
    'gameend': ('gameend', 'Ends the running site game round now.'),
    'station': ('station <station id>', "Opens that crafting station's window wherever the player is."),
    'prop': ('prop <prop id> [dx dy dz]', 'Places that prop next to the player (default 2 m along x) as a placed prop.'),
    'chestpickup': ('chestpickup', 'Picks up the nearest storage chest, as the prop menu does.'),
}

# Descriptions written here instead of the code comment, where that comment is mostly internal research notes.
DEV_TEXT = {
    # The client-code commands: their code comments name server internals; the limits matter to owners.
    'lua': "Runs a Lua function of the player's game client with up to three numbers. It reaches only the admin who runs "
           "it and other admins, and may not name a function that opens the player's desktop web browser, unless "
           "DEV_CLIENT_CODE_ON_OTHERS is true. Every use is logged.",
    'luacode': "Runs a line of Lua code in the player's game client. Same limits as `lua`.",
    'luas': "Calls a Lua function of the player's game client with text arguments. Same limits as `lua`.",
    'send': 'Sends a raw zone packet, opcode bytes included, to the player. For protocol tests only; same limits as `lua`.',
    'testcell': 'Replaces one terrain cell with a flat test mesh and sends it to the players (terrain format tests).',
    'activate': "Uses one held item as the bag's Use does (recipe items, storage expansions, unlockers, prop placement).",
    'clock': 'Shows the world clock in chat and in the log.',
    'durability': "Sends a carried item's consumed-count field (the client calls it ConsumedCount, not durability). Test only.",
    'ensureclaim': 'As `claimhere`, but only for players without a build site (for tests that need one).',
    'expiresites':'Expires those build sites as upkeep expiry does (removed for everyone, a design saved, the terrain '
                   'reverted, props and resources mailed to the owner). Deletes builds: back up first.',
    'propslide':"Sends a movement for a placed prop towards its spot plus the offset, to see whether the client moves a "
                 "placed prop that way. The server's record is unchanged.",
}

# The retail client's admin slash commands that the server answers (docs/admin.md). Each entry: (group, syntax, what the
# server does, the sub constant(s) in AdminCommands*.cs that handle it, or None).
ADMIN_SLASH = [
    ('Travel', '/goto <name>  (also /goto player <name>, /warp)', 'Teleports the admin beside an online player (partial names '
     'work), changing landscape when needed.', ['GotoSub']),
    ('Travel', '/goto waypoint [name]', 'Logged only: the server keeps no player waypoints yet.', ['GotoWaypointSub']),
    ('Travel', '/location <x> <y> <z> [heading], the camera teleport, the world map\'s admin teleport',
     'Moves the admin to the exact point (TeleportToLocation). The same handler serves the Escape menu\'s Evac to Safety '
     'for everyone.', None),
    ('Server', '/target <name>  (also /gm target)', 'Looks the character up (online or not) and sets it as the client\'s '
     '"admin target", which /goto, /profile and the /item commands then use. `/target clear` is handled by the client.',
     ['VerifyTargetSub']),
    ('Server', '/hideme [on|off]', 'Hides the admin from every other player (removed from their view, not introduced to '
     'arrivals, left out of /who) until `/hideme off`.', ['HideMeSub']),
    ('Server', '/admin', 'Opens the admin menu (a page in the in-game browser): give items, travel, building, time of day, '
     'creatures, shutdown, System Broadcast, kicks, the Player Studio staff queue, the server monitor and "Any command" '
     '(any dev command, for yourself, one player or everyone). Bound for admins when they enter the world.', None),
    ('Character', '/run <n> | /run inc <n> | /run dec <n> | /run default', 'Sets the admin\'s movement speed in metres per '
     'second (default 12) and re-sends the stats; `default` (or 0) removes the override.', ['RunspeedSub']),
    ('Character', "The client's grief admin commands (set damage level, set grief level, reset)", 'Act on the admin '
     'themselves: set the grief damage points or level (1-3 warnings, 4 weapon lock, 5 suspension) or clear it. Saved with the '
     'character. The exact command words are the client\'s and are not listed here.', ['GriefAdminSub']),
    ('World', '/servertime (or /zonetime) hour <h> | pause | sync | warp <rate>', 'Moves, pauses or resumes, or changes the '
     'speed of the world clock, and re-syncs everyone online.', ['GameTimeSub']),
    ('World', '/clienttime set | hour | pause | sync | warp | advance | reverse', 'Changes only the admin\'s own client clock '
     'until its next time sync (the server echoes it back; unknown modes and NaN values are refused).', ['ClientTimeSub']),
    ('Building', '/prop <prop id>', 'Places a free prop at the point under the crosshair, owned by the admin, saved and '
     'shown to the landscape.', ['SpawnPropSub']),
    ('Building', '/prop reset_zone_and_wipe_db_forever_with_no_undo', 'Refused, and logged with who tried.', ['DeleteAllPropsSub']),
    ('Social', '/friend forceadd <target> [source]', "Adds the target to the source's friends list without a friend request "
     "(the admin's own list when no source is given; the source may be offline). The admin is told the outcome.", ['ForceAddFriendSub']),
    ('Social', '/friend remove <target> [source]', "Removes the target from the source's friends list, as if the source had "
     'removed them. The admin is told the outcome (removed, or not on the list).', ['ForceRemoveFriendSub']),
    ('Items', '/item add <item> [count] [tint] [name|guid]', 'Gives items to the target (the named player, else the admin '
     'target, else the admin). Tints are reported but not kept.', ['AddItemSub']),
    ('Items', '/item delete <item guid> [name|guid], /item undelete <item guid> [name|guid]', 'Deletes a stack; undelete '
     'gives it back while the world keeps running.', ['DeleteItemSub']),
    ('Items', '/item list [name|guid]', "Lists every stack of the target's, by container, in chat.", ['GetItemListSub']),
    ('Items', '/item check <item> [name|guid]', 'Tells how many of that item the target has, and where.', ['GetItemInfoSub']),
    ('Items', '/item random <item> [catalyst] [count]', 'Gives the item itself (this server\'s items have no rolled '
     'variants).', ['RandomItemSub']),
    ('Items', '/item vault | unvault | collect | uncollect | vaultcollect | vaultuncollect | overflow | unoverflow | buyback | '
     'unbuyback <item> [name|guid]', 'Moves the item\'s stacks between the bag and that container. Buyback and the vault '
     'collection have no storage in the 0.695 client, so those two are refused with the reason.',
     ['VaultItemSub', 'CollectionItemSub', 'VaultCollectionItemSub', 'OverflowItemSub', 'BuybackItemSub']),
    ('Debug', '/setmodel id', 'Shows you (or your admin target) as that model to everyone on the landscape until '
     '`/setmodel 0` or a relog.', ['SetModelSub']),
    ('Debug', '/vehicle, /ammo, /weaponset, /placedeffect, free-fly hide, /profile …', 'Decoded and logged with '
     'their fields; the systems behind them (vehicles, ammo, weapon sets, placed effects, job profiles) are '
     'not built.', ['SpawnVehicleSub', 'AddAmmoSub', 'WeaponSetSub', 'PlacedEffectSub', 'FreeFlyHideSub', 'ProfileSub']),
    ('Combat', 'The build tool "PathFindVisualize" (two clicks)', 'Runs the server\'s creature pathing between the two points '
     'and answers in chat (straight-line verdict, reached / partial / none, waypoints, length). The retail client has no '
     'palette entry for the tool; the dev command `pathquery` sends the same request.', ['DebugPathQuerySub']),
    ('Debug', 'Admin monitor requests (pathing data, zone player count, Storybricks diagnostics)', 'Logged at most once a '
     'minute per admin and kind; the retail client never sends them and has no reader for a reply.',
     ['PathingDataMonitorSub', 'ZonePlayerCountSub', 'StorybricksDiagnosticsSub']),
]

# Server commands a player types. (group, syntax, who, what it does)
PLAYER_SLASH = [
    ('Social', '/emotelist', 'Everyone', 'Lists the emote commands in chat.'),
    ('Social', '/emoteclip <0-33>', 'Everyone', 'Plays emote clip n. (/emote, /em and /me are the free-text emote line of the game itself.)'),
    ('Social', '/<emote word>  (EMOTE_WORDS)', 'Everyone', 'Plays that emote, with its chat lines ("You wave." / '
     '"[name] waves.", the "[target]" forms with a target). Moods (angry, happy, sad; `/calm` ends them) colour the idle '
     'stance and later emotes; stances (dance, kneel, lie, playdead) loop until `/stand`, another stance or walking away; '
     'the walk stances (skip, drunk, sneak) last while walking, until `/stand`; `/talking` talks until `/stop`. Players '
     'who meet you later see your stance and mood too.'),
    ('Social', '/playdead', 'Everyone (0.695 clients)', 'The Play Dead stance, with its chat lines (with your target when '
     'you have one).'),
    ('Social', '/duel <name>', 'Everyone', 'Challenges a player to a duel (a world-channel encounter).'),
    ('Social', '/duelaccept, /dueldecline', 'Everyone', 'Answers a duel challenge.'),
    ('Social', '/duelleave', 'Everyone', 'Leaves the duel you are in.'),
    ('Social', '/who [text]', 'Everyone', 'Lists the players online on this world whose name contains the text (all of '
     'them without one; at most 50; hidden admins left out).'),
    ('Commerce', '/membership', 'Everyone', 'Shows your membership and the offer.'),
    ('Commerce', '/membership buy', 'Everyone', 'Buys MEMBERSHIP_DAYS days of membership for MEMBERSHIP_PRICE_LUMENS Lumens '
     '(not offered when the price is 0).'),
    ('Commerce', '/membership grant <name> <days>', 'Admins', "Adds days of membership to an online player's account."),
    ('Commerce', '/membership revoke <name>', 'Admins', "Ends an online player's membership."),
    ('Debug', '/gatewayprofiler on | off | dump', 'Everyone (own connection only)', "Counts this connection's packets by "
     'kind and direction and prints them as system lines (the gateway\'s own command; live\'s was an admin tool).'),
]

WHO_DEV = 'Console / admins'


def parse_dev_commands():
    src = read('LandmarkEmulator.WorldServer', 'Game', 'DevCommands.cs').split('\n')
    commands, i = [], 0
    case = re.compile(r'^\s{24}case "([^"]+)":')
    while i < len(src):
        m = case.match(src[i])
        if not m:
            i += 1
            continue
        names, j = [m.group(1)], i + 1
        while j < len(src) and case.match(src[j]):
            names.append(case.match(src[j]).group(1))
            j += 1
        comment, k = [], j
        while k < len(src):
            s = src[k].strip()
            if s == '{':
                k += 1
                continue
            if s.startswith('//'):
                comment.append(s[2:].strip())
                k += 1
                continue
            break
        commands.append((names, ' '.join(comment), i + 1))
        i = j
    return commands


def split_usage(names, comment):
    """'name args: what' -> (syntax, what)."""
    if not comment:
        return None
    for name in names + ['@Name ' + names[0]]:
        if comment.startswith(name):
            m = re.match(r'^(.+?):\s+(.*)$', comment)
            if m and len(m.group(1)) < 140:
                return m.group(1), m.group(2)
            return comment, ''
    return None


def emote_words():
    text = read('LandmarkEmulator.WorldServer', 'Game', 'Emotes.cs')
    # The word table's entries: a number or a constant (["lie"] = Lie); the chat-line table's `= new(...)` are not words.
    return re.findall(r'\["(\w+)"\]\s*=\s*(?:\d+|[A-Z]\w*)\b(?!\s*\()', text)


def check_subs():
    text = ''.join(read('LandmarkEmulator.WorldServer', 'Game', f) for f in
                   ('AdminCommands.cs', 'AdminCommands.Items.cs', 'AdminCommands.Monitor.cs'))
    for _, syntax, _, subs in ADMIN_SLASH:
        for sub in subs or []:
            if not re.search(r'\b' + sub + r'\s*=', text):
                findings.append(f'Admin slash command "{syntax}": sub constant {sub} not found in AdminCommands*.cs.')


def command_pages():
    pages = OrderedDict()
    dev = parse_dev_commands()
    check_subs()
    words = emote_words()
    rows = {g: {'player': [], 'admin': [], 'dev': []} for g in GROUPS}
    index = []
    for group, syntax, who, what in PLAYER_SLASH:
        syntax = syntax.replace('EMOTE_WORDS', 'the words below')
        rows[group]['player'].append((syntax, who, what))
        index.append((index_name(syntax), group))
    for group, syntax, what, subs in ADMIN_SLASH:
        rows[group]['admin'].append((syntax, 'Admins', what))
        index.append((index_name(syntax), group))
    for names, comment, line in dev:
        group = DEV_GROUP.get(names[0])
        if group is None:
            group = 'Debug'
            findings.append(f'Dev command "{names[0]}" (DevCommands.cs line {line}) has no group in DEV_GROUP: listed under Debug.')
        parsed = split_usage(names, comment)
        if names[0] in DEV_EXTRA and (parsed is None or not parsed[1]):
            syntax, what = DEV_EXTRA[names[0]]
        elif parsed:
            syntax, what = parsed
        else:
            syntax, what = names[0], ''
            findings.append(f'Dev command "{names[0]}" has no description (add one to DEV_EXTRA).')
        raw_what = what
        what = DEV_TEXT.get(names[0]) or clean(what, xml=False)
        what = what[:1].upper() + what[1:]
        if '--review' in sys.argv and len(what) < 0.6 * len(raw_what):
            print(f'review {names[0]}: {raw_what}\n    -> {what}')
        if len(names) > 1 and names[-1] in DEV_EXTRA and names[-1] not in comment:
            what += ' ' + DEV_EXTRA[names[-1]][0] + ': ' + DEV_EXTRA[names[-1]][1]
        who = WHO_DEV
        if names[0] == 'kick':
            who += ' (needs `@Name`)'
        if names[0] == 'pathquery':
            who += ' (the target must be an admin)'
        rows[group]['dev'].append((' / '.join(names), clean(syntax, keep_quotes=True, code=True, xml=False).replace('\\|', '|'), who, what or '—'))
        for n in names:
            index.append((n, group))

    # Overview page.
    out = []
    w = out.append
    w('# Command Documentation')
    w('')
    w('> Generated by `tools/wiki_reference.py` from `DevCommands.cs`, `Emotes.cs` and the lists in the script. Edit those,')
    w('> not this page.')
    w('')
    w('LandmarkEmu has three kinds of commands:')
    w('')
    w('1. **Player slash commands** that the server adds to the game client (`/who`, `/duel`, the emotes, `/membership`).')
    w('   Anyone can use them.')
    w("2. **The retail client's admin slash commands** (`/goto`, `/target`, `/hideme`, `/item`, `/servertime` ...). The client")
    w('   offers them only to a player the server marks as an admin, and the server checks again for every command: a')
    w("   non-admin's command (only a modified client could send one) is refused and logged. `/admin` opens the admin menu.")
    w("3. **Dev commands**: the world server's own command set (`Game/DevCommands.cs`), for running and testing a server.")
    w('   They are run from the server side, never typed in game chat (see below).')
    w('')
    w('## Who is an admin')
    w('')
    w('The world settings `ADMIN_ACCOUNT`, `ADMIN_2`, `ADMIN_3` and `ADMINS` (comma separated) name the admins (see')
    w('[Settings Reference](Settings-Reference#admin_account-admin_2-admin_3-admins)). Write each entry as')
    w('`account:<account name>` (every character of that account) or `character:<character name>` (that character, on')
    w('any account). A bare name still works for either, but is ignored while an account and another player\'s character')
    w('share it, so nobody can become admin by registering an admin\'s name in the other namespace. Admin rights are')
    w('per world: each world reads its own list. An admin sees "You are an admin: type /admin to open the admin menu." when')
    w('entering the world.')
    w('')
    w('## Running dev commands')
    w('')
    w('- **dev_commands.txt.** Append a line to `dev_commands.txt` in the world\'s data folder (Docker:')
    w('  `server-data/worlds/<WORLD_KEY>/dev_commands.txt`; without Docker, next to the world server program). The world')
    w('  reads the file about once a second, runs each line and empties the file. Lines starting with `#` are ignored.')
    w('- **The admin menu.** `/admin` in game → *Any command* runs a dev command for yourself, one player or everyone. The')
    w('  menu\'s buttons are dev commands too. Commands run on the next server tick and every run is logged with the admin\'s')
    w('  name; results appear in chat or in the world\'s log (`docker compose logs -f world`, or `console.log` in the data')
    w('  folder).')
    w('- **Targets.** A line without a prefix runs once **for every player online**; `@Name command` runs it for that')
    w('  character only. A few commands ignore the target (switches such as `propstream`, `pathing`), and `kick` must have')
    w('  an `@Name`.')
    w('- **Who may run them.** Anyone who can write to the world\'s data folder (the server owner), and admins through the')
    w('  admin menu. Treat both as full control of the server: dev commands can give items, move and remove players, run Lua')
    w('  in their game client and send them raw packets.')
    w('- Arguments in `<angle brackets>` are required, `[square brackets]` optional, `a|b` means one of them. Item, prop,')
    w('  recipe and other ids are the game\'s own ids (the client\'s data tables); `guid hex` values are as the server log')
    w('  prints them.')
    w('')
    w('Example `dev_commands.txt` lines:')
    w('')
    w('```')
    w('broadcast The server restarts in 10 minutes.')
    w('@Alice give 231 10')
    w('@Bob kick 30 Please read the server rules.')
    w('timeofday 12')
    w('```')
    w('')
    w('## The other server programs')
    w('')
    w('- **Login server console** (only when it runs in a terminal; Docker disables it): `account create <username>')
    w("  <password>` creates an account (the sign-up's rules apply: 3-32 letters, digits or underscores, a password of at")
    w('  least 6 characters; it works while sign-ups are closed). Players normally create accounts in LaunchPad or on the')
    w('  sign-up website (see [Account Creation](Account-Creation)).')
    w('- The social, commerce, gateway, world-channel and Environment Server programs have no commands; they are')
    w('  configured through their settings only.')
    w('')
    w('## Groups')
    w('')
    w('| Group | What it covers | Player | Admin | Dev |')
    w('|---|---|---|---|---|')
    for g, about in GROUPS.items():
        r = rows[g]
        w(f'| [{g}](Commands-{g}) | {about} | {len(r["player"])} | {len(r["admin"])} | {len(r["dev"])} |')
    w('')
    w('## Every command')
    w('')
    seen = OrderedDict()
    for name, group in sorted(index, key=lambda x: x[0].lstrip('/').lower()):
        seen.setdefault(name, group)
    w(' · '.join(f'[`{n}`](Commands-{g})' for n, g in seen.items()))
    w('')
    pages['Command-Documentation.md'] = '\n'.join(out)

    for g, about in GROUPS.items():
        r = rows[g]
        out = []
        w = out.append
        w(f'# Commands: {g}')
        w('')
        w(f'> Generated by `tools/wiki_reference.py`. {about} Back to [Command Documentation](Command-Documentation).')
        w('')
        if r['player']:
            w('## Player commands')
            w('')
            w('| Command | Who | What it does |')
            w('|---|---|---|')
            for syntax, who, what in r['player']:
                w(f'| `{esc(syntax)}` | {who} | {what} |')
            if g == 'Social':
                w('')
                w('Emote words: ' + ', '.join(f'`/{x}`' for x in words) + ', `/playdead`.')
            w('')
        if r['admin']:
            w('## Admin slash commands')
            w('')
            w("The retail client's own commands, available to admins only (see [Who is an admin](Command-Documentation#who-is-an-admin)).")
            w('')
            w('| Command | Who | What the server does |')
            w('|---|---|---|')
            for syntax, who, what in r['admin']:
                w(f'| `{esc(syntax)}` | {who} | {what} |')
            w('')
        if r['dev']:
            w('## Dev commands')
            w('')
            w('Run from `dev_commands.txt` or the admin menu\'s *Any command* (see [Running dev commands]'
              '(Command-Documentation#running-dev-commands)). "Console / admins" = the server owner through the file, or an admin.')
            w('')
            w('| Command | Syntax | Who | What it does |')
            w('|---|---|---|---|')
            for name, syntax, who, what in sorted(r['dev']):
                w(f'| `{name}` | `{esc(syntax)}` | {who} | {what} |')
            w('')
        pages[f'Commands-{g}.md'] = '\n'.join(out)
    return pages


INDEX_LABELS = {"The client's grief":'grief admin commands', 'The build tool': 'PathFindVisualize',
                'Admin monitor': 'admin monitor requests', '/<emote': '/<emote word>'}


def index_name(syntax):
    for start, label in INDEX_LABELS.items():
        if syntax.startswith(start):
            return label
    return syntax.split()[0].rstrip(',')


def esc(text):
    return text.replace('|', '\\|').replace('`', "'")


# ----------------------------------------------------------------------------------------------------------------------
# Databases
# ----------------------------------------------------------------------------------------------------------------------

# Tables a world keeps in its own database (lmemu_world_<WORLD_KEY>) when it has one: CharacterDatabase.NewWorldContext.
WORLD_TABLES = ['claim', 'claim_permission', 'claim_vendor_item', 'chest_item', 'world_prop', 'world_prop_binding',
                'world_prop_setting', 'world_area', 'site_template_instance']

TABLE_NOTES = {
    'account': 'One row per account: the SRP-6a login data (salt and verifier; no password is stored), the session key '
               'LaunchPad gets, the single-use server ticket for the world login, and membership.',
    'zone_server': 'The server list: one row per world, written by each world at start and kept alive by its heartbeat '
                   '(players online, the limit, its address).',
    'character': 'One row per character: name, account, appearance, position, landscape, currencies and per-character '
                 'state. Deleting a character sets `deleteTime` and clears the name (the old name is kept).',
    'character_customisation': 'Appearance choices per customisation slot.',
    'character_item': 'Every item stack a character owns, by container and slot.',
    'character_action_slot': 'Hotbar slots.',
    'character_friend': 'Friends lists (the social service has its own copy when it runs).',
    'character_ignore': 'Ignore lists.',
    'character_inventory_filter': 'Saved bag filters.',
    'character_preference': 'Client preference documents the client stores on the server.',
    'character_objective': 'Achievement and daily-goal progress.',
    'character_palette_entry': 'Radial (palette) menu entries.',
    'character_recipe': 'Known recipes.',
    'character_unlock': 'Unlocks of every kind (titles, outfits, emotes, designs taken, votes …), one row per (kind, value).',
    'claim': 'Build sites: owner, box, landscape, name, permissions, site settings, Play Mode.',
    'claim_permission': 'Special Access entries: a permission mask per (site, character).',
    'claim_vendor_item': 'Vendor booth stock (used when VENDOR_SHOPS is on).',
    'chest_item': 'The contents of placed storage chests and vaults.',
    'world_prop': 'Placed props: definition, owner, site, position, rotation, scale, tint, name and text.',
    'world_prop_binding': 'Story-tool Links between props (event → action).',
    'world_prop_setting': "Per-prop settings from a prop's settings window.",
    'world_area': 'Trigger volumes made with the Area tool (Linking Areas).',
    'site_template_instance': 'Player Studio designs placed on a site (so visitors cannot copy over them).',
    'gallery_comment': "Comments on build sites' gallery entries.",
    'guild': 'Guilds.',
    'guild_member': 'Guild membership and rank, one row per character.',
    'mail': 'In-game mail with attachments.',
    'template': 'Saved designs (templates): voxels and props of a copied build.',
    'player_studio_product': 'Player Studio products (designs for sale) when commerce runs inside the world.',
    'player_studio_review': 'Peer reviews of Player Studio products.',
    'showcase_follow': 'Showcase following: creators and tags a player follows.',
    'showcase_digest_run': 'When the daily following mail last ran.',
}

DATABASES = OrderedDict([
    ('Auth', {
        'schema': 'lmemu_auth',
        'about': 'Accounts and the server list. Shared by every world. Used by the login server (AuthServer, its web API) and '
                 'read by the worlds and gateways for the login checks.',
        'source': ('snapshot', 'LandmarkEmulator.Database.Auth/Migrations/AuthContextModelSnapshot.cs',
                   'LandmarkEmulator.Database.Auth/Model'),
        'project': 'Project-Database-Auth',
    }),
    ('Character', {
        'schema': 'lmemu_character',
        'about': 'The characters and everything that travels with them (items, recipes, unlocks, friends, guilds, mail, '
                 'designs, Player Studio). Shared by every world: one character visits every world, as on live Landmark. A '
                 'world without a database of its own also keeps its build sites, props and chests here.',
        'source': ('snapshot', 'LandmarkEmulator.Database.Character/Migrations/CharacterContextModelSnapshot.cs',
                   'LandmarkEmulator.Database.Character/Model'),
        'project': 'Project-Database-Character',
    }),
    ('World', {
        'schema': 'lmemu_world_<WORLD_KEY>',
        'about': "One per world (Docker: `lmemu_world_main` for the first one). The world's own data: build sites, placed "
                 'props, chests, vendor stock, prop links and areas. It is created with the same tables as the character '
                 'database (the same EF Core model and migrations), but only the world tables below are used.',
        'source': ('world', None, None),
        'project': 'Project-Database-Character',
    }),
    ('Social', {
        'schema': 'lmemu_social',
        'about': 'The shared social service\'s own database (optional, compose profile `social`): presence, friends, '
                 'ignore lists, guilds and mail for every world at once.',
        'source': ('snapshot', 'LandmarkEmulator.Database.Social/Migrations/SocialContextModelSnapshot.cs',
                   'LandmarkEmulator.Database.Social/Model'),
        'project': 'Project-Database-Social',
    }),
    ('Commerce', {
        'schema': 'lmemu_commerce',
        'about': 'The shared commerce service\'s own database (optional, compose profile `commerce`): Player Studio, '
                 'contests, portraits and Showcase following for every world. Created by the service with plain SQL at '
                 'start (no EF Core); columns added later are added at start when missing.',
        'source': ('sql', 'LandmarkEmulator.CommerceServer/CommerceStore.cs', None),
        'project': 'Project-CommerceServer',
    }),
    ('World-Channel', {
        'schema': 'lmemu_wc_<WORLD_KEY>',
        'about': "One per world, used only when the world's world-channel service runs in its own container "
                 '(docker/worldchannel): the build-site likes it answers itself.',
        'source': ('sql', 'LandmarkEmulator.WorldChannel/VoteStore.cs', None),
        'project': 'Project-WorldChannel',
    }),
])

SQL_COLUMN_NOTES = {
    'commerce_world': 'The worlds that have linked to the service: their realm (character database) and name.',
    'product': 'Player Studio products, with the design snapshot shown on the Showcase (`snap_*`).',
    'product_review': 'Peer-review verdicts, one per (product, reviewer).',
    'taken_design': 'Who bought or took which design.',
    'contest': 'Contests and their phase dates (Unix seconds).',
    'contest_entry': 'Build sites entered in a contest.',
    'contest_vote': 'Contest votes.',
    'portrait': 'Character portrait and headshot files the service keeps.',
    'following': 'Showcase following of creators.',
    'following_tag': 'Showcase following of tags.',
    'studio_payout': "The creator's share of a sale, waiting to be mailed through a world of the creator's realm.",
    'digest_run': 'When the daily following mail ran, per realm.',
    'digest_mail': 'Following mails waiting to be delivered.',
    'commerce_import': 'One-time imports already done (so they never run twice).',
    'claim_vote': 'Build-site likes: one row per (voter, site).',
}


def model_docs(folder):
    """Model classes' doc comments: (class) -> summary and (class, property) -> summary."""
    docs = {}
    if not folder:
        return docs
    for f in sorted(os.listdir(os.path.join(ROOT, folder))):
        if not f.endswith('.cs'):
            continue
        text = read(folder, f)
        cls = re.search(r'((?:\s*///[^\n]*\n)*)\s*public\s+(?:sealed\s+)?class\s+(\w+)', text)
        if cls:
            docs[cls.group(2)] = doc_text(cls.group(1))
        for m in re.finditer(r'((?:\s*///[^\n]*\n)+)\s*public\s+[\w.<>?, \[\]]+\s+(\w+)\s*\{\s*get;', text):
            docs[(cls.group(2) if cls else f[:-3], m.group(2))] = doc_text(m.group(1))
    return docs


def doc_text(block):
    return ' '.join(l.strip()[3:].strip() for l in block.strip().split('\n') if l.strip().startswith('///'))


def call_argument(chain, method):
    """The argument text of `.method(...)` in a fluent chain (balanced parentheses), or None."""
    m = re.search(r'\.' + method + r'\(', chain)
    if not m:
        return None
    depth, i = 1, m.end()
    while i < len(chain) and depth:
        depth += {'(': 1, ')': -1}.get(chain[i], 0)
        i += 1
    return chain[m.end():i - 1]


def parse_snapshot(rel):
    """EF Core model snapshot -> {table: {'class', 'columns': [...], 'key', 'indexes', 'fks'}}."""
    text = read(rel)
    tables = OrderedDict()
    by_class = {}
    for m in re.finditer(r'modelBuilder\.Entity\("([\w.]+)", b =>\s*\{(.*?)\n\s{16}\}\);', text, re.S):
        full, body = m.group(1), m.group(2)
        cls = full.split('.')[-1]
        table = re.search(r'b\.ToTable\("(\w+)"', body)
        entry = by_class.get(cls)
        if entry is None:
            entry = {'class': cls, 'columns': [], 'key': [], 'indexes': [], 'fks': []}
            by_class[cls] = entry
        if table:
            tables[table.group(1)] = entry
        identity = set(re.findall(r'UseMySqlIdentityColumn\(b\.Property<[^>]+>\("(\w+)"\)\)', body))
        for p in re.finditer(r'^\s*b\.Property<([\w?\[\]]+)>\("(\w+)"\)(.*?);', body, re.S | re.M):
            typ, prop, chain = p.group(1), p.group(2), p.group(3)
            column = re.search(r'HasColumnName\("(\w+)"\)', chain)
            ctype = re.search(r'HasColumnType\("([^"]+)"\)', chain)
            required = 'IsRequired()' in chain
            nullable = typ.endswith('?') or (typ in ('string', 'byte[]') and not required)
            dv = call_argument(chain, 'HasDefaultValueSql') or call_argument(chain, 'HasDefaultValue') or ''
            dv = re.sub(r'^\(\w+\)\s*', '', dv).strip().strip('"')
            dv = re.sub(r'(?<=\d)(ul|u|UL|L|f|d|m)$', '', dv)
            entry['columns'].append({'prop': prop, 'name': column.group(1) if column else prop,
                                     'type': ctype.group(1) if ctype else typ, 'null': nullable, 'default': dv,
                                     'auto': prop in identity})
        key = re.search(r'b\.HasKey\(([^)]*)\)', body)
        if key:
            entry['key'] = re.findall(r'"(\w+)"', key.group(1))
        for ix in re.finditer(r'b\.HasIndex\(([^)]*)\)(.*?);', body, re.S):
            name = re.search(r'HasDatabaseName\("(\w+)"\)', ix.group(2))
            entry['indexes'].append((re.findall(r'"(\w+)"', ix.group(1)), name.group(1) if name else '',
                                     'IsUnique()' in ix.group(2)))
        for fk in re.finditer(r'b\.HasOne\("([\w.]+)", "\w+"\)(.*?);', body, re.S):
            cols = re.search(r'HasForeignKey\(([^)]*)\)', fk.group(2))
            delete = re.search(r'DeleteBehavior\.(\w+)', fk.group(2))
            entry['fks'].append((fk.group(1).split('.')[-1], re.findall(r'"(\w+)"', cols.group(1)) if cols else [],
                                 delete.group(1) if delete else ''))
    # Columns by property name for keys / indexes.
    return tables


def parse_sql(rel):
    """CREATE TABLE statements built from C# string literals, plus EnsureColumn() additions."""
    text = read(rel)
    tables = OrderedDict()
    for m in re.finditer(r'Execute\(\w+,\s*((?:"(?:[^"\\]|\\.)*"\s*\+?\s*)+)(?:\+\s*Table)?\)', text):
        sql = ''.join(re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(1)))
        t = re.match(r'CREATE TABLE IF NOT EXISTS (\w+) \((.*)\)', sql.strip())
        if not t:
            t = re.match(r'CREATE TABLE IF NOT EXISTS (\w+) \((.*)\)\s*ENGINE', sql.strip())
            if not t:
                continue
        body = t.group(2)
        body = re.sub(r'\)\s*ENGINE.*$', '', body)
        parts, depth, cur = [], 0, ''
        for ch in body:
            if ch == '(':
                depth += 1
            elif ch == ')':
                depth -= 1
            if ch == ',' and depth == 0:
                parts.append(cur.strip())
                cur = ''
            else:
                cur += ch
        if cur.strip():
            parts.append(cur.strip())
        cols, key, indexes = [], [], []
        for p in parts:
            if p.upper().startswith('PRIMARY KEY'):
                key = [c.strip() for c in re.search(r'\((.*)\)', p).group(1).split(',')]
            elif p.upper().startswith('KEY '):
                km = re.match(r'KEY (\w+) \((.*)\)', p)
                indexes.append(([c.strip() for c in km.group(2).split(',')], km.group(1), False))
            else:
                cm = re.match(r'(\w+)\s+(.*)$', p)
                if cm:
                    spec = cm.group(2)
                    pk = 'PRIMARY KEY' in spec.upper()
                    if pk:
                        key = [cm.group(1)]
                    typ = re.match(r'([A-Z]+(?:\(\d+\))?(?: UNSIGNED)?)', spec.upper())
                    default = re.search(r"DEFAULT ('[^']*'|\S+)", spec)
                    cols.append({'name': cm.group(1), 'type': typ.group(1).lower() if typ else spec,
                                 'null': 'NOT NULL' not in spec.upper(), 'default': default.group(1).strip("'") if default else '',
                                 'auto': 'AUTO_INCREMENT' in spec.upper(), 'prop': None})
        tables[t.group(1)] = {'columns': cols, 'key': key, 'indexes': indexes, 'fks': [], 'class': None}
    for m in re.finditer(r'EnsureColumn\(\w+,\s*\w+,\s*"(\w+)",\s*"(\w+)",\s*"([^"]+)"\)', text):
        table, col, spec = m.groups()
        if table in tables:
            default = re.search(r"DEFAULT ('[^']*'|\S+)", spec)
            tables[table]['columns'].append({'name': col, 'type': spec.split(' NOT')[0].split(' NULL')[0].lower(),
                                             'null': 'NOT NULL' not in spec.upper(),
                                             'default': default.group(1).strip("'") if default else '', 'prop': None,
                                             'added': True})
    return tables


def database_pages():
    pages = OrderedDict()
    character_tables = parse_snapshot(DATABASES['Character']['source'][1])
    overview = []
    w = overview.append
    w('# Database Documentation')
    w('')
    w('> Generated by `tools/wiki_reference.py` from the EF Core model snapshots and the services\' SQL. Edit those, not')
    w('> this page.')
    w('')
    w('LandmarkEmu keeps its data in **MariaDB / MySQL** (Docker runs MariaDB 11.4 in the `db` container; the database')
    w('port is published on the host PC only, `DB_PORT`, for tools like HeidiSQL). The programs create and update their')
    w('own tables at start:')
    w('')
    w('- the auth, character, world and social databases through **EF Core migrations** (the `Migrations` folder of each')
    w('  `LandmarkEmulator.Database.*` project; applied history in each database\'s `__EFMigrationsHistory` table);')
    w('- the commerce and world-channel databases with plain `CREATE TABLE IF NOT EXISTS` statements, columns added later')
    w('  being added at start when missing.')
    w('')
    w('Docker creates `lmemu_auth` and `lmemu_character` on the database container\'s first start and grants the')
    w('`landmarkemu` user the others (`docker/db-init/01-databases.sql`); each service creates its own database the first')
    w('time it starts. The connection strings are built by the compose files from `DB_PASSWORD` (see')
    w('[Settings Reference](Settings-Reference)); without Docker they are the `Database` keys of each program\'s JSON file.')
    w('')
    w('| Database | Schema | Used by | Tables |')
    w('|---|---|---|---|')
    contents = {}
    for name, info in DATABASES.items():
        kind, rel, model = info['source']
        if kind == 'snapshot':
            tables = parse_snapshot(rel) if name != 'Character' else character_tables
        elif kind == 'world':
            tables = OrderedDict((t, character_tables[t]) for t in WORLD_TABLES if t in character_tables)
        else:
            tables = parse_sql(rel)
        contents[name] = tables
        used = {'Auth': 'login server, worlds, gateways', 'Character': 'login server, worlds', 'World': 'its world',
                'Social': 'social service', 'Commerce': 'commerce service',
                'World-Channel': "the world's world-channel service"}[name]
        w(f'| [{name}](Database-{name}) | `{info["schema"]}` | {used} | {len(tables)} |')
    w('')
    w('Keys and ids: character ids, item guids and prop guids are 64-bit numbers the server allocates; item, prop,')
    w('recipe and other *definition* ids are the game client\'s own ids from its data tables. World positions are in the')
    w('client\'s local metres unless a column says otherwise (build-site boxes are in decimetres).')
    w('')
    w('Back up the database volume before upgrading the server or switching on an optional service with a one-time import')
    w('(see [Server Guide](Server-Guide)).')
    w('')
    pages['Database-Documentation.md'] = '\n'.join(overview)

    for name, info in DATABASES.items():
        kind, rel, model_folder = info['source']
        docs = model_docs(model_folder or ('LandmarkEmulator.Database.Character/Model' if kind == 'world' else None))
        tables = contents[name]
        out = []
        w = out.append
        w(f'# Database: {name} (`{info["schema"]}`)')
        w('')
        w('> Generated by `tools/wiki_reference.py`. Back to [Database Documentation](Database-Documentation).')
        w('')
        w(info['about'])
        w('')
        src = rel or DATABASES['Character']['source'][1]
        w(f'Source: `{src}`' + (f' and the model classes in `{model_folder}`' if model_folder else '') + '. Code: '
          f'[{info["project"].replace("-", ".").replace("Project.", "LandmarkEmulator.")}]({info["project"]}).')
        w('')
        if name == 'Character':
            w('Tables marked *world* are kept in the world\'s own database instead when the world has one (always under')
            w('Docker; see [Database: World](Database-World)).')
            w('')
        w('Tables: ' + ', '.join(f'[`{t}`](#{t})' for t in tables))
        w('')
        for table, entry in tables.items():
            w(f'## {table}')
            w('')
            note = TABLE_NOTES.get(table) or SQL_COLUMN_NOTES.get(table) or first_sentence(clean(docs.get(entry.get('class'), '')))
            if name == 'Character' and table in WORLD_TABLES:
                note = (note + ' ' if note else '') + '*World table.*'
            if note:
                w(note)
                w('')
            prop_to_col = {c['prop']: c['name'] for c in entry['columns'] if c.get('prop')}
            key = [prop_to_col.get(k, k) for k in entry['key']]
            w('| Column | Type | Null | Default | Key | Description |')
            w('|---|---|---|---|---|---|')
            index_cols = {}
            for cols, iname, unique in entry['indexes']:
                for c in cols:
                    index_cols.setdefault(prop_to_col.get(c, c), []).append('unique' if unique else 'index')
            fk_cols = {}
            for target, cols, delete in entry['fks']:
                for c in cols:
                    fk_cols[prop_to_col.get(c, c)] = f'FK → {TABLE_OF.get(target, target)}'
            for c in entry['columns']:
                k = []
                if c['name'] in key:
                    k.append('PK')
                if c['name'] in fk_cols:
                    k.append(fk_cols[c['name']])
                k += sorted(set(index_cols.get(c['name'], [])))
                if c.get('auto'):
                    k.append('auto')
                desc = ''
                if c.get('prop') and entry.get('class'):
                    desc = clean(docs.get((entry['class'], c['prop']), ''))
                    desc = shorten(desc)
                if c.get('added'):
                    desc = (desc + ' ' if desc else '') + '(added at start when missing)'
                default = c['default']
                default = '' if default in ('', 'null') else f'`{default}`'
                w(f'| `{c["name"]}` | {c["type"]} | {"yes" if c["null"] else ""} | {default} | {", ".join(k)} | {desc} |')
            w('')
            if entry['indexes']:
                w('Indexes: ' + '; '.join(f'`{n or "+".join(cols)}` ({", ".join(prop_to_col.get(c, c) for c in cols)})'
                                          + (' unique' if u else '') for cols, n, u in entry['indexes']) + '.')
                w('')
            for target, cols, delete in entry['fks']:
                w(f'Foreign key: ({", ".join(prop_to_col.get(c, c) for c in cols)}) → `{TABLE_OF.get(target, target)}`'
                  + (f', on delete {delete.lower()}' if delete else '') + '.')
                w('')
        pages[f'Database-{name}.md'] = '\n'.join(out)
    return pages


TABLE_OF = {}


def fill_table_of():
    for rel in ('LandmarkEmulator.Database.Auth/Migrations/AuthContextModelSnapshot.cs',
                'LandmarkEmulator.Database.Character/Migrations/CharacterContextModelSnapshot.cs',
                'LandmarkEmulator.Database.Social/Migrations/SocialContextModelSnapshot.cs'):
        for table, entry in parse_snapshot(rel).items():
            TABLE_OF[entry['class']] = table


def shorten(text, limit=260):
    if len(text) <= limit:
        return text
    cut = text[:limit]
    end = max(cut.rfind('. '), cut.rfind('; '))
    return (cut[:end + 1] if end > 80 else cut.rsplit(' ', 1)[0] + ' …')


# ----------------------------------------------------------------------------------------------------------------------

# Pages of the wiki that other files provide (the guide pages); links to them are not reported.
GUIDE_PAGES = {'Home', 'FAQ', 'Project-Status', 'Contributing', 'Client-Connection-Guide', 'LaunchPad', 'Server-Guide',
               'Server-Installation', 'Account-Creation', 'Hosting-Patches', '_Sidebar', '_Footer'}


def check_links():
    """[text](Page#anchor) links between wiki pages: the page must exist (or be a guide page), the anchor must exist."""
    problems = []
    texts = {f[:-3]: open(os.path.join(WIKI, f), encoding='utf-8').read() for f in os.listdir(WIKI) if f.endswith('.md')}

    def anchors(text):
        return {anchor(re.sub(r'`', '', m.group(1)).strip()) for m in re.finditer(r'^#+ (.+)$', text, re.M)}

    for page, text in texts.items():
        body = re.sub(r'```.*?```', '', text, flags=re.S)
        for m in re.finditer(r'\]\(([^)\s]+)\)', body):
            link = m.group(1)
            if re.match(r'[a-z]+:', link):
                continue
            target, _, frag = link.partition('#')
            target = target or page
            if target not in texts and target not in GUIDE_PAGES:
                problems.append(f'{page}: link to a missing page: {link}')
            elif frag and target in texts and frag not in anchors(texts[target]):
                problems.append(f'{page}: link to a missing anchor: {link}')
    return problems


def main():
    check_only = '--check' in sys.argv
    fill_table_of()
    pages = OrderedDict()
    pages['Settings-Reference.md'] = settings_page()
    pages.update(command_pages())
    pages.update(database_pages())

    problems = []
    for name, text in pages.items():
        problems += scan_public(name, text)
    if problems:
        print('Refused: the generated pages hold text that must not be published:')
        for p in problems:
            print('  ' + p)
        sys.exit(1)

    os.makedirs(WIKI, exist_ok=True)
    changed = 0
    for name, text in pages.items():
        path = os.path.join(WIKI, name)
        text = text.rstrip() + '\n'
        old = open(path, encoding='utf-8').read() if os.path.exists(path) else None
        if old != text:
            changed += 1
            if not check_only:
                with open(path, 'w', encoding='utf-8', newline='\n') as f:
                    f.write(text)
            print(('would write ' if check_only else 'wrote ') + name)
    print(f'{len(pages)} pages, {changed} changed.')

    # The hand-written pages get the same scan, and every page's wiki links are checked.
    for name in sorted(os.listdir(WIKI)):
        if name.endswith('.md') and name not in pages:
            for p in scan_public(name, open(os.path.join(WIKI, name), encoding='utf-8').read()):
                findings.append('Hand-written page: ' + p)
    findings.extend(check_links())
    if findings:
        print(f'\n{len(findings)} finding(s):')
        for f in findings:
            print('  - ' + f)


if __name__ == '__main__':
    main()
