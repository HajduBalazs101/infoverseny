using MarsRover.Models;

namespace MarsRover.Engine
{
    /// <summary>
    /// Mars Rover szimulációs motor – okos energiamenedzsmenttel,
    /// adaptív újratervezéssel, éjszakai energiatakarékos móddal.
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
        private const int REPLAN_INTERVAL = 20; // tick-enként újratervez

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
        private int _lastReplanTick = -999;

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

        /// <summary>Első tervezés indítás előtt.</summary>
        public void PlanInitial()
        {
            SmartPlanner.ClearCache();
            Replan();
        }

        /// <summary>Adaptív újratervezés: újraszámolja a hátralevő célokat.</summary>
        private void Replan()
        {
            _targets = SmartPlanner.Plan(Map, RoverPos, Collected,
                TotalTicks - Tick, Battery, TickInCycle);
            PlannedRoute = new List<Pos>(_targets);
            _targetIdx = 0;
            _pathQ.Clear();
            _returning = false;
            _lastReplanTick = Tick;
        }

        /// <summary>Egy fél-órás tick szimulálása.</summary>
        public void Step()
        {
            if (Finished) return;

            var phase = Phase;
            string ev = "";
            double energyBefore = Battery;

            // Napszak váltás jelzés
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
                        case CellType.MineralB: MinB++; ev += "Kék ásvány begyűjtve!"; break;
                        case CellType.MineralY: MinY++; ev += "Sárga ásvány begyűjtve!"; break;
                        case CellType.MineralG: MinG++; ev += "Zöld ásvány begyűjtve!"; break;
                    }
                }
                EmitLog(ev, energyBefore);
                return;
            }

            // ── Periodikus újratervezés (ADAPTÍV AI) ──
            if (!_returning && Tick - _lastReplanTick >= REPLAN_INTERVAL && _targetIdx > 0)
            {
                Replan();
                ev += "AI újratervez... ";
            }

            // ── Éjszakai energiatakarékos mód ──
            // Ha éjszaka van ÉS alacsony az energia ÉS hamarosan napfelkelte → várjunk
            if (phase == DayPhase.Night && Battery < 25 && TicksToPhaseChange <= 6 && !_returning)
            {
                Action = RoverAction.WaitingForDawn;
                ApplyEnergy(STANDBY_DRAIN, phase);
                ev += "Napfelkeltére várakozás (energiatakarékos)";
                EmitLog(ev, energyBefore);
                return;
            }

            // ── Sebesség választás ──
            SpeedLevel spd = PickSpeed(phase);
            Speed = spd;
            int stepsThisTick = (int)spd;

            // ── Következő úticél keresése ──
            if (_pathQ.Count == 0 && !_returning)
            {
                if (_targetIdx < _targets.Count)
                {
                    var target = _targets[_targetIdx];

                    // Ellenőrzés: van elég idő+energia?
                    var pathTo = AStar.FindPath(Map, RoverPos, target);
                    var pathBack = AStar.FindPath(Map, target, Map.StartPos);

                    if (pathTo != null && pathBack != null)
                    {
                        int need = pathTo.Count + 1 + pathBack.Count;
                        int left = TotalTicks - Tick;

                        if (need <= left - 2 && Battery > 8)
                        {
                            foreach (var p in pathTo) _pathQ.Enqueue(p);
                            ev += $"Cél: ásvány #{TotalMin + 1} @ {target} ";
                        }
                        else
                        {
                            // Nem fér bele → újratervezés rövidebb listával
                            _targetIdx++;
                            if (_targetIdx >= _targets.Count)
                            {
                                GoHome(ref ev);
                            }
                        }
                    }
                    else
                    {
                        _targetIdx++; // elérhetetlen, skip
                    }
                }
                else
                {
                    // Nincs több célpont → de van idő? Újratervezés!
                    int ticksLeft = TotalTicks - Tick;
                    int distHome = SmartPlanner.CachedDist(Map, RoverPos, Map.StartPos);
                    if (distHome == int.MaxValue) distHome = 50;

                    if (ticksLeft > distHome + 10)
                    {
                        // Van még idő, próbáljunk újabb ásványokat keresni
                        Replan();
                        if (_targets.Count == 0)
                        {
                            GoHome(ref ev);
                        }
                        else
                        {
                            ev += "Újratervezett útvonal! ";
                        }
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

                // Ásvány elérve?
                if (!_returning && Map.IsMineral(next) && !Collected.Contains(next))
                {
                    if (_targetIdx < _targets.Count && next == _targets[_targetIdx])
                    {
                        _mining = true;
                        _targetIdx++;
                        ev += $"Bányászás @ {next} ";
                        break;
                    }
                    // Útba eső ásvány: ingyen begyűjtés!
                    else
                    {
                        _mining = true;
                        ev += $"Útba eső ásvány @ {next} ";
                        break;
                    }
                }

                if (_returning && next == Map.StartPos)
                {
                    Home = true;
                    ev += "Visszaérkezett a bázisra!";
                }
            }

            // ── Energia ──
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
            ev += "Visszatérés a bázisra ";
        }

        private void ApplyEnergy(double drain, DayPhase phase)
        {
            double charge = phase == DayPhase.Day ? SOLAR : 0;
            Battery = Math.Clamp(Battery - drain + charge, 0, MAX_BATT);
        }

        private SpeedLevel PickSpeed(DayPhase phase)
        {
            // Visszatérésnél: gyorsítsunk ha kell
            if (_returning)
            {
                int distHome = _pathQ.Count;
                int ticksLeft = TotalTicks - Tick;
                if (distHome > ticksLeft * 2 && Battery > 30) return SpeedLevel.Fast;
                if (distHome > ticksLeft && Battery > 20) return SpeedLevel.Normal;
            }

            if (Battery <= 12) return SpeedLevel.Slow;

            if (phase == DayPhase.Day)
            {
                if (Battery >= 65) return SpeedLevel.Fast;
                if (Battery >= 30) return SpeedLevel.Normal;
                return SpeedLevel.Slow;
            }
            else
            {
                // Éjszaka: óvatos
                if (Battery >= 75) return SpeedLevel.Normal;
                return SpeedLevel.Slow;
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
