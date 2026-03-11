using System.IO;

namespace MarsRover.Models
{
    public enum CellType { Empty, Wall, MineralB, MineralY, MineralG, Start }
    public enum DayPhase { Day, Night }
    public enum SpeedLevel { Slow = 1, Normal = 2, Fast = 3 }
    public enum RoverAction { Moving, Mining, Standby, Returning, WaitingForDawn }

    public readonly struct Pos : IEquatable<Pos>
    {
        public int Row { get; }
        public int Col { get; }
        public Pos(int row, int col) { Row = row; Col = col; }
        public bool Equals(Pos other) => Row == other.Row && Col == other.Col;
        public override bool Equals(object? obj) => obj is Pos p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(Row, Col);
        public override string ToString() => $"({Row},{Col})";
        public static bool operator ==(Pos a, Pos b) => a.Equals(b);
        public static bool operator !=(Pos a, Pos b) => !a.Equals(b);
        public int ChebyshevTo(Pos o) => Math.Max(Math.Abs(Row - o.Row), Math.Abs(Col - o.Col));
    }

    public class LogEntry
    {
        public int Tick { get; set; }
        public double SimHours { get; set; }
        public Pos Position { get; set; }
        public double Battery { get; set; }
        public SpeedLevel Speed { get; set; }
        public RoverAction Action { get; set; }
        public DayPhase Phase { get; set; }
        public int TotalDistance { get; set; }
        public int MineralsB { get; set; }
        public int MineralsY { get; set; }
        public int MineralsG { get; set; }
        public int TotalMinerals => MineralsB + MineralsY + MineralsG;
        public string Event { get; set; } = "";
        public double EnergyDelta { get; set; }
    }

    public class MarsMap
    {
        public int W { get; }
        public int H { get; }
        public CellType[,] Grid { get; }
        public Pos StartPos { get; }

        private MarsMap(CellType[,] g, Pos s) { Grid = g; H = g.GetLength(0); W = g.GetLength(1); StartPos = s; }

        public bool InBounds(Pos p) => p.Row >= 0 && p.Row < H && p.Col >= 0 && p.Col < W;
        public bool Walkable(Pos p) => InBounds(p) && Grid[p.Row, p.Col] != CellType.Wall;
        public bool IsMineral(Pos p) => InBounds(p) && Grid[p.Row, p.Col] is CellType.MineralB or CellType.MineralY or CellType.MineralG;

        public IEnumerable<Pos> Neighbors(Pos p)
        {
            for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                var n = new Pos(p.Row + dr, p.Col + dc);
                if (Walkable(n)) yield return n;
            }
        }

        public List<Pos> AllMinerals()
        {
            var list = new List<Pos>();
            for (int r = 0; r < H; r++)
            for (int c = 0; c < W; c++)
                if (Grid[r, c] is CellType.MineralB or CellType.MineralY or CellType.MineralG)
                    list.Add(new Pos(r, c));
            return list;
        }

        public static MarsMap Load(string path)
        {
            var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            int rows = lines.Length, cols = lines[0].Split(',').Length;
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
                        "#" => CellType.Wall, "B" => CellType.MineralB,
                        "Y" => CellType.MineralY, "G" => CellType.MineralG,
                        "S" => CellType.Start, _ => CellType.Empty
                    };
                    if (ch == "S") start = new Pos(r, c);
                }
            }
            return new MarsMap(grid, start);
        }
    }
}
