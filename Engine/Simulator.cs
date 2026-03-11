using MarsRover.Models;

namespace MarsRover.Engine
{
    /// <summary>
    /// Mars Rover szimulációs motor v4
    /// 
    /// Fő változások:
    /// - Agresszívebb nappali sebesség: Normal (2 lépés/tick, nettó +2) az alap
    ///   Ha akku ≥80%: Fast (3 lépés, nettó -8, DE gyorsan halad + nappal feltölt)
    /// - Éjszaka: Normal ha van akku (≥40%), mert -8/tick de 16 tick alatt max -128 → 
    ///   ha 100%-ról indulsz éjszakát ~12 ticket bírod Normal-on
    /// - Sebesség-szimuláció a tervezőben is Normal-t használ (nem slow-t!)
    /// - Újratervezés CSAK ha elfogytak a célok, nem napfelkeltekor
    /// </summary>
    public class Simulator
    {
        public const int DAY_TICKS = 32;
        public const int NIGHT_TICKS = 16;
        public const int CYCLE_TICKS = 48;
        public const double MAX_BATT = 100.0;
        public const double K = 2.0;
        public const double SOLAR = 10.0;
        public const double STANDBY_DRAIN = 1.0;
        public const double MINING_DRAIN = 2.0;

        public MarsMap Map { get; }
        public int TotalHours { get; }
        public int TotalTicks => TotalHours * 2;

        public Pos RoverPos { get; private set; }
        public double Battery { get; private set; } = MAX_BATT;
        public int Tick { get; private set; } = 0;
        public int Steps { get; private set; } = 0;
        public int MinB { get; private set; } = 0;
        public int MinY { get; private set; } = 0;
        public int MinG { get; private set; } = 0;
        public int TotalMin => MinB + MinY + MinG;
        public RoverAction Action { get; private set; } = RoverAction.Standby;
        public SpeedLevel Speed { get; private set; } = SpeedLevel.Slow;
        public bool Finished => Tick >= TotalTicks;
        public bool Home { get; private set; } = false;

        public HashSet<Pos> Collected { get; } = new();
        public List<LogEntry> Log { get; } = new();
        public List<Pos> PlannedRoute { get; private set; } = new();
        public List<Pos> Trail { get; } = new();

        private Queue<Pos> _pathQ = new();
        private List<Pos> _targets = new();
        private int _targetIdx = 0;
        private bool _mining = false;
        private bool _returning = false;

        public event Action<LogEntry>? OnTick;
        public event Action? OnDone;

        public Simulator(MarsMap map, int hours)
        {
            Map = map;
            TotalHours = Math.Max(24, hours);
            RoverPos = map.StartPos;
            Trail.Add(RoverPos);
        }

        public DayPhase Phase => PhaseAt(Tick);
        public DayPhase PhaseAt(int t) => (t % CYCLE_TICKS) < DAY_TICKS ? DayPhase.Day : DayPhase.Night;
        public int TickInCycle => Tick % CYCLE_TICKS;

        public int TicksToPhaseChange
        {
            get
            {
                int ic = Tick % CYCLE_TICKS;
                return ic < DAY_TICKS ? DAY_TICKS - ic : CYCLE_TICKS - ic;
            }
        }

        public void PlanInitial()
        {
            SmartPlanner.ClearCache();
            _targets = SmartPlanner.Plan(Map, RoverPos, Collected,
                TotalTicks - Tick, Battery, TickInCycle);
            PlannedRoute = new List<Pos>(_targets);
            _targetIdx = 0;
        }

        private void Replan()
        {
            _targets = SmartPlanner.Plan(Map, RoverPos, Collected,
                TotalTicks - Tick, Battery, TickInCycle);
            PlannedRoute = new List<Pos>(_targets);
            _targetIdx = 0;
            _pathQ.Clear();
            _returning = false;
        }

        public void Step()
        {
            if (Finished) return;

            var phase = Phase;
            string ev = "";
            double energyBefore = Battery;

            int ic = Tick % CYCLE_TICKS;
            if (ic == 0 && Tick > 0) ev += "Napfelkelte! ";
            if (ic == DAY_TICKS) ev += "Napnyugta! ";

            // ── Bányászás tick ──
            if (_mining)
            {
                _mining = false;
                Action = RoverAction.Mining;
                ApplyEnergy(MINING_DRAIN, phase);

                if (!Collected.Contains(RoverPos))
                {
                    Collected.Add(RoverPos);
                    var ct = Map.Grid[RoverPos.Row, RoverPos.Col];
                    switch (ct)
                    {
                        case CellType.MineralB: MinB++; ev += "Kék!"; break;
                        case CellType.MineralY: MinY++; ev += "Sárga!"; break;
                        case CellType.MineralG: MinG++; ev += "Zöld!"; break;
                    }
                }
                EmitLog(ev, energyBefore);
                return;
            }

            // ── Éjszaka + kritikusan alacsony akku + hamarosan napkelte → várakozás ──
            if (phase == DayPhase.Night && Battery < 12 && TicksToPhaseChange <= 6 && !_returning)
            {
                Action = RoverAction.WaitingForDawn;
                ApplyEnergy(STANDBY_DRAIN, phase);
                ev += "Napkeltére vár";
                EmitLog(ev, energyBefore);
                return;
            }

            // ── Nappal + kritikus → töltés (1-2 tick elég) ──
            if (phase == DayPhase.Day && Battery < 8 && !_returning)
            {
                Action = RoverAction.Charging;
                ApplyEnergy(STANDBY_DRAIN, phase);
                ev += "Töltés";
                EmitLog(ev, energyBefore);
                return;
            }

            // ── Sebesség ──
            SpeedLevel spd = PickSpeed(phase);
            Speed = spd;
            int stepsThisTick = (int)spd;

            // ── Következő célpont ──
            if (_pathQ.Count == 0 && !_returning)
            {
                // Skip already collected
                while (_targetIdx < _targets.Count && Collected.Contains(_targets[_targetIdx]))
                    _targetIdx++;

                if (_targetIdx < _targets.Count)
                {
                    var target = _targets[_targetIdx];
                    var pathTo = AStar.FindPath(Map, RoverPos, target);
                    var pathBack = AStar.FindPath(Map, target, Map.StartPos);

                    if (pathTo != null && pathBack != null)
                    {
                        int need = pathTo.Count + 1 + pathBack.Count;
                        int left = TotalTicks - Tick;

                        if (need <= left - 2 && Battery > 3)
                        {
                            foreach (var p in pathTo) _pathQ.Enqueue(p);
                            ev += $"#{TotalMin + 1} @ {target} (d={pathTo.Count}) ";
                        }
                        else
                        {
                            _targetIdx++;
                            // Próbáljuk a következőt is
                            int tried = 0;
                            while (_targetIdx < _targets.Count && tried < 5)
                            {
                                if (Collected.Contains(_targets[_targetIdx])) { _targetIdx++; continue; }
                                var t2 = _targets[_targetIdx];
                                var p2 = AStar.FindPath(Map, RoverPos, t2);
                                var pb2 = AStar.FindPath(Map, t2, Map.StartPos);
                                if (p2 != null && pb2 != null && p2.Count + 1 + pb2.Count <= left - 2)
                                {
                                    foreach (var p in p2) _pathQ.Enqueue(p);
                                    ev += $"#{TotalMin + 1} @ {t2} (d={p2.Count}) ";
                                    break;
                                }
                                _targetIdx++;
                                tried++;
                            }
                            if (_pathQ.Count == 0 && _targetIdx >= _targets.Count)
                                GoHome(ref ev);
                        }
                    }
                    else
                    {
                        _targetIdx++;
                    }
                }
                else
                {
                    // Nincs több cél – újratervezés
                    int ticksLeft = TotalTicks - Tick;
                    int distHome = SmartPlanner.CachedDist(Map, RoverPos, Map.StartPos);
                    if (distHome == int.MaxValue) distHome = 60;

                    if (ticksLeft > distHome + 10)
                    {
                        Replan();
                        if (_targets.Count == 0) GoHome(ref ev);
                        else ev += "Újratervez! ";
                    }
                    else
                    {
                        GoHome(ref ev);
                    }
                }
            }

            // ── Mozgás ──
            int done = 0;
            for (int s = 0; s < stepsThisTick && _pathQ.Count > 0; s++)
            {
                var next = _pathQ.Dequeue();
                RoverPos = next;
                Trail.Add(next);
                Steps++;
                done++;

                if (!_returning && Map.IsMineral(next) && !Collected.Contains(next))
                {
                    _mining = true;
                    if (_targetIdx < _targets.Count && next == _targets[_targetIdx])
                        _targetIdx++;
                    ev += $"Bányász @ {next} ";
                    break;
                }

                if (_returning && next == Map.StartPos)
                {
                    Home = true;
                    ev += "Bázis!";
                }
            }

            if (done > 0)
            {
                Action = _returning ? RoverAction.Returning : RoverAction.Moving;
                double drain = K * (int)spd * (int)spd;
                ApplyEnergy(drain, phase);
            }
            else
            {
                Action = RoverAction.Standby;
                ApplyEnergy(STANDBY_DRAIN, phase);
                if (string.IsNullOrEmpty(ev)) ev = "Standby";
            }

            EmitLog(ev, energyBefore);

            if (Finished || (Home && _targetIdx >= _targets.Count))
                OnDone?.Invoke();
        }

        private void GoHome(ref string ev)
        {
            _returning = true;
            _pathQ.Clear();
            var ph = AStar.FindPath(Map, RoverPos, Map.StartPos);
            if (ph != null)
                foreach (var p in ph) _pathQ.Enqueue(p);
            ev += "Visszatérés ";
        }

        private void ApplyEnergy(double drain, DayPhase phase)
        {
            double charge = phase == DayPhase.Day ? SOLAR : 0;
            Battery = Math.Clamp(Battery - drain + charge, 0, MAX_BATT);
        }

        /// <summary>
        /// Agresszívebb sebesség-stratégia:
        /// 
        ///                  Fogy   Tölt   Nappali nettó   Éjszakai nettó
        ///   Slow  (1 lép)   2     10        +8              -2
        ///   Normal(2 lép)   8     10        +2              -8
        ///   Fast  (3 lép)  18     10        -8             -18
        /// 
        /// Nappal:
        ///   Normal az alap (nettó +2, tehát SOSEM fogy el nappal!)
        ///   Fast ha akku ≥80% (nettó -8, de 3 lépés → gyorsabban jut célba)
        ///   Slow CSAK ha akku <20% (feltöltés szükséges)
        /// 
        /// Éjszaka:
        ///   Normal ha akku ≥50% (nettó -8, de éjszaka csak 16 tick → max -128 → elég)
        ///   Slow ha akku <50% (nettó -2, biztonságos)
        /// </summary>
        private SpeedLevel PickSpeed(DayPhase phase)
        {
            if (_returning)
            {
                int distHome = _pathQ.Count;
                int ticksLeft = TotalTicks - Tick;
                if (distHome > ticksLeft && Battery > 25) return SpeedLevel.Fast;
                if (distHome > ticksLeft * 2 / 3 && Battery > 15) return SpeedLevel.Normal;
            }

            if (phase == DayPhase.Day)
            {
                // Nappal: Normal nettó +2, tehát sosem fogy el!
                if (Battery >= 80) return SpeedLevel.Fast;   // 3 lépés, nettó -8, de gyors
                if (Battery >= 20) return SpeedLevel.Normal;  // 2 lépés, nettó +2
                return SpeedLevel.Slow;                       // <20%: tölt +8/tick
            }
            else
            {
                // Éjszaka: 16 tick. Normal -8/tick = max -128 összesen
                // Ha 100%-ról induljuk: 100 - 8*16 = 100-128 = negatív!
                // De a nappali +2/tick 32 tickből +64-et ad → 100+64=164 kap → 100%
                // Szóval ha 50%+: Normal biztonságos
                if (Battery >= 50) return SpeedLevel.Normal;  // 2 lépés, nettó -8
                if (Battery >= 20) return SpeedLevel.Slow;    // 1 lépés, nettó -2
                return SpeedLevel.Slow;                       // védelem
            }
        }

        private void EmitLog(string ev, double energyBefore)
        {
            var entry = new LogEntry
            {
                Tick = Tick, SimHours = Tick * 0.5, Position = RoverPos,
                Battery = Battery, Speed = Speed, Action = Action,
                Phase = Phase, TotalDistance = Steps,
                MineralsB = MinB, MineralsY = MinY, MineralsG = MinG,
                Event = ev.Trim(), EnergyDelta = Battery - energyBefore
            };
            Log.Add(entry);
            Tick++;
            OnTick?.Invoke(entry);
        }
    }
}
