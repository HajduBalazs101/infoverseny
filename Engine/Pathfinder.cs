using MarsRover.Models;

namespace MarsRover.Engine
{
    /// <summary>
    /// A* útvonalkereső Chebyshev-heurisztikával (8-irányú mozgás).
    /// </summary>
    public static class AStar
    {
        public static List<Pos>? FindPath(MarsMap map, Pos start, Pos goal)
        {
            if (start == goal) return new List<Pos>();
            if (!map.Walkable(goal)) return null;

            var open = new PriorityQueue<Pos, int>();
            var from = new Dictionary<Pos, Pos>();
            var g = new Dictionary<Pos, int>();
            var closed = new HashSet<Pos>();

            g[start] = 0;
            open.Enqueue(start, H(start, goal));

            while (open.Count > 0)
            {
                var cur = open.Dequeue();
                if (cur == goal) return Rebuild(from, cur);
                if (!closed.Add(cur)) continue;

                foreach (var nb in map.Neighbors(cur))
                {
                    if (closed.Contains(nb)) continue;
                    int tg = g[cur] + 1;
                    if (!g.TryGetValue(nb, out int og) || tg < og)
                    {
                        from[nb] = cur;
                        g[nb] = tg;
                        open.Enqueue(nb, tg + H(nb, goal));
                    }
                }
            }
            return null;
        }

        public static int Dist(MarsMap map, Pos a, Pos b)
        {
            var p = FindPath(map, a, b);
            return p?.Count ?? int.MaxValue;
        }

        private static int H(Pos a, Pos b) => a.ChebyshevTo(b);

        private static List<Pos> Rebuild(Dictionary<Pos, Pos> from, Pos cur)
        {
            var path = new List<Pos> { cur };
            while (from.ContainsKey(cur)) { cur = from[cur]; path.Add(cur); }
            path.Reverse();
            path.RemoveAt(0);
            return path;
        }
    }

    /// <summary>
    /// Adaptív ásványgyűjtő tervező:
    /// - Greedy Nearest Neighbor alapú sorrend
    /// - 2-opt javítás
    /// - Folyamatos újratervezés ha az idő/energia engedi
    /// - Klaszter-tudatos: közeli ásványok csoportos begyűjtése
    /// </summary>
    public static class SmartPlanner
    {
        private static readonly Dictionary<(Pos, Pos), int> _cache = new();

        public static void ClearCache() => _cache.Clear();

        public static int CachedDist(MarsMap map, Pos a, Pos b)
        {
            if (a == b) return 0;
            var key = (a, b);
            if (_cache.TryGetValue(key, out int d)) return d;
            d = AStar.Dist(map, a, b);
            _cache[key] = d;
            _cache[(b, a)] = d;
            return d;
        }

        /// <summary>
        /// Megtervezi a gyűjtési sorrendet az aktuális pozícióból,
        /// figyelembe véve a maradék időt és energiát.
        /// Újrahívható menet közben (adaptív újratervezés).
        /// </summary>
        public static List<Pos> Plan(MarsMap map, Pos currentPos, HashSet<Pos> alreadyCollected,
            int ticksRemaining, double battery, int currentTickInCycle)
        {
            var minerals = map.AllMinerals()
                .Where(m => !alreadyCollected.Contains(m))
                .ToList();

            if (minerals.Count == 0) return new List<Pos>();

            // Greedy Nearest Neighbor az idő- és energiakorlátokkal
            var remaining = new HashSet<Pos>(minerals);
            var route = new List<Pos>();
            var cur = currentPos;
            int simTick = 0;
            double simBatt = battery;
            int simCycleTick = currentTickInCycle;

            while (remaining.Count > 0 && simTick < ticksRemaining)
            {
                Pos? best = null;
                int bestDist = int.MaxValue;
                double bestScore = double.MinValue;

                foreach (var m in remaining)
                {
                    int dTo = CachedDist(map, cur, m);
                    if (dTo == int.MaxValue) continue;
                    int dBack = CachedDist(map, m, map.StartPos);
                    if (dBack == int.MaxValue) continue;

                    // Kell idő: odajutás + bányászás(1 tick) + hazajutás
                    int ticksNeeded = dTo + 1 + dBack;
                    int ticksLeft = ticksRemaining - simTick;
                    if (ticksNeeded > ticksLeft - 2) continue; // 2 tick tartalék

                    // Energia becslés: lassú sebességnél legrosszabb eset
                    double estEnergy = EstimateEnergy(dTo + 1, simBatt, simCycleTick);
                    if (estEnergy < 5) continue;

                    // Pontozás: közelség + klaszter-bonus (közeli ásványokhoz közel)
                    int clusterBonus = 0;
                    foreach (var other in remaining)
                    {
                        if (other == m) continue;
                        if (m.ChebyshevTo(other) <= 5) clusterBonus++;
                    }

                    double score = -dTo + clusterBonus * 3.0;

                    if (score > bestScore || (Math.Abs(score - bestScore) < 0.01 && dTo < bestDist))
                    {
                        bestScore = score;
                        bestDist = dTo;
                        best = m;
                    }
                }

                if (best == null) break;

                route.Add(best.Value);
                remaining.Remove(best.Value);
                simTick += bestDist + 1;
                simBatt = EstimateEnergy(bestDist + 1, simBatt, simCycleTick);
                simCycleTick = (simCycleTick + bestDist + 1) % 48;
                cur = best.Value;
            }

            // 2-opt javítás
            if (route.Count >= 3)
                route = TwoOpt(route, currentPos, map);

            return route;
        }

        /// <summary>Becslés: mennyi energia marad N tick után.</summary>
        private static double EstimateEnergy(int ticks, double battery, int cycleTick)
        {
            double b = battery;
            for (int i = 0; i < ticks; i++)
            {
                int ct = (cycleTick + i) % 48;
                bool isDay = ct < 32;
                double drain = 2; // lassú sebesség
                double charge = isDay ? 10 : 0;
                b = Math.Clamp(b - drain + charge, 0, 100);
            }
            return b;
        }

        private static List<Pos> TwoOpt(List<Pos> route, Pos start, MarsMap map)
        {
            bool improved = true;
            int maxIter = 80;
            while (improved && maxIter-- > 0)
            {
                improved = false;
                for (int i = 0; i < route.Count - 1; i++)
                {
                    for (int j = i + 2; j < route.Count; j++)
                    {
                        var pi = i == 0 ? start : route[i - 1];
                        var nj = j == route.Count - 1 ? start : route[j + 1];
                        int oldC = CachedDist(map, pi, route[i]) + CachedDist(map, route[j], nj);
                        int newC = CachedDist(map, pi, route[j]) + CachedDist(map, route[i], nj);
                        if (newC < oldC)
                        {
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
