═══════════════════════════════════════════════════
  MARS ROVER – Vadász Dénes Informatika Verseny 2026
  Programozói Kategória
═══════════════════════════════════════════════════

Csapat neve: [CSAPAT NÉV]
Csapattagok:
  - [Név 1], [Iskola], [Osztály]
  - [Név 2], [Iskola], [Osztály]
Felkészítő tanár: [Tanár neve]
Email: [email cím]

───────────────────────────────────────────────────
FEJLESZTŐI KÖRNYEZET
───────────────────────────────────────────────────
Programnyelv:     C# (.NET 8.0)
Keretrendszer:    WPF (Windows Presentation Foundation)
IDE:              Visual Studio 2022 (v17.x)
NuGet csomagok:   LiveChartsCore.SkiaSharpView.WPF 2.0.0-rc3.3
Target:           Windows x64

───────────────────────────────────────────────────
HASZNÁLATI ÚTMUTATÓ
───────────────────────────────────────────────────

1. INDÍTÁS
   - Nyisd meg a MarsRover.sln fájlt Visual Studio 2022-ben
   - Vagy futtasd a MarsRover.exe fájlt (a mars_map_50x50.csv-nek
     mellette kell lennie)
   - Release build: jobb klikk a projektre → Publish → Folder

2. FUTTATÁS
   - A program betölti a mars_map_50x50.csv térképet
   - Adj meg időkeretet órában (min. 24 óra, ajánlott: 48-72)
   - Nyomd meg az INDÍTÁS gombot
   - A program megtervezi az útvonalat (A* + Greedy TSP + 2-opt),
     majd elindítja a szimulációt

3. VEZÉRLÉS
   - ▶/⏸ gomb: szimuláció indítás/szüneteltetés
   - Animáció slider: szimuláció sebesség szabályozása (50-1000ms/tick)
   - 📋 gomb: eseménynapló exportálása CSV fájlba

4. DASHBOARD
   A jobb oldali panelen valós időben frissülő adatok:
   - Akkumulátor szint és grafikon
   - Gyűjtött ásványok (kék/sárga/zöld) és összesített grafikon
   - Rover pozíció, sebesség, tevékenység
   - Nappal/éjszaka ciklus
   - Sebesség grafikon
   - Eseménynapló

───────────────────────────────────────────────────
ÚTVONALTERVEZŐ ALGORITMUS (röviden)
───────────────────────────────────────────────────
1. A* keresés – akadálykerülő legrövidebb út, Chebyshev-heurisztikával
2. Greedy Nearest Neighbor – legközelebbi ásvány kiválasztása energia-
   és időkorlátok figyelembevételével
3. 2-opt lokális javítás – az ásványgyűjtési sorrend optimalizálása
   a teljes úthossz csökkentésére
4. Adaptív sebességválasztás – napszak és energiaszint alapján

Részletes leírás: algoritmus_leiras.pdf
