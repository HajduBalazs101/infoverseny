# Mars Rover Útvonaltervező Algoritmus
## Vadász Dénes Informatika Verseny 2026 – Programozói Kategória

---

## 1. Áttekintés

Az útvonaltervező rendszer három fő komponensből áll:

1. **A\* pathfinder** – akadálykerülő legrövidebb útvonal keresés
2. **Greedy Nearest Neighbor + 2-opt** – ásványgyűjtési sorrend optimalizálás
3. **Adaptív sebességvezérlő** – energiahatékony mozgásstratégia

---

## 2. A\* Keresés (Legrövidebb Út)

### Pszeudokód

```
FUNCTION A_Star(térkép, start, cél):
    nyílt_halmaz ← prioritási_sor()
    zárt_halmaz ← üres_halmaz()
    g_költség[start] ← 0
    nyílt_halmaz.hozzáad(start, heurisztika(start, cél))

    WHILE nyílt_halmaz NEM üres:
        aktuális ← nyílt_halmaz.kivesz_legkisebb()

        IF aktuális == cél:
            RETURN útvonal_visszafejtés(aktuális)

        zárt_halmaz.hozzáad(aktuális)

        FOR EACH szomszéd IN 8_szomszéd(aktuális):  // átlós is
            IF szomszéd ∈ zárt_halmaz OR szomszéd == akadály:
                CONTINUE

            tentative_g ← g_költség[aktuális] + 1

            IF tentative_g < g_költség[szomszéd]:
                szülő[szomszéd] ← aktuális
                g_költség[szomszéd] ← tentative_g
                f ← tentative_g + heurisztika(szomszéd, cél)
                nyílt_halmaz.hozzáad(szomszéd, f)

    RETURN nincs_út
```

### Heurisztika
**Chebyshev-távolság** (8-irányú mozgáshoz admissible):
```
h(a, b) = max(|a.sor - b.sor|, |a.oszlop - b.oszlop|)
```

---

## 3. Ásványgyűjtési Sorrend Optimalizálás

### 3.1 Greedy Nearest Neighbor

```
FUNCTION Tervez_Gyűjtés(térkép, időkeret):
    ásványok ← térkép.összes_ásvány()
    sorrend ← üres_lista()
    aktuális ← start_pozíció
    maradék_tick ← időkeret × 2  // fél-órás tickek

    WHILE van_ásvány ÉS van_idő:
        legjobb ← NULL
        legjobb_táv ← ∞

        FOR EACH ásvány IN maradék_ásványok:
            táv_oda ← A_Star_távolság(aktuális, ásvány)
            táv_vissza ← A_Star_távolság(ásvány, start)

            // Elég idő van eljutni + bányászni + visszajutni?
            szükséges_tick ← táv_oda + 1 + táv_vissza
            IF szükséges_tick ≤ maradék_tick ÉS táv_oda < legjobb_táv:
                legjobb ← ásvány
                legjobb_táv ← táv_oda

        IF legjobb == NULL:
            BREAK

        sorrend.hozzáad(legjobb)
        maradék_ásványok.töröl(legjobb)
        maradék_tick -= (legjobb_táv + 1)
        aktuális ← legjobb

    RETURN 2_opt_javítás(sorrend)
```

### 3.2 2-opt Lokális Javítás

A Greedy sorrend nem garantáltan optimális, ezért 2-opt szomszédsági
kereséssel javítjuk:

```
FUNCTION 2_opt_javítás(sorrend):
    javult ← IGAZ

    WHILE javult:
        javult ← HAMIS
        FOR i ← 0 TO |sorrend| - 2:
            FOR j ← i + 2 TO |sorrend| - 1:
                előző_i ← (i == 0) ? start : sorrend[i-1]
                követő_j ← (j == utolsó) ? start : sorrend[j+1]

                régi_költség ← táv(előző_i, sorrend[i]) + táv(sorrend[j], követő_j)
                új_költség   ← táv(előző_i, sorrend[j]) + táv(sorrend[i], követő_j)

                IF új_költség < régi_költség:
                    megfordít(sorrend, i, j)
                    javult ← IGAZ

    RETURN sorrend
```

---

## 4. Adaptív Sebességvezérlő

A rover sebességét minden lépésben az aktuális helyzet alapján választjuk:

```
FUNCTION Sebesség_Választás(akkumulátor, napszak):
    IF akkumulátor ≤ 15%:
        RETURN Lassú  // 1 blokk/tick, E = 2

    IF napszak == Nappal:
        IF akkumulátor ≥ 60%: RETURN Gyors   // 3 blokk/tick, E = 18, töltés +10
        IF akkumulátor ≥ 30%: RETURN Normál  // 2 blokk/tick, E = 8, töltés +10
        RETURN Lassú

    ELSE:  // Éjszaka – nincs töltés!
        IF akkumulátor ≥ 70%: RETURN Normál
        RETURN Lassú
```

### Energiamérleg (fél-óránként):

| Sebesség | Fogyasztás (E=k·v²) | Nappal nettó | Éjszaka nettó |
|----------|---------------------|--------------|---------------|
| Lassú    | 2                   | +8           | -2            |
| Normál   | 8                   | +2           | -8            |
| Gyors    | 18                  | -8           | -18           |

---

## 5. Folyamatábra

```
                    ┌──────────────┐
                    │ TÉRKÉP       │
                    │ BETÖLTÉSE    │
                    └──────┬───────┘
                           │
                    ┌──────▼───────┐
                    │ ÁSVÁNYOK     │
                    │ FELTÉRKÉPEZÉS│
                    └──────┬───────┘
                           │
               ┌───────────▼──────────┐
               │ GREEDY NEAREST       │
               │ NEIGHBOR SORREND     │
               │ (energia+időkorlát)  │
               └───────────┬──────────┘
                           │
               ┌───────────▼──────────┐
               │ 2-OPT LOKÁLIS        │
               │ JAVÍTÁS              │
               └───────────┬──────────┘
                           │
          ┌────────────────▼────────────────┐
          │        SZIMULÁCIÓ CIKLUS        │
          │  ┌──────────────────────────┐   │
          │  │ Következő ásvány felé    │   │
          │  │ A* útvonal keresés       │   │
          │  └────────────┬─────────────┘   │
          │               │                 │
          │  ┌────────────▼─────────────┐   │
          │  │ Sebesség választás       │   │
          │  │ (akkumulátor + napszak)  │   │
          │  └────────────┬─────────────┘   │
          │               │                 │
          │  ┌────────────▼─────────────┐   │
          │  │ Mozgás végrehajtás       │   │
          │  │ Energia számítás         │   │
          │  │ Nappal/Éjszaka váltás    │   │
          │  └────────────┬─────────────┘   │
          │               │                 │
          │  ┌────────────▼─────────────┐   │
          │  │ Ásvány elérve?           │   │
          │  │ → Bányászás (1 tick)     │   │
          │  └────────────┬─────────────┘   │
          │               │                 │
          │  ┌────────────▼─────────────┐   │
          │  │ Idő/energia elég?        │──NO──→ VISSZATÉRÉS
          │  │ Van még célpont?         │        A BÁZISRA
          │  └────────────┬─────────────┘        (A* útvonal)
          │              YES                         │
          │               │                          │
          └───────────────┘                          │
                                            ┌────────▼───────┐
                                            │  SZIMULÁCIÓ    │
                                            │  BEFEJEZVE     │
                                            └────────────────┘
```

---

## 6. Komplexitás

| Komponens              | Időkomplexitás              |
|------------------------|-----------------------------|
| A* keresés             | O(n² · log n), n = cellaszám |
| Greedy NN              | O(m² · A*), m = ásványszám  |
| 2-opt javítás          | O(m² · iter)                |
| Szimuláció (1 tick)    | O(1)                        |

---

*Készítette: [Csapat neve] – 2026*
