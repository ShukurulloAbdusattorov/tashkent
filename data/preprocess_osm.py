"""Convert raw Overpass JSON into a compact, game-ready city.json in local metres.
Origin = Amir Temur monument. x = east, z = north (Unity left-handed, y up)."""
import json, math, sys, random
random.seed(7)
LAT0, LON0 = 41.3111759, 69.2797551
KX = math.cos(math.radians(LAT0)) * 111320.0
KZ = 110574.0
def xz(lat, lon): return [round((lon - LON0) * KX, 2), round((lat - LAT0) * KZ, 2)]
def geom(e): return [xz(p['lat'], p['lon']) for p in e.get('geometry', [])]
def parse_len(v, default=None):
    if v is None: return default
    try:
        v = str(v).lower().replace('m', '').replace(',', '.').strip()
        return float(v.split(';')[0])
    except Exception:
        return default
def ring_area(r):
    a = 0
    for i in range(len(r)):
        x1, z1 = r[i]; x2, z2 = r[(i + 1) % len(r)]
        a += x1 * z2 - x2 * z1
    return a / 2
def close(r):
    if len(r) > 2 and r[0] != r[-1]: r = r + [r[0]]
    return r
def norm_ring(r):
    r = close(r)
    if len(r) < 4: return None
    r = r[:-1]
    if ring_area(r) < 0: r.reverse()   # CCW in x/z (map view)
    return r

d = json.load(open('osm_square.json', encoding='utf-8'))
els = d['elements']

ROAD_W = {'primary': None, 'secondary': 10.5, 'tertiary': 8, 'residential': 6.5, 'service': 4, 'living_street': 5, 'unclassified': 6}
PATH_W = {'footway': 2.2, 'steps': 2.5, 'pedestrian': 5, 'path': 1.8, 'cycleway': 2}
BUILDING_DEFAULT_LEVELS = {'apartments': 5, 'commercial': 4, 'office': 5, 'university': 4, 'museum': 2, 'civic': 3, 'hotel': 6, 'retail': 2, 'roof': 1, 'yes': 4}

out = dict(origin=dict(lat=LAT0, lon=LON0), buildings=[], roads=[], paths=[], areas=[], trees=[], benches=[], bins=[], lamps=[], kerbs=[], fences=[], hedges=[], walls=[], pois=[], crossings=[], signals=[], stops=[])

def building_from(tags, outer, inners, eid):
    b = tags.get('building', 'yes')
    h = parse_len(tags.get('height'))
    lv = parse_len(tags.get('building:levels'))
    if h is None:
        if lv is None: lv = BUILDING_DEFAULT_LEVELS.get(b, 4)
        h = lv * 3.4 + (1.0 if b != 'roof' else 0)
    if lv is None: lv = max(1, round(h / 3.4))
    minh = parse_len(tags.get('min_height'), 0.0)
    return dict(id=eid, name=tags.get('name:en', tags.get('name', '')), type=b, height=round(h, 2), levels=int(lv), min_height=minh,
                roof=tags.get('roof:shape', 'flat'), colour=tags.get('building:colour', ''), material=tags.get('building:material', ''),
                outer=outer, inners=inners)

def area_kind(tags):
    if tags.get('amenity') == 'fountain': return 'fountain'
    if tags.get('natural') == 'water' or tags.get('water'): return 'water'
    if tags.get('landuse') == 'grass' or tags.get('natural') == 'grass' or tags.get('leisure') in ('park', 'garden') or tags.get('landuse') == 'flowerbed': return 'grass'
    if tags.get('natural') == 'wood' or tags.get('landuse') == 'forest': return 'wood'
    if tags.get('highway') == 'pedestrian' and tags.get('area') == 'yes': return 'plaza'
    if tags.get('amenity') == 'parking': return 'parking'
    if tags.get('leisure') in ('pitch', 'playground'): return 'pitch'
    return None

# ---- ways ----
for e in els:
    if e['type'] != 'way': continue
    t = e.get('tags', {}); g = geom(e)
    if len(g) < 2: continue
    closed = g[0] == g[-1] and len(g) >= 4
    if 'building' in t and t['building'] != 'no' and closed:
        r = norm_ring(g)
        if r: out['buildings'].append(building_from(t, r, [], e['id']))
        continue
    if 'building:part' in t and closed:
        r = norm_ring(g)
        if r:
            bb = building_from(t, r, [], e['id']); bb['part'] = True; out['buildings'].append(bb)
        continue
    hw = t.get('highway')
    if hw and not (hw == 'pedestrian' and t.get('area') == 'yes'):
        if hw in ROAD_W:
            lanes = parse_len(t.get('lanes'))
            w = parse_len(t.get('width'))
            if w is None:
                if lanes: w = lanes * 3.5 + 0.5
                else: w = ROAD_W[hw] or 11.0
            out['roads'].append(dict(id=e['id'], cls=hw, name=t.get('name:en', t.get('name', '')), width=round(w, 2), lanes=int(lanes) if lanes else None,
                                     oneway=t.get('oneway') == 'yes', surface=t.get('surface', 'asphalt'), pts=g, bridge=t.get('bridge') == 'yes', tunnel=t.get('tunnel') == 'yes', layer=int(parse_len(t.get('layer'), 0))))
        elif hw in PATH_W:
            w = parse_len(t.get('width'), PATH_W[hw])
            out['paths'].append(dict(id=e['id'], cls=hw, width=round(w, 2), surface=t.get('surface', 'paving_stones'), pts=g, layer=int(parse_len(t.get('layer'), 0)), tunnel=t.get('tunnel') == 'yes'))
        continue
    if t.get('natural') == 'tree_row':
        step = 7.0
        for i in range(len(g) - 1):
            x1, z1 = g[i]; x2, z2 = g[i + 1]; L = math.hypot(x2 - x1, z2 - z1)
            if L == 0: continue
            s = 0
            while s < L:
                f = s / L; out['trees'].append([round(x1 + (x2 - x1) * f, 2), round(z1 + (z2 - z1) * f, 2), 'row', ''])
                s += step
        continue
    br = t.get('barrier')
    if br:
        key = {'kerb': 'kerbs', 'fence': 'fences', 'hedge': 'hedges', 'wall': 'walls', 'retaining_wall': 'walls'}.get(br)
        if key: out[key].append(dict(id=e['id'], pts=g, height=parse_len(t.get('height'), 1.2 if key != 'kerbs' else 0.15)))
        continue
    k = area_kind(t)
    if k and closed:
        r = norm_ring(g)
        if r: out['areas'].append(dict(id=e['id'], kind=k, name=t.get('name:en', t.get('name', '')), outer=r, inners=[], layer=int(parse_len(t.get('layer'), 0))))
        continue

# ---- relations (multipolygons) ----
def assemble_rings(members, role):
    segs = [[list(p) for p in geom(m)] for m in members if m.get('role') == role and m.get('geometry')]
    rings = []
    while segs:
        cur = segs.pop(0)
        changed = True
        while cur[0] != cur[-1] and changed:
            changed = False
            for i, s in enumerate(segs):
                if s[0] == cur[-1]: cur += s[1:]; segs.pop(i); changed = True; break
                if s[-1] == cur[-1]: cur += list(reversed(s))[1:]; segs.pop(i); changed = True; break
                if s[-1] == cur[0]: cur = s[:-1] + cur; segs.pop(i); changed = True; break
                if s[0] == cur[0]: cur = list(reversed(s))[:-1] + cur; segs.pop(i); changed = True; break
        r = norm_ring(cur)
        if r: rings.append(r)
    return rings
for e in els:
    if e['type'] != 'relation': continue
    t = e.get('tags', {})
    if t.get('type') != 'multipolygon': continue
    outers = assemble_rings(e['members'], 'outer'); inners = assemble_rings(e['members'], 'inner')
    if not outers: continue
    inners = [list(reversed(r)) for r in inners]  # CW holes
    if 'building' in t:
        for o in outers: out['buildings'].append(building_from(t, o, inners, e['id']))
    else:
        k = area_kind(t)
        if k:
            for o in outers: out['areas'].append(dict(id=e['id'], kind=k, name=t.get('name:en', t.get('name', '')), outer=o, inners=inners, layer=0))

# ---- nodes ----
path_segs = [(p['pts'], p['cls']) for p in out['paths']]
def nearest_dir(x, z):
    best = (1e9, 0)
    for pts, _ in path_segs:
        for i in range(len(pts) - 1):
            x1, z1 = pts[i]; x2, z2 = pts[i + 1]; dx, dz = x2 - x1, z2 - z1; L2 = dx * dx + dz * dz
            if L2 == 0: continue
            u = max(0, min(1, ((x - x1) * dx + (z - z1) * dz) / L2)); px, pz = x1 + u * dx, z1 + u * dz
            dd = math.hypot(px - x, pz - z)
            if dd < best[0]: best = (dd, math.degrees(math.atan2(dx, dz)))  # yaw from +z
    return best
for e in els:
    if e['type'] != 'node' or 'tags' not in e: continue
    t = e['tags']; p = xz(e['lat'], e['lon'])
    if t.get('natural') == 'tree':
        out['trees'].append([p[0], p[1], t.get('leaf_type', ''), t.get('genus', t.get('species', ''))])
    elif t.get('amenity') == 'bench':
        dist, yaw = nearest_dir(*p); out['benches'].append(dict(x=p[0], z=p[1], yaw=round(yaw, 1), backrest=t.get('backrest', 'yes') != 'no'))
    elif t.get('amenity') == 'waste_basket': out['bins'].append(dict(x=p[0], z=p[1]))
    elif t.get('highway') == 'street_lamp': out['lamps'].append(dict(x=p[0], z=p[1]))
    elif t.get('highway') == 'crossing': out['crossings'].append(p)
    elif t.get('highway') == 'traffic_signals': out['signals'].append(p)
    elif t.get('highway') == 'bus_stop' or t.get('public_transport') == 'platform': out['stops'].append(dict(x=p[0], z=p[1], name=t.get('name:en', t.get('name', ''))))
    elif t.get('railway') == 'subway_entrance': out['pois'].append(dict(kind='subway_entrance', x=p[0], z=p[1], name=t.get('name:en', t.get('name', ''))))
    elif t.get('historic') or t.get('tourism') or t.get('man_made') in ('tower', 'flagpole') or t.get('amenity') in ('clock', 'fountain'):
        out['pois'].append(dict(kind=t.get('historic') or t.get('tourism') or t.get('man_made') or t.get('amenity'), x=p[0], z=p[1], name=t.get('name:en', t.get('name', '')), tags={k: v for k, v in t.items() if len(v) < 60}))

# ---- named landmarks lookup ----
def centroid(r): return [round(sum(p[0] for p in r) / len(r), 1), round(sum(p[1] for p in r) / len(r), 1)]
lm = {}
NEEDLES = [('hotel_uzbekistan', 'uzbekistan hotel'), ('forum_palace', 'palace of international forums'), ('timurid_museum', 'museum of timurid'), ('chimes', 'chiming clock'), ('law_university', 'university of law'), ('navoi_theater', 'navoi theater'), ('central_bank', 'central bank'), ('hampton', 'hampton')]
for b in out['buildings']:
    n = b['name'].lower()
    for key, needle in NEEDLES:
        if needle in n and key not in lm: lm[key] = dict(id=b['id'], center=centroid(b['outer']), height=b['height'])
lm['monument'] = dict(center=[0.0, 0.0])
out['landmarks'] = lm

json.dump(out, open('city.json', 'w', encoding='utf-8'), ensure_ascii=False, separators=(',', ':'))
print('buildings', len(out['buildings']), 'roads', len(out['roads']), 'paths', len(out['paths']), 'areas', len(out['areas']), 'trees', len(out['trees']), 'benches', len(out['benches']), 'bins', len(out['bins']), 'kerbs', len(out['kerbs']), 'fences', len(out['fences']), 'hedges', len(out['hedges']), 'pois', len(out['pois']))
print('landmarks', json.dumps(lm))
from collections import Counter
print('area kinds', Counter(a['kind'] for a in out['areas']))
print('road classes', Counter(r['cls'] for r in out['roads']))
xs = [p[0] for b in out['buildings'] for p in b['outer']]; zs = [p[1] for b in out['buildings'] for p in b['outer']]
print('extent x', min(xs), max(xs), 'z', min(zs), max(zs))
