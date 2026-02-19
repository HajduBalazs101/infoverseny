using System.IO;
using MarsRover.Models;

namespace MarsRover.Engine
{
    /// <summary>
    /// A Mars Rover szimuláció fő motorja.
    /// Fél-óránkénti tickekben működik, kezeli:
    /// - mozgás (3 sebesség)
    /// - nappal/éjszaka ciklus (16h/8h)
    /// - akkumulátor töltés/fogyasztás
    /// - ásványgyűjtés
    /// - eseménynapló
    /// </summary>
    public class Simulator
    {
        // ── Konstansok ──
        public const int DAY_TICKS   = 32;  // 16 óra × 2 tick/óra
        public const int NIGHT_TICKS = 16;  // 8 óra × 2 tick/óra
        public const int CYCLE_TICKS = 48;  // 24 óra
        public const double MAX_BATTERY     = 100.0;
        public const double K_ENERGY        = 2.0;
        public const double SOLAR_CHARGE    = 10.0; // nappal töltés / tick
        public const double STANDBY_DRAIN   = 1.0;  // standby fogyasztás / tick
        public const double MINING_DRAIN    = 2.0;  // bányászás fogyasztás / tick

        // ── Állapot ──
        public MarsMap Map { get; }
        public int TotalHours { get; }
        public int TotalTicks => TotalHours * 2;

        public Pos    RoverPos     { get; private set; }
        public double Battery      { get; private set; } = MAX_BATTERY;
        public int    CurrentTick  { get; private set; } = 0;
        public int    StepsTotal   { get; private set; } = 0;
        public int    MineralsB    { get; private set; } = 0;
        public int    MineralsY    { get; private set; } = 0;
        public int    MineralsG    { get; private set; } = 0;
        public int    TotalMinerals => MineralsB + MineralsY + MineralsG;
        public RoverAction CurrentAction { get; private set; } = RoverAction.Standby;
        public SpeedLevel  CurrentSpeed  { get; private set; } = SpeedLevel.Slow;

        public bool IsFinished => CurrentTick >= TotalTicks;
        public bool ReturnedHome { get; private set; } = false;

        // Begyűjtött ásványok pozíciói
        public HashSet<Pos> CollectedMinerals { get; } = new();

        // Napló
        public List<LogEntry> Log { get; } = new();

        // Tervezett útvonal (vizualizációhoz)
        public List<Pos> PlannedRoute { get; private set; } = new();
        public List<Pos> ActualPath   { get; } = new();

        // Aktuális mozgási célpont és az ahhoz vezető A* útvonal
        private Queue<Pos> _currentPathQueue = new();
        private List<Pos>  _mineralTargets   = new();
        private int        _mineralTargetIdx = 0;
        private bool       _isMining         = false;
        private bool       _isReturning      = false;

        // Események callback
        public event Action<LogEntry>? OnTickCompleted;
        public event Action? OnSimulationFinished;

        public Simulator(MarsMap map, int totalHours)
        {
            Map        = map;
            TotalHours = Math.Max(24, totalHours);
            RoverPos   = map.StartPos;
            ActualPath.Add(RoverPos);
        }

        /// <summary>Megtervezi az ásványgyűjtési sorrendet.</summary>
        public void PlanRoute()
        {
            _mineralTargets = MineralPlanner.PlanCollection(Map, TotalTicks);
            PlannedRoute    = new List<Pos>(_mineralTargets);
            _mineralTargetIdx = 0;
        }

        /// <summary>Napszak meghatározása a tick alapján.</summary>
        public DayPhase GetPhase(int tick)
        {
            int inCycle = tick % CYCLE_TICKS;
            return inCycle < DAY_TICKS ? DayPhase.Day : DayPhase.Night;
        }

        public DayPhase CurrentPhase => GetPhase(CurrentTick);

        /// <summary>Mennyi idő van még hátra az aktuális napszakban (tickben).</summary>
        public int TicksUntilPhaseChange
        {
            get
            {
                int inCycle = CurrentTick % CYCLE_TICKS;
                if (inCycle < DAY_TICKS) return DAY_TICKS - inCycle;
                return CYCLE_TICKS - inCycle;
            }
        }

        /// <summary>
        /// Egyetlen fél-órás tick szimulálása.
        /// Ez az a metódus amit a UI timer hív meg ismételten.
        /// </summary>
        public void SimulateTick()
        {
            if (IsFinished) return;

            var phase = CurrentPhase;
            string eventText = "";

            // ── Ha éppen bányászik ──
            if (_isMining)
            {
                _isMining = false;
                CurrentAction = RoverAction.Mining;

                // Bányászás energiaköltsége
                double drain = MINING_DRAIN;
                double charge = (phase == DayPhase.Day) ? SOLAR_CHARGE : 0;
                Battery = Math.Clamp(Battery - drain + charge, 0, MAX_BATTERY);

                // Ásvány begyűjtése
                var cellType = Map.Grid[RoverPos.Row, RoverPos.Col];
                if (!CollectedMinerals.Contains(RoverPos))
                {
                    CollectedMinerals.Add(RoverPos);
                    switch (cellType)
                    {
                        case CellType.MineralB: MineralsB++; eventText = "🔵 Kék ásvány begyűjtve"; break;
                        case CellType.MineralY: MineralsY++; eventText = "🟡 Sárga ásvány begyűjtve"; break;
                        case CellType.MineralG: MineralsG++; eventText = "🟢 Zöld ásvány begyűjtve"; break;
                    }
                }

                LogTick(eventText);
                return;
            }

            // ── Sebesség meghatározása ──
            SpeedLevel speed = ChooseSpeed(phase);
            CurrentSpeed = speed;
            int stepsThisTick = (int)speed;

            // ── Ha nincs úticél, következő ásvány vagy visszatérés ──
            if (_currentPathQueue.Count == 0)
            {
                if (!_isReturning && _mineralTargetIdx < _mineralTargets.Count)
                {
                    // Következő ásvány felé indulás
                    var target = _mineralTargets[_mineralTargetIdx];

                    // Ellenőrzés: elég idő és energia van-e
                    var pathToTarget = AStarPathfinder.FindPath(Map, RoverPos, target);
                    var pathBack     = AStarPathfinder.FindPath(Map, target, Map.StartPos);

                    if (pathToTarget != null && pathBack != null)
                    {
                        int ticksNeeded = pathToTarget.Count + 1 + pathBack.Count;
                        int ticksLeft   = TotalTicks - CurrentTick;

                        if (ticksNeeded <= ticksLeft && Battery > 10)
                        {
                            foreach (var p in pathToTarget) _currentPathQueue.Enqueue(p);
                            eventText = $"Úticél: ásvány #{_mineralTargetIdx + 1} @ {target}";
                        }
                        else
                        {
                            // Nincs elég idő, visszatérés a bázisra
                            StartReturn();
                            eventText = "⏱ Idő/energia kevés → visszatérés a bázisra";
                        }
                    }
                    else
                    {
                        _mineralTargetIdx++;
                    }
                }
                else if (!_isReturning)
                {
                    StartReturn();
                    eventText = "✅ Összes elérhető ásvány begyűjtve → visszatérés";
                }
            }

            // ── Mozgás végrehajtása ──
            int stepsDone = 0;
            for (int s = 0; s < stepsThisTick && _currentPathQueue.Count > 0; s++)
            {
                var next = _currentPathQueue.Dequeue();
                RoverPos = next;
                ActualPath.Add(next);
                StepsTotal++;
                stepsDone++;

                // Ha megérkeztünk egy ásványra, bányászás indítása
                if (!_isReturning && Map.IsMineral(next) && !CollectedMinerals.Contains(next))
                {
                    // Ha ez a célpont
                    if (_mineralTargetIdx < _mineralTargets.Count && next == _mineralTargets[_mineralTargetIdx])
                    {
                        _isMining = true;
                        _mineralTargetIdx++;
                        eventText += (eventText.Length > 0 ? " | " : "") + $"⛏ Bányászás elkezdve @ {next}";
                        break;
                    }
                }

                // Hazaérkeztünk?
                if (_isReturning && next == Map.StartPos)
                {
                    ReturnedHome = true;
                    eventText += (eventText.Length > 0 ? " | " : "") + "🏠 Visszaérkezett a bázisra!";
                }
            }

            // ── Energia számítás ──
            if (stepsDone > 0)
            {
                CurrentAction = _isReturning ? RoverAction.Returning : RoverAction.Moving;
                double drain  = K_ENERGY * (int)speed * (int)speed; // k * v²
                double charge = (phase == DayPhase.Day) ? SOLAR_CHARGE : 0;
                Battery = Math.Clamp(Battery - drain + charge, 0, MAX_BATTERY);
            }
            else
            {
                // Standby
                CurrentAction = RoverAction.Standby;
                double drain  = STANDBY_DRAIN;
                double charge = (phase == DayPhase.Day) ? SOLAR_CHARGE : 0;
                Battery = Math.Clamp(Battery - drain + charge, 0, MAX_BATTERY);

                if (string.IsNullOrEmpty(eventText))
                    eventText = "⏸ Standby";
            }

            // Napszak váltás jelzés
            int inCycle = CurrentTick % CYCLE_TICKS;
            if (inCycle == 0) eventText = "🌅 Napfelkelte! " + eventText;
            else if (inCycle == DAY_TICKS) eventText = "🌙 Napnyugta! " + eventText;

            LogTick(eventText);

            // Vége?
            if (IsFinished || (ReturnedHome && _mineralTargetIdx >= _mineralTargets.Count))
            {
                OnSimulationFinished?.Invoke();
            }
        }

        /// <summary>Visszatérési útvonal tervezése a starthoz.</summary>
        private void StartReturn()
        {
            _isReturning = true;
            _currentPathQueue.Clear();
            var pathHome = AStarPathfinder.FindPath(Map, RoverPos, Map.StartPos);
            if (pathHome != null)
            {
                foreach (var p in pathHome) _currentPathQueue.Enqueue(p);
            }
        }

        /// <summary>
        /// Sebesség kiválasztása az aktuális helyzet alapján.
        /// Stratégia:
        /// - Nappal és sok az energia → gyors
        /// - Nappal és közepes energia → normál
        /// - Éjszaka vagy kevés energia → lassú
        /// </summary>
        private SpeedLevel ChooseSpeed(DayPhase phase)
        {
            if (Battery <= 15)
                return SpeedLevel.Slow;

            if (phase == DayPhase.Day)
            {
                // Nappal: gyors ha sok energia, normál ha közepes
                if (Battery >= 60) return SpeedLevel.Fast;
                if (Battery >= 30) return SpeedLevel.Normal;
                return SpeedLevel.Slow;
            }
            else
            {
                // Éjszaka: energia-takarékos mód
                if (Battery >= 70) return SpeedLevel.Normal;
                return SpeedLevel.Slow;
            }
        }

        /// <summary>Napló bejegyzés létrehozása és kibocsátása.</summary>
        private void LogTick(string eventText)
        {
            var entry = new LogEntry
            {
                Tick         = CurrentTick,
                SimHours     = CurrentTick * 0.5,
                Position     = RoverPos,
                Battery      = Battery,
                Speed        = CurrentSpeed,
                Action       = CurrentAction,
                Phase        = CurrentPhase,
                TotalDistance = StepsTotal,
                MineralsB    = MineralsB,
                MineralsY    = MineralsY,
                MineralsG    = MineralsG,
                EventText    = eventText
            };

            Log.Add(entry);
            CurrentTick++;
            OnTickCompleted?.Invoke(entry);
        }
    }
}
