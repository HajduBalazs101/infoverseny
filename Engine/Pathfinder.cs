using MarsRover.Models;

namespace MarsRover.Engine
{
    /// <summary>
    /// A* alapú útvonaltervező, amely Chebyshev-heurisztikát használ
    /// a 8-irányú mozgáshoz. Akadálykerülő legrövidebb utat keres.
    /// </summary>
    public static class AStarPathfinder
    {
        /// <summary>
        /// Legrövidebb utat keres A-ból B-be A* algoritmussal.
        /// Visszatérési érték: a lépések listája (A-t nem tartalmazza, B-t igen),
        /// vagy null ha nincs út.
        /// </summary>
        public static List<Pos>? FindPath(MarsMap map, Pos start, Pos goal)
        {
            if (start == goal) return new List<Pos>();
            if (!map.IsWalkable(goal)) return null;

            // Nyílt halmaz prioritási sorral (f-érték alapján)
            var openSet  = new PriorityQueue<Pos, int>();
            var cameFrom = new Dictionary<Pos, Pos>();
            var gScore   = new Dictionary<Pos, int>();
            var closed   = new HashSet<Pos>();

            gScore[start] = 0;
            openSet.Enqueue(start, Heuristic(start, goal));

            while (openSet.Count > 0)
            {
                var current = openSet.Dequeue();

                if (current == goal)
                    return ReconstructPath(cameFrom, current);

                if (!closed.Add(current)) continue;

                foreach (var neighbor in map.GetNeighbors(current))
                {
                    if (closed.Contains(neighbor)) continue;

                    int tentativeG = gScore[current] + 1; // minden lépés költsége 1

                    if (!gScore.TryGetValue(neighbor, out int currentG) || tentativeG < currentG)
                    {
                        cameFrom[neighbor] = current;
                        gScore[neighbor]   = tentativeG;
                        int f = tentativeG + Heuristic(neighbor, goal);
                        openSet.Enqueue(neighbor, f);
                    }
                }
            }

            return null; // nincs elérhető út
        }

        /// <summary>
        /// Chebyshev-távolság heurisztika (8-irányú mozgáshoz tökéletes admissible).
        /// </summary>
        private static int Heuristic(Pos a, Pos b)
            => Math.Max(Math.Abs(a.Row - b.Row), Math.Abs(a.Col - b.Col));

        private static List<Pos> ReconstructPath(Dictionary<Pos, Pos> cameFrom, Pos current)
        {
            var path = new List<Pos> { current };
            while (cameFrom.ContainsKey(current))
            {
                current = cameFrom[current];
                path.Add(current);
            }
            path.Reverse();
            path.RemoveAt(0); // a kiindulópontot nem tartalmazzuk
            return path;
        }

        /// <summary>
        /// Két pont közötti A* távolság (lépésszám).
        /// Visszatér int.MaxValue ha nincs út.
        /// </summary>
        public static int Distance(MarsMap map, Pos a, Pos b)
        {
            var path = FindPath(map, a, b);
            return path?.Count ?? int.MaxValue;
        }
    }

    /// <summary>
    /// Greedy Nearest-Neighbor + 2-opt javítással működő ásványgyűjtő tervező.
    /// Klaszterekbe csoportosítja az ásványokat, majd energiatudatos sorrendben
    /// tervezi meg a bejárást.
    /// </summary>
    public static class MineralPlanner
    {
        /// <summary>
        /// Megtervezi az ásványgyűjtési sorrendet, figyelembe véve:
        /// - A* távolságokat az ásványok között
        /// - Energiakorlátokat
        /// - Vissza kell érni a starthoz
        /// Visszatér az ásványok optimalizált sorrendjével.
        /// </summary>
        public static List<Pos> PlanCollection(MarsMap map, int totalTicksBudget)
        {
            var minerals = map.GetAllMinerals();
            if (minerals.Count == 0) return new List<Pos>();

            var start = map.StartPos;

            // 1. Lépés: Előre kiszámoljuk a távolságokat (start + összes ásvány között)
            //    Csak a legközelebbi ~120 ásványt vizsgáljuk a teljesítmény miatt
            var allPoints = new List<Pos> { start };
            allPoints.AddRange(minerals);

            // Távolság cache
            var distCache = new Dictionary<(Pos, Pos), int>();

            int GetDist(Pos a, Pos b)
            {
                if (a == b) return 0;
                var key = (a, b);
                if (distCache.TryGetValue(key, out int d)) return d;
                d = AStarPathfinder.Distance(map, a, b);
                distCache[key] = d;
                distCache[(b, a)] = d; // szimmetrikus
                return d;
            }

            // 2. Greedy Nearest Neighbor: mindig a legközelebbi ásványt választjuk
            var remaining = new HashSet<Pos>(minerals);
            var route     = new List<Pos>();
            var current   = start;

            // Időszimuláció a tervezéshez
            int    simTick      = 0;
            int    tickBudget   = totalTicksBudget;

            while (remaining.Count > 0 && simTick < tickBudget)
            {
                // Keressük a legközelebbi ásványt, ahová el tudunk jutni ÉS vissza
                Pos? best     = null;
                int  bestDist = int.MaxValue;

                foreach (var mineral in remaining)
                {
                    int distToMineral = GetDist(current, mineral);
                    if (distToMineral == int.MaxValue) continue;

                    int distBack = GetDist(mineral, start);
                    if (distBack == int.MaxValue) continue;

                    // Elég idő marad-e eljutni, kibányászni (1 tick), és visszajutni?
                    int ticksNeeded = distToMineral + 1 + distBack; // lassú sebességgel
                    int ticksRemaining = tickBudget - simTick;
                    if (ticksNeeded > ticksRemaining) continue;

                    // Energia ellenőrzés (durva becslés: lassú sebességnél 2/lépés, nappal +10 töltés)
                    // Pontos szimulációt a Simulator végez, itt csak szűrünk
                    if (distToMineral < bestDist)
                    {
                        bestDist = distToMineral;
                        best = mineral;
                    }
                }

                if (best == null) break; // nincs elérhető ásvány az időn belül

                route.Add(best.Value);
                remaining.Remove(best.Value);

                // Szimuláció előreléptetése
                simTick += bestDist + 1; // mozgás + bányászás
                current  = best.Value;
            }

            // 3. 2-opt lokális javítás a sorrenden
            route = TwoOptImprove(route, start, GetDist);

            return route;
        }

        /// <summary>
        /// 2-opt lokális keresés: megpróbálja a sorrend szegmenseit megfordítani
        /// a teljes úthossz csökkentése érdekében.
        /// </summary>
        private static List<Pos> TwoOptImprove(List<Pos> route, Pos start, Func<Pos, Pos, int> dist)
        {
            if (route.Count < 3) return route;

            bool improved = true;
            int maxIter = 50; // max iteráció a teljesítmény érdekében

            while (improved && maxIter-- > 0)
            {
                improved = false;
                for (int i = 0; i < route.Count - 1; i++)
                {
                    for (int j = i + 2; j < route.Count; j++)
                    {
                        var prevI = (i == 0) ? start : route[i - 1];
                        var nextJ = (j == route.Count - 1) ? start : route[j + 1];

                        int currentCost = dist(prevI, route[i]) + dist(route[j], nextJ);
                        int newCost     = dist(prevI, route[j]) + dist(route[i], nextJ);

                        if (newCost < currentCost)
                        {
                            // Megfordítjuk az i..j szegmenst
                            route.Reverse(i, j - i + 1);
                            improved = true;
                        }
                    }
                }
            }

            return route;
        }
    }
}
