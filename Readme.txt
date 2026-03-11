================================================================
  MARS ROVER - Mission Control
  Vadász Dénes Informatika Verseny 2026 - Programozói Kategória
================================================================

Csapat neve: [CSAPAT NÉV]
Csapattagok:
  - [Név 1], [Iskola], [Osztály]
  - [Név 2], [Iskola], [Osztály]
Felkészítő tanár: [Tanár neve]
Email: [email cím]

----------------------------------------------------------------
FEJLESZTŐI KÖRNYEZET
----------------------------------------------------------------
Programnyelv:     C# (.NET 8.0)
Keretrendszer:    WPF (Windows Presentation Foundation)
IDE:              Visual Studio 2022 (v17.x)
NuGet csomagok:   LiveChartsCore.SkiaSharpView.WPF 2.0.0-rc3.3
Target:           Windows x64

----------------------------------------------------------------
HASZNÁLATI ÚTMUTATÓ
----------------------------------------------------------------

1. INDÍTÁS
   - Nyisd meg a MarsRover.sln fájlt Visual Studio 2022-ben
   - Build > Rebuild Solution (Release | Any CPU)
   - F5 vagy Ctrl+F5 a futtatáshoz
   - Az exe a bin\Release\net8.0-windows\ mappában lesz
   - A mars_map_50x50.csv-nek az exe mellett kell lennie

2. KEZELÉS
   - Időkeret megadása (min. 24 óra, ajánlott: 48-72)
   - INDÍTÁS gomb: AI megtervezi az útvonalat, majd elindul
   - II / > gomb: szüneteltetés / folytatás
   - Újraindítás gomb: teljes reset, új paraméterekkel
   - CSV gomb: részletes napló exportálása
   - Animáció slider: szimuláció sebesség (20-800ms/tick)

3. AI JELLEMZŐK
   - A* keresés Chebyshev-heurisztikával (8-irányú mozgás)
   - Klaszter-tudatos Greedy Nearest Neighbor sorrend
   - 2-opt lokális javítás az úthossz minimalizálására
   - ADAPTÍV ÚJRATERVEZÉS: 20 tick-enként újraszámolja a célokat
   - Éjszakai energiatakarékos mód (napkelteváró stratégia)
   - Útba eső ásványok automatikus begyűjtése
   - Intelligens sebességválasztás (napszak + energia alapján)
   - Folyamatosan kihasználja az összes rendelkezésre álló időt

4. DASHBOARD
   - Valós idejű akkumulátor szint és energia-delta kijelzés
   - Ásványgyűjtés számlálók (kék/sárga/zöld + összesen)
   - Rover telemetria (pozíció, sebesség, akció, Sol, fázis)
   - Energia görbe grafikon
   - Ásványgyűjtés kumulatív grafikon
   - Sebesség grafikon
   - Részletes eseménynapló

----------------------------------------------------------------
ÚTVONALTERVEZŐ ALGORITMUS
----------------------------------------------------------------
Részletes leírás: algoritmus_leiras.md / .pdf
