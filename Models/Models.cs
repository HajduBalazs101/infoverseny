using System.IO;

namespace MarsRover.Models
{
    /// <summary>A térkép egy cellájának típusa.</summary>
    public enum CellType
    {
        Empty,      // '.' – átjárható marsi felszín
        Wall,       // '#' – akadály
        MineralB,   // 'B' – kék ásvány (vízjég)
        MineralY,   // 'Y' – sárga ásvány (ritka arany)
        MineralG,   // 'G' – zöld ásvány (ritka)
        Start       // 'S' – kiindulópont
    }

    /// <summary>Napszak a Marson.</summary>
    public enum DayPhase
    {
        Day,    // nappal – 16 óra (32 fél-óra tick)
        Night   // éjszaka – 8 óra (16 fél-óra tick)
    }

    /// <summary>Rover sebességi fokozat.</summary>
    public enum SpeedLevel
    {
        Slow   = 1,  // 1 blokk / fél óra  → E = 2*1² = 2
        Normal = 2,  // 2 blokk / fél óra  → E = 2*2² = 8
        Fast   = 3   // 3 blokk / fél óra  → E = 2*3² = 18
    }

    /// <summary>Rover aktuális tevékenysége.</summary>
    public enum RoverAction
    {
        Moving,
        Mining,
        Standby,
        Returning
    }

    /// <summary>Koordináta a térképen (sor, oszlop).</summary>
    public readonly struct Pos : IEquatable<Pos>
    {
        public int Row { get; }
        public int Col { get; }

        public Pos(int row, int col)
        {
            Row = row;
            Col = col;
        }

        public bool Equals(Pos other) => Row == other.Row && Col == other.Col;
        public override bool Equals(object? obj) => obj is Pos p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(Row, Col);
        public override string ToString() => $"({Row},{Col})";

        public static bool operator ==(Pos a, Pos b) => a.Equals(b);
        public static bool operator !=(Pos a, Pos b) => !a.Equals(b);

        /// <summary>Chebyshev-távolság (átlós mozgás megengedett).</summary>
        public int DistanceTo(Pos other)
            => Math.Max(Math.Abs(Row - other.Row), Math.Abs(Col - other.Col));
    }

    /// <summary>Fél-óránkénti napló bejegyzés.</summary>
    public class LogEntry
    {
        public int    Tick           { get; set; } // fél-óra sorszám (0-tól)
        public double SimHours       { get; set; } // eltelt idő órában
        public Pos    Position       { get; set; }
        public double Battery        { get; set; }
        public SpeedLevel Speed      { get; set; }
        public RoverAction Action    { get; set; }
        public DayPhase Phase        { get; set; }
        public int    TotalDistance   { get; set; } // összes megtett blokk
        public int    MineralsB      { get; set; }
        public int    MineralsY      { get; set; }
        public int    MineralsG      { get; set; }
        public int    TotalMinerals  => MineralsB + MineralsY + MineralsG;
        public string EventText      { get; set; } = "";
    }

    /// <summary>Mars térkép betöltése és kezelése.</summary>
    public class MarsMap
    {
        public int Width  { get; }
        public int Height { get; }
        public CellType[,] Grid { get; }
        public Pos StartPos { get; }

        private MarsMap(CellType[,] grid, Pos start)
        {
            Grid   = grid;
            Height = grid.GetLength(0);
            Width  = grid.GetLength(1);
            StartPos = start;
        }

        public bool InBounds(Pos p)
            => p.Row >= 0 && p.Row < Height && p.Col >= 0 && p.Col < Width;

        public bool IsWalkable(Pos p)
            => InBounds(p) && Grid[p.Row, p.Col] != CellType.Wall;

        public bool IsMineral(Pos p)
            => InBounds(p) && Grid[p.Row, p.Col] is CellType.MineralB
                                                  or CellType.MineralY
                                                  or CellType.MineralG;

        /// <summary>A 8 szomszéd (átlós is) közül az átjárhatóak.</summary>
        public IEnumerable<Pos> GetNeighbors(Pos p)
        {
            for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                var n = new Pos(p.Row + dr, p.Col + dc);
                if (IsWalkable(n)) yield return n;
            }
        }

        /// <summary>Összes ásványmező listája.</summary>
        public List<Pos> GetAllMinerals()
        {
            var list = new List<Pos>();
            for (int r = 0; r < Height; r++)
            for (int c = 0; c < Width; c++)
            {
                var ct = Grid[r, c];
                if (ct is CellType.MineralB or CellType.MineralY or CellType.MineralG)
                    list.Add(new Pos(r, c));
            }
            return list;
        }

        /// <summary>CSV fájl betöltése.</summary>
        public static MarsMap LoadFromCsv(string path)
        {
            var lines = File.ReadAllLines(path)
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .ToArray();

            int rows = lines.Length;
            int cols = lines[0].Split(',').Length;
            var grid = new CellType[rows, cols];
            Pos start = default;

            for (int r = 0; r < rows; r++)
            {
                var cells = lines[r].Split(',');
                for (int c = 0; c < cols; c++)
                {
                    var ch = cells[c].Trim();
                    grid[r, c] = ch switch
                    {
                        "."  => CellType.Empty,
                        "#"  => CellType.Wall,
                        "B"  => CellType.MineralB,
                        "Y"  => CellType.MineralY,
                        "G"  => CellType.MineralG,
                        "S"  => CellType.Start,
                        _    => CellType.Empty
                    };
                    if (ch == "S") start = new Pos(r, c);
                }
            }
            return new MarsMap(grid, start);
        }
    }
}
