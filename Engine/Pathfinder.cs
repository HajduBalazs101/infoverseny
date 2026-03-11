using MarsRover.Models;

namespace MarsRover.Engine
{
    /// <summary>
    /// A* útvonalkereső, optimalizált.
    /// </summary>
    public static class AStar
    {
        public static List<Pos>? FindPath(MarsMap map, Pos start, Pos goal)
        {
            if (start == goal) return new List<Pos>();
            if (!map.Walkable(goal)) return null;

            var open = new PriorityQueue<Pos, int>();
            var from = new Dictionary<Pos, Pos>(512);
            var g = new Dictionary<Pos, int>(512);
            var closed = new HashSet<Pos>(512);
            var neighbors = new List<Pos>(8);

            g[start] = 0;
            open.Enqueue(start, H(start, goal));

            while (open.Count > 0)
            {
                var cur = open.Dequeue();
                if (cur == goal) return Rebuild(from, cur);
                if (!closed.Add(cur)) continue;

                map.GetNeighbors(cur, neighbors);
                foreach (var nb in neighbors)
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

        public static Dictionary<Pos, int> BfsDistances(MarsMap map, Pos start, HashSet<Pos>? targets = null)
        {
            var dist = new Dictionary<Pos, int>(map.W * map.H / 2);
            var queue = new Queue<Pos>(1024);
            dist[start] = 0;
            queue.Enqueue(start);
            var neighbors = new List<Pos>(8);
            int found = 0;
            int targetCount = targets?.Count ?? int.MaxValue;

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                int d = dist[cur];

                if (targets != null && targets.Contains(cur))
                {
                    found++;
                    if (found >= targetCount) break;
                }

                map.GetNeighbors(cur, neighbors);
                foreach (var nb in neighbors)
                {
                    if (!dist.ContainsKey(nb))
                    {
                        dist[nb] = d + 1;
                        queue.Enqueue(nb);
                    }
                }
            }
            return dist;
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
    /// Mars Rover útvonaltervező v4 – Klaszter-TSP.
    /// 
    /// Változás v3-hoz:
    /// - Feasibility szimuláció REÁLIS sebességgel (Normal nappal, Slow éjjel)
    ///   → sokkal több ásványt enged be (nem vágja le feleslegesen)
    /// - Nincs túl konzervatív slow-only becslés
    /// - A tervező a rover tényleges sebesség-stratégiáját tükrözi
    /// </summary>
    public static class SmartPlanner
    {
        private static Dictionary<Pos, Dictionary<Pos, int>>? _distMatrix;
        private static HashSet<Pos>? _allKeyPoints;

        public static void ClearCache()
        {
            _distMatrix = null;
            _allKeyPoints = null;
        }

        private static int Dist(Pos a, Pos b)
        {
            if (a == b) return 0;
            if (_distMatrix != null &&
                _distMatrix.TryGetValue(a, out var da) &&
                da.TryGetValue(b, out int d))
                return d;
            return int.MaxValue;
        }

        public static List<Pos> Plan(MarsMap map, Pos currentPos, HashSet<Pos> alreadyCollected,
            int ticksRemaining, double battery, int currentTickInCycle)
        {
            var minerals = map.AllMinerals()
                .Where(m => !alreadyCollected.Contains(m))
                .ToList();

            if (minerals.Count == 0) return new List<Pos>();

            // ═══ 1) BFS TÁVOLSÁG-MÁTRIX ═══
            _allKeyPoints = new HashSet<Pos>(minerals) { currentPos, map.StartPos };
            _distMatrix = new Dictionary<Pos, Dictionary<Pos, int>>();

            foreach (var pt in _allKeyPoints)
            {
                _distMatrix[pt] = AStar.BfsDistances(map, pt, _allKeyPoints);
            }

            minerals = minerals.Where(m =>
                Dist(currentPos, m) < int.MaxValue &&
                Dist(m, map.StartPos) < int.MaxValue
            ).ToList();

            if (minerals.Count == 0) return new List<Pos>();

            // ═══ 2) KLASZTEREZÉS ═══
            int clusterThreshold = 8;
            var clusters = ClusterByBfsDist(minerals, clusterThreshold);

            // ═══ 3) KLASZTER-SORREND ═══
            var clusterOrder = OrderClusters(clusters, currentPos, map.StartPos);

            // ═══ 4) KLASZTEREN BELÜLI NN ═══
            var fullRoute = new List<Pos>();
            var cur = currentPos;

            foreach (var cluster in clusterOrder)
            {
                var remaining = new HashSet<Pos>(cluster);
                while (remaining.Count > 0)
                {
                    Pos? nearest = null;
                    int nearestDist = int.MaxValue;

                    foreach (var m in remaining)
                    {
                        int d = Dist(cur, m);
                        if (d < nearestDist)
                        {
                            nearestDist = d;
                            nearest = m;
                        }
                    }

                    if (nearest == null) break;
                    fullRoute.Add(nearest.Value);
                    remaining.Remove(nearest.Value);
                    cur = nearest.Value;
                }
            }

            if (fullRoute.Count < 2) return fullRoute;

            // ═══ 5) 2-OPT + OR-OPT + 2-OPT ═══
            fullRoute = TwoOpt(fullRoute, currentPos, 200);
            fullRoute = OrOpt(fullRoute, currentPos, 100);
            fullRoute = TwoOpt(fullRoute, currentPos, 100);

            // ═══ 6) FEASIBILITY ═══
            fullRoute = TrimToFeasible(fullRoute, currentPos, map.StartPos,
                ticksRemaining, battery, currentTickInCycle);

            return fullRoute;
        }

        private static List<List<Pos>> ClusterByBfsDist(List<Pos> minerals, int threshold)
        {
            int n = minerals.Count;
            int[] parent = new int[n];
            int[] rank = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }

            void Union(int a, int b)
            {
                a = Find(a); b = Find(b);
                if (a == b) return;
                if (rank[a] < rank[b]) (a, b) = (b, a);
                parent[b] = a;
                if (rank[a] == rank[b]) rank[a]++;
            }

            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    int d = Dist(minerals[i], minerals[j]);
                    if (d <= threshold)
                        Union(i, j);
                }

            var groups = new Dictionary<int, List<Pos>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (!groups.TryGetValue(root, out var list))
                {
                    list = new List<Pos>();
                    groups[root] = list;
                }
                list.Add(minerals[i]);
            }

            return groups.Values.ToList();
        }

        private static List<List<Pos>> OrderClusters(List<List<Pos>> clusters, Pos startPos, Pos basePos)
        {
            var remaining = new List<List<Pos>>(clusters);
            var ordered = new List<List<Pos>>();
            var cur = startPos;

            while (remaining.Count > 0)
            {
                int bestIdx = -1;
                double bestScore = double.MaxValue;

                for (int i = 0; i < remaining.Count; i++)
                {
                    int minDist = int.MaxValue;
                    foreach (var m in remaining[i])
                    {
                        int d = Dist(cur, m);
                        if (d < minDist) minDist = d;
                    }
                    if (minDist == int.MaxValue) continue;

                    // Közelség - klaszterméret bonus
                    double score = minDist - remaining[i].Count * 0.8;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestIdx = i;
                    }
                }

                if (bestIdx < 0) break;

                var chosen = remaining[bestIdx];
                ordered.Add(chosen);
                remaining.RemoveAt(bestIdx);

                // Kilépő pont: a klaszterből a legközelebbi a megmaradt klaszterekhez
                int bestExit = int.MaxValue;
                Pos exitPos = chosen[0];

                if (remaining.Count > 0)
                {
                    foreach (var m in chosen)
                    {
                        foreach (var nextC in remaining)
                        {
                            foreach (var nm in nextC)
                            {
                                int d = Dist(m, nm);
                                if (d < bestExit)
                                {
                                    bestExit = d;
                                    exitPos = m;
                                }
                            }
                        }
                    }
                }
                else
                {
                    foreach (var m in chosen)
                    {
                        int dBase = Dist(m, basePos);
                        if (dBase < bestExit)
                        {
                            bestExit = dBase;
                            exitPos = m;
                        }
                    }
                }
                cur = exitPos;
            }

            return ordered;
        }

        private static List<Pos> TwoOpt(List<Pos> route, Pos start, int maxIter)
        {
            bool improved = true;
            while (improved && maxIter-- > 0)
            {
                improved = false;
                for (int i = 0; i < route.Count - 1; i++)
                {
                    for (int j = i + 2; j < route.Count; j++)
                    {
                        var pi = i == 0 ? start : route[i - 1];
                        var nj = j == route.Count - 1 ? start : route[j + 1];

                        int d1 = Dist(pi, route[i]);
                        int d2 = Dist(route[j], nj);
                        int d3 = Dist(pi, route[j]);
                        int d4 = Dist(route[i], nj);

                        if (d1 == int.MaxValue || d2 == int.MaxValue ||
                            d3 == int.MaxValue || d4 == int.MaxValue) continue;

                        if (d3 + d4 < d1 + d2)
                        {
                            route.Reverse(i, j - i + 1);
                            improved = true;
                        }
                    }
                }
            }
            return route;
        }

        private static List<Pos> OrOpt(List<Pos> route, Pos start, int maxIter)
        {
            if (route.Count < 4) return route;
            bool improved = true;
            while (improved && maxIter-- > 0)
            {
                improved = false;
                for (int segLen = 1; segLen <= 3; segLen++)
                {
                    for (int i = 0; i <= route.Count - segLen; i++)
                    {
                        var prevI = i == 0 ? start : route[i - 1];
                        var afterSeg = (i + segLen < route.Count) ? route[i + segLen] : start;

                        int removeCost = Dist(prevI, route[i]) + Dist(route[i + segLen - 1], afterSeg);
                        int bridgeCost = Dist(prevI, afterSeg);
                        if (removeCost == int.MaxValue || bridgeCost == int.MaxValue) continue;
                        int gain = removeCost - bridgeCost;
                        if (gain <= 0) continue;

                        for (int j = 0; j <= route.Count - segLen; j++)
                        {
                            if (j >= i && j <= i + segLen) continue;
                            var prevJ = j == 0 ? start : route[j - 1];
                            var atJ = route[j];

                            int oldEdge = Dist(prevJ, atJ);
                            int newEdge = Dist(prevJ, route[i]) + Dist(route[i + segLen - 1], atJ);
                            if (oldEdge == int.MaxValue || newEdge == int.MaxValue) continue;

                            if (gain - (newEdge - oldEdge) > 0)
                            {
                                var segment = route.GetRange(i, segLen);
                                route.RemoveRange(i, segLen);
                                int insertAt = j > i ? j - segLen : j;
                                route.InsertRange(insertAt, segment);
                                improved = true;
                                goto nextIter;
                            }
                        }
                    }
                }
                nextIter:;
            }
            return route;
        }

        /// <summary>
        /// Feasibility: szimulál reális sebességgel (Normal nappal, Slow éjjel).
        /// 
        /// A lépések (steps) és a tick-ek viszonya sebességfüggő!
        /// Normal = 2 step/tick, tehát d lépés = ceil(d/2) tick nappal
        /// Slow = 1 step/tick, tehát d lépés = d tick éjjel
        /// 
        /// Egyszerűsítés: átlagosan ~1.5 step/tick → tick = ceil(d / 1.5)
        /// De a biztonság kedvéért: tick = ceil(d / 1.5) a becslés
        /// </summary>
        private static List<Pos> TrimToFeasible(List<Pos> route, Pos currentPos, Pos basePos,
            int ticksRemaining, double battery, int cycleTick)
        {
            var feasible = new List<Pos>();
            var cur = currentPos;
            int simTick = 0;
            double simBatt = battery;
            int simCycleTick = cycleTick;

            for (int idx = 0; idx < route.Count; idx++)
            {
                var target = route[idx];
                int dTo = Dist(cur, target);
                int dBack = Dist(target, basePos);
                if (dTo == int.MaxValue || dBack == int.MaxValue) continue;

                // Tick-becslés: Normal (2 step/tick) nappal, Slow (1) éjjel
                // Átlagosan: ~1.6 step/tick (mert 32/48 nappal, 16/48 éjjel)
                int ticksTo = EstimateTicks(dTo, simCycleTick);
                int ticksBack = EstimateTicks(dBack, (simCycleTick + ticksTo + 1) % 48);
                int ticksMine = 1;

                int totalTicks = ticksTo + ticksMine + ticksBack + 2; // 2 tartalék
                int ticksLeft = ticksRemaining - simTick;

                if (totalTicks > ticksLeft) continue;

                // Energia: szimulálás tick-enként, vegyes sebességgel
                double simBattAfterGo = SimulateEnergyRealistic(ticksTo + ticksMine, simBatt, simCycleTick);
                double simBattAfterBack = SimulateEnergyRealistic(ticksBack, simBattAfterGo, (simCycleTick + ticksTo + 1) % 48);

                if (simBattAfterBack < 1) continue;

                feasible.Add(target);
                simTick += ticksTo + ticksMine;
                simBatt = simBattAfterGo;
                simCycleTick = (simCycleTick + ticksTo + ticksMine) % 48;
                cur = target;
            }

            return feasible;
        }

        /// <summary>
        /// Tick-becslés figyelembe véve a sebességet a napszak alapján.
        /// Normal (2 step/tick) nappal, Slow (1 step/tick) éjjel.
        /// </summary>
        private static int EstimateTicks(int steps, int cycleTick)
        {
            int ticks = 0;
            int remaining = steps;
            int ct = cycleTick;

            while (remaining > 0)
            {
                bool isDay = (ct % 48) < 32;
                int stepsPerTick = isDay ? 2 : 1; // Normal nappal, Slow éjjel
                remaining -= stepsPerTick;
                ticks++;
                ct++;
            }
            return ticks;
        }

        /// <summary>
        /// Energia szimulálás reális sebességgel.
        /// Normal nappal (drain=8, charge=10 → nettó +2), Slow éjjel (drain=2 → nettó -2).
        /// </summary>
        private static double SimulateEnergyRealistic(int ticks, double battery, int cycleTick)
        {
            double b = battery;
            for (int i = 0; i < ticks && i < 500; i++)
            {
                int ct = (cycleTick + i) % 48;
                bool isDay = ct < 32;

                if (isDay)
                {
                    // Normal: drain = K*2² = 8, charge = 10 → nettó +2
                    b = Math.Clamp(b - 8 + 10, 0, 100);
                }
                else
                {
                    // Slow: drain = K*1² = 2, charge = 0 → nettó -2
                    b = Math.Clamp(b - 2, 0, 100);
                }
            }
            return b;
        }

        public static int CachedDist(MarsMap map, Pos a, Pos b)
        {
            if (a == b) return 0;
            int d = Dist(a, b);
            if (d < int.MaxValue) return d;
            var path = AStar.FindPath(map, a, b);
            return path?.Count ?? int.MaxValue;
        }
    }
}
