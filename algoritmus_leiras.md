# Mars Rover Útvonaltervező Algoritmus v3
## Vadász Dénes Informatika Verseny 2026 – Programozói Kategória

---

## 1. Áttekintés

A rendszer három fő fázisból áll:

1. **BFS távolság-mátrix**: Minden ásvány + start + bázis párra kiszámolja a legrövidebb utat
2. **Klaszter-TSP**: DBSCAN klaszterezés → klaszter-sorrend → klaszteren belüli NN → 2-opt + or-opt
3. **Energia-szimuláció**: Precíz feasibility ellenőrzés a sebesség-stratégiával

---

## 2. A* Keresés (8-irányú, Chebyshev-heurisztika)

```
FUNCTION A_Star(térkép, start, cél):
    nyílt ← prioritási_sor()
    zárt ← halmaz()
    g[start] ← 0
    nyílt.add(start, heurisztika(start, cél))

    WHILE nyílt nem üres:
        akt ← nyílt.kivesz_min()
        IF akt == cél: RETURN visszafejt(akt)
        zárt.add(akt)
        FOR szomszéd IN 8_irány(akt):
            IF szomszéd ∈ zárt OR fal: SKIP
            tg ← g[akt] + 1
            IF tg < g[szomszéd]:
                szülő[szomszéd] ← akt
                g[szomszéd] ← tg
                nyílt.add(szomszéd, tg + h(szomszéd, cél))
    RETURN nincs_út

h(a,b) = max(|a.sor - b.sor|, |a.oszlop - b.oszlop|)   // Chebyshev
```

---

## 3. BFS Távolság-mátrix

Egyetlen BFS-ből megkapjuk egy pontból az összes többi ásvány távolságát.
Ez O(N) a térkép-méretben, szemben az A* O(N·log N)-jével.

```
FUNCTION BFS_Distances(térkép, start, célok):
    dist[start] ← 0
    sor ← [start]
    WHILE sor nem üres:
        akt ← sor.kivesz()
        FOR szomszéd IN 8_irány(akt):
            IF szomszéd nincs dist-ben:
                dist[szomszéd] ← dist[akt] + 1
                sor.add(szomszéd)
    RETURN dist
```

Ezt minden fontos pontból (ásványok + start + bázis) lefuttatjuk → teljes távolság-mátrix.

---

## 4. DBSCAN Klaszterezés (BFS-távolság alapú)

A korábbi Chebyshev-alapú klaszterezés helyett BFS-távolságot használunk,
ami figyelembe veszi a falakat!

```
FUNCTION Klaszterezés(ásványok, küszöb=8):
    Union-Find struktúra inicializálás
    FOR minden (i, j) ásványpár:
        IF BFS_távolság(i, j) ≤ küszöb:
            Union(i, j)
    RETURN csoportosítás a Union-Find szerint
```

---

## 5. Klaszter-TSP Sorrend

### 5.1 Klaszter-sorrend (Greedy Nearest Cluster)

```
FUNCTION Klaszter_Sorrend(klaszterek, start, bázis):
    sorrend ← []
    akt ← start
    WHILE van klaszter:
        legjobb ← min_távolságú klaszter (legközelebbi pont alapján)
        sorrend.add(legjobb)
        akt ← legjobb kilépő pontja
    RETURN sorrend
```

### 5.2 Klaszteren belüli sorrend (Nearest Neighbor)

Minden klaszteren belül egyszerű nearest-neighbor.

### 5.3 Globális 2-opt + Or-opt javítás

A teljes útvonalon (klaszter-határokon átívelő javítás is lehetséges):

**2-opt**: Élek cseréje ha rövidebb útvonalat kapunk.
**Or-opt**: 1-2-3 elemű szegmensek áthelyezése jobb pozícióba.

---

## 6. Energia-szimuláció és Feasibility

A tervezett útvonalat végigszimulálva ellenőrizzük, hogy minden
célpont elérhető-e az időn és energián belül, figyelembe véve a
nappal/éjszaka ciklust.

```
FUNCTION Feasibility(útvonal, start, bázis, idő, akku, ciklus_tick):
    FOR minden célpont:
        szükséges = oda_táv + 1 (bányász) + haza_táv + 3 (tartalék)
        IF szükséges > maradék_idő: SKIP
        IF szimulált_akku_hazaérésnél < 2%: SKIP
        elfogad(célpont)
    RETURN elfogadott_célpontok
```

---

## 7. Sebesség-stratégia

| Sebesség | Fogyasztás (K·v²) | Nappal nettó | Éjszaka nettó |
|----------|-------------------|-------------|---------------|
| Lassú    | 2                 | **+8**      | **-2**        |
| Normál   | 8                 | **+2**      | **-8**        |
| Gyors    | 18                | **-8**      | **-18**       |

**Stratégia**:
- Nappal 50%+ akku → Normál (jó sebesség, még töltődik)
- Nappal <50% → Lassú (töltődik +8/tick)
- Éjszaka → Lassú (-2/tick, fenntartható)
- Éjszaka 90%+ → Normál (ritka, de gyorsabb)
- Hazaút + sürgős → Gyors (csak ha muszáj)

---

## 8. Komplexitás

| Komponens              | Időkomplexitás              |
|------------------------|-----------------------------|
| BFS (1 pont)           | O(N), N = cellamszám        |
| BFS mátrix (m pont)    | O(m·N)                      |
| DBSCAN klaszterezés    | O(m²)                       |
| Klaszter-sorrend       | O(k²), k = klaszterszám     |
| NN klaszteren belül    | O(m²)                       |
| 2-opt                  | O(m²·iter)                  |
| Or-opt                 | O(m²·iter)                  |
| Szimuláció (1 tick)    | O(1)                        |

---

*Készítette: [Csapat neve] – 2026*
