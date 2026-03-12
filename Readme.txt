================================================================
  MARS ROVER – Mission Control Terminal v2
  Vadász Dénes Informatika Verseny 2026 – Programozói Kategória
================================================================

Csapat neve: While(True)
Csapattagok:
  - Antal Máté Dániel, Miskolci SZC Kandó Kálmán Informatikai Technikum, 12.A
  - Hajdu Balázs, Miskolci SZC Kandó Kálmán Informatikai Technikum, 12.A
  - Szitovszki Máté, Miskolci SZC Kandó Kálmán Informatikai Technikum, 12.A
Felkészítő tanár: Kasza László Róbert
Email: antalm@kkszki.hu

----------------------------------------------------------------
FEJLESZTŐI KÖRNYEZET
----------------------------------------------------------------

Programnyelv:     C# (.NET 8.0)
Keretrendszer:    WPF + SkiaSharp (2D gyorsított renderelés)
IDE:              Visual Studio 2022 (v17.x)
NuGet csomagok:   SkiaSharp 2.88.8
                  SkiaSharp.Views.WPF 2.88.8
Target:           Windows x64

----------------------------------------------------------------
INDÍTÁS
----------------------------------------------------------------

1. Nyisd meg a MarsRover.sln fájlt Visual Studio 2022-ben
2. Build > Rebuild Solution
3. F5 vagy Ctrl+F5 a futtatáshoz
4. A mars_map_50x50.csv automatikusan az exe mellé másolódik

Ha kézzel futtatod: az exe a bin\Debug\net8.0-windows\win-x64\
mappában van, a CSV-nek mellette kell lennie.

----------------------------------------------------------------
KEZELÉS
----------------------------------------------------------------

JOBB PANEL:
  - ÓRA mező:      Szimuláció időkerete órában (min. 24)
  - SPD slider:     Animáció sebesség (20–600 ms/tick)
  - ▶ START:        AI megtervezi az útvonalat, szimuláció indul
  - ║║ / ▶:         Szünet / folytatás
  - ↺ RST:          Teljes újraindítás
  - CSV:            Részletes napló exportálása fájlba

TÉRKÉP NAVIGÁCIÓ:
  - Egér görgő:     Zoom (az egér pozíciójára zoomol)
  - Egér húzás:     Térkép mozgatása
  - WASD:           Térkép mozgatása billentyűzettel
  - +/-:            Zoom billentyűzettel

Kinagyítva (zoom ≥14x) megjelennek az ásvány betűjelek
(B/Y/G) és a rover jelölése (R).

----------------------------------------------------------------
MEGJELENÍTÉS
----------------------------------------------------------------

A térkép SkiaSharp 2D renderelést használ (nem 3D Viewport),
ami gyors és reszponzív marad nagy zoom-szinteken is.
Csak a képernyőn látható cellák kerülnek kirajzolásra.

Retro CRT terminál stílus:
  - Foszforzöld szövegek, sötét háttér, Consolas font
  - ASCII progress bar-ok az akkumulátorhoz és ásványokhoz
  - Éjszakai kék overlay a napszakváltásnál

Térkép színkódok:
  - Kék cella:      Kék ásvány (B)
  - Sárga cella:    Sárga ásvány (Y)
  - Zöld cella:     Zöld ásvány (G)
  - Szürke cella:   Fal (nem járható)
  - Sötét cella:    Üres terep
  - Piros négyzet:  Rover
  - Zöld cella:     Start/bázis pozíció
  - Halvány zöld:   Rover nyomvonala
  - Narancssárga:   Tervezett célpontok

----------------------------------------------------------------
AI ÚTVONALTERVEZŐ ALGORITMUS
----------------------------------------------------------------

A rover egy többlépcsős optimalizálási eljárást használ:

1. BFS TÁVOLSÁG-MÁTRIX
   Minden ásványból és a bázisból BFS-t futtatunk, így
   megkapjuk a pontos legrövidebb távolságot bármely két
   fontos pont között. Ez figyelembe veszi a falakat
   (nem légvonalat számol).

2. KLASZTEREZÉS (DBSCAN + Union-Find)
   Az ásványokat klaszterekbe soroljuk: ha két ásvány
   BFS-távolsága ≤ 8 lépés, egy csoportba kerülnek.
   Így a közeli ásványok együtt lesznek begyűjtve.

3. KLASZTER-SORREND (Nearest Cluster)
   A klasztereket méret és közelség alapján sorba
   rendezzük – a rover mindig a legközelebbi, legsűrűbb
   klasztert célozza meg először.

4. KLASZTEREN BELÜLI SORREND (Nearest Neighbor)
   Egy klaszteren belül a legközelebbi szomszéd
   heurisztikával határozza meg a begyűjtési sorrendet.

5. GLOBÁLIS JAVÍTÁS (2-opt + Or-opt)
   A teljes útvonalra 2-opt és or-opt lokális keresést
   futtatunk, ami a klaszterek közötti átmeneteket is
   javítja.

6. FEASIBILITY ELLENŐRZÉS
   Az útvonalat végigszimulálva ellenőrzi, hogy minden
   célpont elérhető-e az időn és energián belül.
   Figyelembe veszi a nappal/éjszaka ciklust és a
   sebesség-stratégiát.

Részletes pszeudokód: algoritmus_leiras.md

----------------------------------------------------------------
SEBESSÉG-STRATÉGIA
----------------------------------------------------------------

A rover sebessége automatikusan alkalmazkodik:

              Fogyasztás    Nappali nettó    Éjszakai nettó
  Lassú       2/tick          +8/tick          -2/tick
  Normál      8/tick          +2/tick          -8/tick
  Gyors      18/tick          -8/tick         -18/tick

Nappal:   Normál az alap (nettó +2, sosem fogy el)
          80%+ akku → Gyors (3 lépés/tick)
          <20% akku → Lassú (feltöltődés)
Éjszaka:  50%+ akku → Normál
          <50% → Lassú (biztonságos)
Hazaút:   Ha szükséges, Gyors módra vált

----------------------------------------------------------------
DASHBOARD ELEMEK
----------------------------------------------------------------

  AKKUMULÁTOR:  Töltöttség %-ban, ASCII bar, energia-delta
  ÁSVÁNYOK:     Összesen + típusonként (kék/sárga/zöld)
  TELEMETRIA:   Pozíció, sebesség, akció, Sol, fázisváltás
  NAPLÓ:        Valós idejű eseménynapló (bányászás,
                napszakváltás, újratervezés, stb.)

----------------------------------------------------------------
FÁJLSTRUKTÚRA
----------------------------------------------------------------

  MarsRover.sln              Solution fájl
  MarsRover.csproj           Projekt konfiguráció
  App.xaml / App.xaml.cs     Alkalmazás + retro stílusú témák
  MainWindow.xaml            UI layout (panel + térkép)
  MainWindow.xaml.cs         Renderelés, kamera, szimuláció UI
  Engine\Pathfinder.cs       A*, BFS, klaszter-TSP tervező
  Engine\Simulator.cs        Szimulációs motor, sebesség-logika
  Models\Models.cs           Térkép, pozíció, napló modellek
  mars_map_50x50.csv         50x50-es Mars térkép
  algoritmus_leiras.md       Algoritmus dokumentáció
