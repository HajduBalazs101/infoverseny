# Mars Rover Útvonaltervező Algoritmus

**Vadász Dénes Informatika Verseny 2026 – Programozói Kategória**
**Csapat: While(True)**

---

## 1. Áttekintés

A Mars rover feladata egy 50×50-es térképen ásványok begyűjtése, miközben
kezeli az akkumulátor töltöttségét, a nappal/éjszaka ciklust, és időben
visszatér a bázisra.

A megoldásunk egy többlépcsős optimalizálási eljárás:

```
┌──────────────────────────────────────────────┐
│  1. BFS távolság-mátrix                      │
│     (pontos távolság minden ásványpár közt)   │
│                    ▼                          │
│  2. DBSCAN klaszterezés                      │
│     (közeli ásványok csoportosítása)          │
│                    ▼                          │
│  3. Klaszter-sorrend (nearest-cluster)       │
│     + klaszteren belüli sorrend (NN)         │
│                    ▼                          │
│  4. Globális javítás (2-opt + or-opt)        │
│                    ▼                          │
│  5. Feasibility ellenőrzés                   │
│     (idő + energia szimuláció)               │
│                    ▼                          │
│  6. Szimuláció végrehajtás                   │
│     (adaptív sebesség + útba eső begyűjtés)  │
└──────────────────────────────────────────────┘
```

---

## 2. A* Útvonalkeresés

Két pont közötti legrövidebb út megtalálására az A* algoritmust
használjuk. A mozgás 8-irányú (átlós is megengedett).

### Heurisztika

Chebyshev-távolság, ami admissible és consistent 8-irányú mozgásnál:

```
h(a, b) = max(|a.sor − b.sor|, |a.oszlop − b.oszlop|)
```

### Pszeudokód

```
FUNCTION A_Star(térkép, start, cél):

    nyílt_halmaz ← prioritási_sor()
    zárt_halmaz  ← üres_halmaz()
    g_költség[start] ← 0

    nyílt_halmaz.hozzáad(start, prioritás = h(start, cél))

    WHILE nyílt_halmaz nem üres:

        aktuális ← nyílt_halmaz.kivesz_legkisebb()

        IF aktuális == cél:
            RETURN útvonal_visszafejtés(aktuális)

        zárt_halmaz.hozzáad(aktuális)

        FOR EACH szomszéd IN 8_szomszéd(aktuális):
            IF szomszéd ∈ zárt_halmaz:  SKIP
            IF szomszéd == fal:          SKIP

            tentative_g ← g_költség[aktuális] + 1

            IF tentative_g < g_költség[szomszéd]:
                szülő[szomszéd] ← aktuális
                g_költség[szomszéd] ← tentative_g
                f ← tentative_g + h(szomszéd, cél)
                nyílt_halmaz.hozzáad(szomszéd, prioritás = f)

    RETURN nincs_elérhető_út
```

Az A* garantáltan a legrövidebb utat adja, mert a Chebyshev-heurisztika
soha nem becsüli túl a valós távolságot.

---

## 3. BFS Távolság-mátrix

### A probléma

Az útvonaltervezéshez szükségünk van minden ásványpár közötti
legrövidebb távolságra. Ha ezt A*-gal csinálnánk, az ~390² = 152 100
A* futtatás lenne – túl lassú.

### A megoldás

Egy BFS (szélességi keresés) egyetlen futtatással megadja egy pontból
az összes többi pont távolságát. Ezt minden fontos pontból
(~392 db: 390 ásvány + start + bázis) lefuttatjuk.

```
FUNCTION BFS_Távolságok(térkép, start):

    távolság[start] ← 0
    sor ← [start]

    WHILE sor nem üres:
        aktuális ← sor.kivesz_első()

        FOR EACH szomszéd IN 8_szomszéd(aktuális):
            IF szomszéd NINCS a távolság-ban ÉS szomszéd ≠ fal:
                távolság[szomszéd] ← távolság[aktuális] + 1
                sor.hozzáad(szomszéd)

    RETURN távolság
```

### Miért jobb ez?

| Módszer        | Futtatások száma | Egy futtatás     | Összesen         |
|---------------|-----------------|-----------------|-----------------|
| A* páronként  | 152 100         | O(N × log N)    | nagyon lassú     |
| BFS pontonként| 392             | O(N)            | **gyors**        |

Ahol N = 2500 (a 50×50-es térkép celláinak száma).

Az eredmény egy teljes távolság-mátrix: bármely két fontos pont
közötti pontos legrövidebb távolság azonnal elérhető.

---

## 4. Klaszterezés

### Miért kell klaszterezni?

Klaszterezés nélkül a rover a legközelebbi ásványra megy, ami
globálisan rossz sorrendet eredményez – ide-oda ugrál a térkép
különböző sarkai között ahelyett, hogy egy területet végigdolgozna.

### DBSCAN + Union-Find

A klaszterezés BFS-távolságon alapul (nem légvonalon!), tehát
figyelembe veszi a falakat. Ha két ásvány között fal van,
nem kerülnek egy klaszterbe, még ha egyébként közel lennének.

```
FUNCTION Klaszterezés(ásványok, küszöb = 8):

    // Minden ásvány saját csoportba kerül
    Union-Find inicializálás (n elem, n csoport)

    FOR EACH (i, j) ásványpár:
        IF BFS_távolság(i, j) ≤ küszöb:
            Union(i, j)     // összevonjuk a két csoportot

    RETURN csoportok a Union-Find gyökerei szerint
```

A **küszöbérték 8 BFS-lépés**: ha két ásvány 8 lépésen belül
elérhető egymástól (falakat kerülve), egy klaszterbe kerülnek.

A Union-Find adatszerkezet path compression és union-by-rank
optimalizációval közel O(1) műveletenként.

---

## 5. Útvonaltervezés (Klaszter-TSP)

Az útvonaltervezés három lépésből áll. Ez egy heurisztikus
megoldás a Travelling Salesman Problem (TSP) feladatra,
klaszter-struktúrát kihasználva.

### 5.1. Klaszter-sorrend (Nearest Cluster)

Greedy módszerrel választjuk a következő klasztert: mindig a
legközelebbi és legsűrűbb klasztert célozzuk meg.

```
FUNCTION Klaszter_Sorrend(klaszterek, start_pozíció):

    sorrend ← üres_lista()
    aktuális ← start_pozíció

    WHILE van feldolgozatlan klaszter:

        FOR EACH klaszter IN maradék_klaszterek:
            belépő_távolság ← min(BFS_táv(aktuális, m)) m ∈ klaszter
            pontszám ← belépő_távolság − méret(klaszter) × 0.8

        legjobb ← legkisebb pontszámú klaszter
        sorrend.hozzáad(legjobb)

        // Kilépő pont: a klaszterből a legközelebbi pont
        // a megmaradt klaszterekhez/bázishoz
        aktuális ← legjobb_kilépő_pont

    RETURN sorrend
```

A pontszámban a **klaszter mérete bónuszt ad**: nagyobb klaszterek
(több ásvány egy helyen) előnyt élveznek, mert hatékonyabb
egyszerre sok ásványt begyűjteni.

### 5.2. Klaszteren belüli sorrend (Nearest Neighbor)

Egy klaszteren belül a legegyszerűbb hatékony heurisztikát
használjuk: mindig a legközelebbi nem-látogatott ásványhoz megyünk.

```
FUNCTION Belső_Sorrend(klaszter, belépő_pozíció):

    sorrend ← üres_lista()
    maradék ← klaszter összes ásványa
    aktuális ← belépő_pozíció

    WHILE maradék nem üres:
        legközelebbi ← argmin(BFS_táv(aktuális, m)) m ∈ maradék
        sorrend.hozzáad(legközelebbi)
        maradék.töröl(legközelebbi)
        aktuális ← legközelebbi

    RETURN sorrend
```

### 5.3. Globális javítás (2-opt + Or-opt)

A klaszter-sorrend és a belső NN sorrend összeillesztése után
a teljes útvonalon futtatunk lokális keresést, ami a klaszterek
közötti átmeneteket is képes javítani.

**2-opt**: Két élet kiválasztunk és megfordítjuk a köztük lévő
szegmenst. Ha ez rövidebb össztávolságot ad, megtartjuk.

```
FUNCTION 2_opt(útvonal, start):

    REPEAT WHILE van javulás (max 200 iteráció):

        FOR EACH (i, j) pozíció-pár, i < j:

            előző_i  ← (i == 0) ? start : útvonal[i − 1]
            követő_j ← (j == utolsó) ? start : útvonal[j + 1]

            régi_költség ← táv(előző_i, útvonal[i])
                         + táv(útvonal[j], követő_j)

            új_költség   ← táv(előző_i, útvonal[j])
                         + táv(útvonal[i], követő_j)

            IF új_költség < régi_költség:
                megfordít(útvonal, i-től j-ig)

    RETURN útvonal
```

**Or-opt**: 1, 2 vagy 3 elemű szegmenst kiveszünk a helyéről
és beszúrjuk egy jobb pozícióba. Ez aszimmetrikus javítás,
amit a 2-opt nem tud megtalálni.

```
FUNCTION Or_opt(útvonal, start):

    REPEAT WHILE van javulás (max 100 iteráció):

        FOR szegmens_hossz IN [1, 2, 3]:
            FOR EACH i pozíció:

                // Mennyit nyerünk a szegmens kivételével?
                nyereség ← (él a szegmens előtt + él utána)
                          − (közvetlen él szegmens nélkül)

                FOR EACH j beszúrási pozíció:
                    // Mennyibe kerül a szegmens beszúrása?
                    költség ← (új élek j-nél) − (régi él j-nél)

                    IF nyereség > költség:
                        szegmens áthelyezése i-ből j-be

    RETURN útvonal
```

A sorrend: **2-opt → or-opt → 2-opt** (két kör 2-opt,
köztük or-opt, mert az or-opt új lehetőségeket nyithat
a 2-opt számára).

---

## 6. Feasibility Ellenőrzés

A tervezett útvonalat végigszimulálva ellenőrizzük, hogy
minden célpont elérhető-e az idő- és energiakorlátok mellett.

### A probléma

Nem elég, hogy egy ásvány elméletileg elérhető – a rovernek
utána még **haza is kell jutnia** a bázisra. Ráadásul az
energia a napszaktól és a sebességtől függ.

### A megoldás

```
FUNCTION Feasibility(útvonal, pozíció, bázis, maradék_idő, akku):

    elfogadott ← üres_lista()

    FOR EACH célpont IN útvonal:

        // 1. Időbecslés (nappal 2 lépés/tick, éjjel 1 lépés/tick)
        tick_oda  ← lépésszám / sebesség (napszak-függő)
        tick_haza ← lépésszám / sebesség (napszak-függő)
        tick_össz ← tick_oda + 1 (bányászat) + tick_haza + 2 (tartalék)

        IF tick_össz > maradék_idő:
            SKIP    // nem fér bele időben

        // 2. Energia-szimuláció (tick-enként)
        akku_oda  ← szimulál(tick_oda + 1, akku, napszak)
        akku_haza ← szimulál(tick_haza, akku_oda, napszak)

        IF akku_haza < 1%:
            SKIP    // nem lenne elég energia hazajutni

        // 3. Elfogadjuk
        elfogadott.hozzáad(célpont)
        frissít(pozíció, akku, idő)

    RETURN elfogadott
```

Az energia-szimuláció a rover tényleges sebesség-stratégiáját
tükrözi: nappal Normál (nettó +2/tick), éjjel Lassú (nettó −2/tick).

---

## 7. Sebesség-stratégia

A rover három sebességfokozattal rendelkezik. Az energiafogyasztás
a sebesség négyzetével arányos: **E = K × v²**, ahol K = 2.

### Energiamérleg

| Sebesség | Lépés/tick | Fogyasztás | Nappali töltés | Nappali nettó | Éjszakai nettó |
|----------|-----------|------------|---------------|--------------|----------------|
| Lassú    | 1         | 2/tick     | +10/tick      | **+8/tick**  | **−2/tick**    |
| Normál   | 2         | 8/tick     | +10/tick      | **+2/tick**  | **−8/tick**    |
| Gyors    | 3         | 18/tick    | +10/tick      | **−8/tick**  | **−18/tick**   |

### Választási logika

```
FUNCTION Sebesség_Választás(akku, napszak, hazafelé):

    // Hazaút: ha szükséges, gyorsítunk
    IF hazafelé ÉS nem érünk haza időben:
        RETURN Gyors

    IF napszak == Nappal:
        IF akku ≥ 80%:   RETURN Gyors    // 3 lépés, nettó −8, de gyors haladás
        IF akku ≥ 20%:   RETURN Normál   // 2 lépés, nettó +2, sosem fogy el
        RETURN Lassú                      // 1 lépés, feltöltődés +8/tick

    IF napszak == Éjszaka:
        IF akku ≥ 50%:   RETURN Normál   // 2 lépés, nettó −8, de bírja
        RETURN Lassú                      // 1 lépés, biztonságos −2/tick
```

### Miért ez a stratégia?

- **Nappal Normál az alap**: nettó +2/tick, tehát az akku sosem fogy
  el napközben, és kétszer olyan gyorsan halad mint Lassúval.
- **Nappal 80%+ → Gyors**: nettó −8, de háromszor olyan gyors.
  A nappali ciklus 32 tick, szóval max 256-ot fogyaszt – 80%-ról
  induulva még bőven marad.
- **Éjszaka 50%+ → Normál**: nettó −8/tick, éjszaka 16 tick,
  össz. −128. Ha 50%-ról indul, −128 + 50 = negatív, DE nappal
  feltöltődik. A cél: ne pazaroljunk időt Lassúval ha nem kell.

---

## 8. Adaptív Viselkedés

A rover futás közben is alkalmazkodik a helyzethez:

### Útba eső ásványok begyűjtése

Ha a rover egy célpont felé haladva egy még nem begyűjtött
ásványon halad át, automatikusan begyűjti. Ez "ingyen" bónusz,
mert a rover amúgy is arra megy.

### Újratervezés

Ha a rover elfogyasztotta az összes tervezett célpontot, de
van még ideje és energiája, újrafuttatja a teljes tervezési
algoritmust a jelenlegi pozícióból. Így a maradék időt is
hatékonyan használja ki.

### Éjszakai várakozás

Ha az akku kritikusan alacsony (< 12%) és hamarosan napfelkelte
(≤ 6 tick), a rover standby-ba áll. A standby csak 1/tick-et
fogyaszt, és napkeltekor azonnal tölteni kezd (+9/tick nettó).

### Nappali töltés

Ha az akku 8% alá esik nappal, a rover megáll és tölt.
A nettó töltés +9/tick (10 töltés − 1 standby), tehát néhány
tick alatt újra mozgásképes.

### Cél-kihagyás

Ha egy célpont nem fér bele időben vagy energiában, a rover
nem adja fel azonnal – kipróbálja a következő 5 célpontot is,
hátha egy közelebbi még belefér.

---

## 9. Komplexitás

| Komponens                 | Időkomplexitás            | Megjegyzés                    |
|--------------------------|--------------------------|-------------------------------|
| BFS (1 pontból)          | O(N)                     | N = 2500 (50×50 térkép)      |
| BFS mátrix (m pontból)   | O(m × N)                 | m ≈ 392 pont × 2500 cella    |
| Klaszterezés             | O(m² × α(m))             | α = inverz Ackermann ≈ O(1)  |
| Klaszter-sorrend         | O(k²)                    | k = klaszterek száma          |
| NN klaszteren belül      | O(m²)                    | legrosszabb eset              |
| 2-opt javítás            | O(m² × iteráció)         | max 200 iteráció              |
| Or-opt javítás           | O(m² × iteráció)         | max 100 iteráció              |
| Feasibility              | O(m)                     | lineáris az útvonal mentén    |
| Szimuláció (1 tick)      | O(1)                     | állandó idejű                 |

---

## 10. Teljes Folyamatábra

```
  ┌─────────────────────────────┐
  │  TÉRKÉP BETÖLTÉS            │
  │  (50×50 CSV fájl beolvasás) │
  └──────────────┬──────────────┘
                 ▼
  ┌─────────────────────────────┐
  │  BFS TÁVOLSÁG-MÁTRIX        │
  │  ~392 BFS futtatás          │
  │  Eredmény: pontos távolság  │
  │  minden fontos pont között  │
  └──────────────┬──────────────┘
                 ▼
  ┌─────────────────────────────┐
  │  DBSCAN KLASZTEREZÉS        │
  │  Union-Find, küszöb = 8     │
  │  Eredmény: ásványcsoportok  │
  └──────────────┬──────────────┘
                 ▼
  ┌─────────────────────────────┐
  │  KLASZTER-SORREND           │
  │  Nearest-cluster heurisztika│
  │  Eredmény: bejárási sorrend│
  └──────────────┬──────────────┘
                 ▼
  ┌─────────────────────────────┐
  │  KLASZTEREN BELÜLI SORREND  │
  │  Nearest-neighbor           │
  │  Eredmény: teljes útvonal  │
  └──────────────┬──────────────┘
                 ▼
  ┌─────────────────────────────┐
  │  GLOBÁLIS JAVÍTÁS           │
  │  2-opt → or-opt → 2-opt    │
  │  Eredmény: javított útvonal│
  └──────────────┬──────────────┘
                 ▼
  ┌─────────────────────────────┐
  │  FEASIBILITY VÁGÁS          │
  │  Idő + energia szimuláció  │
  │  Eredmény: végrehajtható   │
  │  célpont-lista              │
  └──────────────┬──────────────┘
                 ▼
  ┌─────────────────────────────────────┐
  │  SZIMULÁCIÓ VÉGREHAJTÁS             │
  │                                     │
  │  REPEAT tick-enként:                │
  │                                     │
  │    ● Van célpont?                   │
  │      → A* útvonal a következőhöz   │
  │    ● Sebesség kiválasztása          │
  │      (akku + napszak alapján)       │
  │    ● Mozgás végrehajtása            │
  │    ● Útba eső ásvány?              │
  │      → automatikus begyűjtés       │
  │    ● Célpont elérve?               │
  │      → 1 tick bányászat            │
  │    ● Elfogytak a célok?            │
  │      → ÚJRATERVEZÉS                │
  │    ● Idő fogy?                     │
  │      → visszatérés a bázisra       │
  │                                     │
  │  UNTIL idő lejárt VAGY bázison van │
  └─────────────────────────────────────┘
                 ▼
  ┌─────────────────────────────┐
  │  EREDMÉNY                   │
  │  Begyűjtött ásványok száma  │
  │  + CSV napló exportálás     │
  └─────────────────────────────┘
```

---

*Készítette: While(True) csapat – 2026*
*Miskolci SZC Kandó Kálmán Informatikai Technikum*
