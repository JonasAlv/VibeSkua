import re, sys, json
src = open(sys.argv[1], encoding='utf-8').read().splitlines()
FORGE_W = {"Forge","Lacerate","Smite","Valiance","Arcanas_Concerto","Acheron","Elysium","Praxis","Dauntless","Ravenous"}
AWE_W = {"Spiral_Carve","Awe_Blast","Health_Vamp","Mana_Vamp","Powerword_Die"}

def library(start_sig):
    i = next(n for n, l in enumerate(src) if start_sig in l)
    depth = 0; out = {}; labels = []; body = {}; started = False
    stmt = ""
    for l in src[i:]:
        depth += l.count('{') - l.count('}')
        if '{' in l: started = True
        s = l.strip()
        m = re.match(r'case "([^"]+)":', s)
        if m:
            if body: labels, body = [], {}
            labels.append(m.group(1)); continue
        if s.startswith('default:'): labels, body = [], {}
        stmt += " " + s
        if s.endswith(';') or s.endswith('{') or s.endswith('}'):
            for var, enum in (("type","EnhancementType"),("c","CapeSpecial"),("w","WeaponSpecial"),("h","HelmSpecial")):
                name = {"type":"type","c":"cSpecial","w":"wSpecial","h":"hSpecial"}[var]
                if re.search(rf'\b{name}\s*=(?!=)', stmt) and var not in body:
                    mm = re.search(rf'{enum}\.(\w+)', stmt)
                    if mm: body[var] = mm.group(1)
            stmt = ""
        if s.startswith('break;') and labels:
            for lab in labels:
                out.setdefault(lab, dict(body))
            labels, body = [], {}
        if started and depth <= 0: break
    return out

forge = library("bool ForgeEnhancementLibrary()")
awe = library("void AweEnhancementLibrary()")
presets = {}
for name in sorted(set(forge) | set(awe)):
    if name.lower() == "placeholder": continue
    f, a = forge.get(name, {}), awe.get(name, {})
    w = [x for x in (f.get("w"), a.get("w")) if x]
    presets[name] = {
        "Base": f.get("type") or a.get("type") or "Lucky",
        "WeaponForge": next((x for x in w if x in FORGE_W), "None"),
        "WeaponAwe": next((x for x in w if x in AWE_W), "None"),
        "Cape": f.get("c", "None"),
        "Helm": f.get("h", "None"),
    }

# Classes only in the Awe library get the most common Forge setup (weapon, cape, helm) among
# Forge-library classes with the same Base and Awe weapon, falling back to the same Base only.
from collections import Counter
def setups(match):
    return Counter((v["WeaponForge"], v["Cape"], v["Helm"]) for n, v in presets.items()
                   if n in forge and v["WeaponForge"] != "None" and match(v))
inferred = 0
for name, v in presets.items():
    if name in forge:
        continue
    common = setups(lambda o: (o["Base"], o["WeaponAwe"]) == (v["Base"], v["WeaponAwe"])) or setups(lambda o: o["Base"] == v["Base"])
    if common:
        v["WeaponForge"], v["Cape"], v["Helm"] = common.most_common(1)[0][0]
        inferred += 1

# Any slot still None (old Forge-library entries) gets the most common value of that slot in the same groups.
for name, v in presets.items():
    for slot in ("WeaponForge", "WeaponAwe", "Cape", "Helm"):
        if v[slot] != "None":
            continue
        for match in (lambda o: (o["Base"], o["WeaponAwe"]) == (v["Base"], v["WeaponAwe"]), lambda o: o["Base"] == v["Base"]):
            values = Counter(o[slot] for o in presets.values() if o[slot] != "None" and match(o))
            if values:
                v[slot] = values.most_common(1)[0][0]
                inferred += 1
                break
print("inferred", inferred)
json.dump(presets, open(sys.argv[2], 'w', encoding='utf-8'), indent=1)
print(len(forge), len(awe), len(presets))
