using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using MarsRover.Engine;
using MarsRover.Models;

namespace MarsRover
{
    public partial class MainWindow : Window
    {
        private MarsMap? _map;
        private Simulator? _sim;
        private DispatcherTimer? _timer;
        private bool _paused;
        private int _totalMin;

        // 3D
        private Model3DGroup _sceneGroup = new();
        private GeometryModel3D? _roverModel;
        private TranslateTransform3D? _roverTransform;
        private readonly Dictionary<Pos, GeometryModel3D> _cellModels = new();
        private readonly List<GeometryModel3D> _trailModels = new();

        // Kamera
        private double _camAngle = 45;   // fok, vízszintes forgatás
        private double _camPitch = 55;   // fok, dőlésszög
        private double _camDist = 55;    // távolság
        private double _camCenterX = 25;
        private double _camCenterZ = 25;

        // Napfény
        private DirectionalLight? _sunLight;

        public MainWindow() { InitializeComponent(); }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadMap();
            SldSpeed.ValueChanged += (_, _) =>
            {
                int ms = (int)SldSpeed.Value;
                TxtTempo.Text = $"{ms}ms";
                if (_timer != null) _timer.Interval = TimeSpan.FromMilliseconds(ms);
            };
        }

        // ══════════════════════════════════════
        //  TÉRKÉP BETÖLTÉS
        // ══════════════════════════════════════

        private void LoadMap()
        {
            string csv = FindCsv();
            try
            {
                _map = MarsMap.Load(csv);
                _totalMin = _map.AllMinerals().Count;
                TxtMinPct.Text = $"0/{_totalMin}";
                TxtStatus.Text = $"// TÉRKÉP: {_map.W}x{_map.H} | START: {_map.StartPos} | ÁSVÁNYOK: {_totalMin}";
                Build3DScene();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hiba:\n{ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string FindCsv()
        {
            string[] ps = {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mars_map_50x50.csv"),
                Path.Combine(Directory.GetCurrentDirectory(), "mars_map_50x50.csv"),
                "mars_map_50x50.csv"
            };
            foreach (var p in ps) if (File.Exists(p)) return p;
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "CSV|*.csv" };
            if (dlg.ShowDialog() == true) return dlg.FileName;
            throw new FileNotFoundException("CSV nem található");
        }

        // ══════════════════════════════════════
        //  3D JELENET ÉPÍTÉS
        // ══════════════════════════════════════

        private void Build3DScene()
        {
            if (_map == null) return;

            _sceneGroup = new Model3DGroup();
            _cellModels.Clear();
            _trailModels.Clear();

            // Fények
            _sunLight = new DirectionalLight(
                Color.FromRgb(0xFF, 0xEE, 0xDD),
                new Vector3D(-0.5, -1, -0.3));
            _sceneGroup.Children.Add(_sunLight);
            _sceneGroup.Children.Add(new AmbientLight(Color.FromRgb(0x33, 0x28, 0x30)));

            // Alaplap (talaj)
            var floorMat = MakeMat(Color.FromRgb(0x12, 0x0E, 0x16));
            _sceneGroup.Children.Add(MakeBox(
                _map.W / 2.0, -0.15, _map.H / 2.0,
                _map.W + 2, 0.3, _map.H + 2, floorMat));

            // Cellák
            for (int r = 0; r < _map.H; r++)
            for (int c = 0; c < _map.W; c++)
            {
                var ct = _map.Grid[r, c];
                double h;
                Color col;

                switch (ct)
                {
                    case CellType.Wall:
                        h = 0.6 + 0.4 * ((r * 7 + c * 13) % 10) / 10.0; // változó magasság
                        col = Color.FromRgb(0x50, 0x40, 0x55);
                        break;
                    case CellType.MineralB:
                        h = 0.25;
                        col = Color.FromRgb(0x38, 0x8B, 0xE8);
                        break;
                    case CellType.MineralY:
                        h = 0.25;
                        col = Color.FromRgb(0xE8, 0xC0, 0x20);
                        break;
                    case CellType.MineralG:
                        h = 0.25;
                        col = Color.FromRgb(0x30, 0xC0, 0x60);
                        break;
                    case CellType.Start:
                        h = 0.1;
                        col = Color.FromRgb(0xFF, 0x6B, 0x35);
                        break;
                    default:
                        h = 0.05;
                        col = Color.FromRgb(0x1E, 0x18, 0x24);
                        break;
                }

                var mat = MakeMat(col);
                double x = c;
                double z = r;
                var model = MakeBox(x, h / 2, z, 0.9, h, 0.9, mat);
                _sceneGroup.Children.Add(model);
                _cellModels[new Pos(r, c)] = model;
            }

            // Start jelölő – magasabb narancssárga oszlop
            var startMat = MakeEmissiveMat(Color.FromRgb(0xFF, 0x6B, 0x35));
            _sceneGroup.Children.Add(MakeBox(
                _map.StartPos.Col, 0.6, _map.StartPos.Row,
                0.3, 1.2, 0.3, startMat));

            // Rover – kocka, piros-narancs, emissive
            var roverMat = MakeEmissiveMat(Color.FromRgb(0xFF, 0x50, 0x20));
            _roverTransform = new TranslateTransform3D(
                _map.StartPos.Col, 0.4, _map.StartPos.Row);
            _roverModel = MakeBox(0, 0, 0, 0.7, 0.5, 0.7, roverMat);
            _roverModel.Transform = _roverTransform;
            _sceneGroup.Children.Add(_roverModel);

            // Jelenet hozzárendelése
            SceneRoot.Content = _sceneGroup;

            // Kamera beállítás
            _camCenterX = _map.W / 2.0;
            _camCenterZ = _map.H / 2.0;
            _camDist = Math.Max(_map.W, _map.H) * 1.1;
            UpdateCamera();
        }

        // ══════════════════════════════════════
        //  3D SEGÉDFÜGGVÉNYEK
        // ══════════════════════════════════════

        private static GeometryModel3D MakeBox(double cx, double cy, double cz,
            double sx, double sy, double sz, Material mat)
        {
            double hx = sx / 2, hy = sy / 2, hz = sz / 2;
            var mesh = new MeshGeometry3D();

            // 8 csúcspont
            Point3D[] pts = {
                new(cx-hx, cy-hy, cz-hz), new(cx+hx, cy-hy, cz-hz),
                new(cx+hx, cy+hy, cz-hz), new(cx-hx, cy+hy, cz-hz),
                new(cx-hx, cy-hy, cz+hz), new(cx+hx, cy-hy, cz+hz),
                new(cx+hx, cy+hy, cz+hz), new(cx-hx, cy+hy, cz+hz)
            };
            foreach (var p in pts) mesh.Positions.Add(p);

            // 6 lap = 12 háromszög
            int[] idx = {
                0,2,1, 0,3,2, // front
                4,5,6, 4,6,7, // back
                0,1,5, 0,5,4, // bottom
                2,3,7, 2,7,6, // top
                0,4,7, 0,7,3, // left
                1,2,6, 1,6,5  // right
            };
            foreach (var i in idx) mesh.TriangleIndices.Add(i);

            return new GeometryModel3D(mesh, mat) { BackMaterial = mat };
        }

        private static DiffuseMaterial MakeMat(Color c) =>
            new(new SolidColorBrush(c));

        private static MaterialGroup MakeEmissiveMat(Color c)
        {
            var mg = new MaterialGroup();
            mg.Children.Add(new DiffuseMaterial(new SolidColorBrush(c)));
            mg.Children.Add(new EmissiveMaterial(new SolidColorBrush(
                Color.FromArgb(0x88, c.R, c.G, c.B))));
            return mg;
        }

        // ══════════════════════════════════════
        //  KAMERA VEZÉRLÉS
        // ══════════════════════════════════════

        private void UpdateCamera()
        {
            double radH = _camAngle * Math.PI / 180;
            double radV = _camPitch * Math.PI / 180;

            double y = Math.Sin(radV) * _camDist;
            double r = Math.Cos(radV) * _camDist;
            double x = _camCenterX + Math.Cos(radH) * r;
            double z = _camCenterZ + Math.Sin(radH) * r;

            Camera.Position = new Point3D(x, y, z);
            Camera.LookDirection = new Vector3D(
                _camCenterX - x, -y * 0.7, _camCenterZ - z);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            double moveSpd = 2;
            switch (e.Key)
            {
                case Key.Q: _camAngle -= 5; break;
                case Key.E: _camAngle += 5; break;
                case Key.W: _camCenterZ -= moveSpd; break;
                case Key.S: _camCenterZ += moveSpd; break;
                case Key.A: _camCenterX -= moveSpd; break;
                case Key.D: _camCenterX += moveSpd; break;
                case Key.OemPlus:
                case Key.Add:
                    _camDist = Math.Max(15, _camDist - 3); break;
                case Key.OemMinus:
                case Key.Subtract:
                    _camDist += 3; break;
                case Key.R: _camPitch = Math.Min(85, _camPitch + 3); break;
                case Key.F: _camPitch = Math.Max(15, _camPitch - 3); break;
                default: return;
            }
            UpdateCamera();
        }

        // ══════════════════════════════════════
        //  SZIMULÁCIÓ
        // ══════════════════════════════════════

        private void BtnGo_Click(object sender, RoutedEventArgs e)
        {
            if (_map == null) return;
            if (!int.TryParse(TxtHours.Text, out int h) || h < 24)
            {
                MessageBox.Show("Minimum 24 óra!", "Hiba"); return;
            }

            BtnGo.IsEnabled = false;
            BtnPause.IsEnabled = true;
            BtnCsv.IsEnabled = false;
            BtnReset.IsEnabled = false;
            TxtHours.IsEnabled = false;
            _paused = false;

            _sim = new Simulator(_map, h);
            TxtStatus.Text = "// AI ÚTVONALTERVEZÉS...";

            Task.Run(() =>
            {
                _sim.PlanInitial();
                Dispatcher.Invoke(() =>
                {
                    TxtStatus.Text = $"// TERVEZETT CÉLOK: {_sim.PlannedRoute.Count} | SZIMULÁCIÓ INDÍTVA";
                    DrawPlannedTargets();
                    StartSim();
                });
            });
        }

        private void DrawPlannedTargets()
        {
            if (_sim == null) return;
            var mat = MakeEmissiveMat(Color.FromArgb(0x66, 0xFF, 0x6B, 0x35));
            foreach (var p in _sim.PlannedRoute)
            {
                var marker = MakeBox(p.Col, 0.5, p.Row, 0.4, 0.05, 0.4, mat);
                _sceneGroup.Children.Add(marker);
            }
        }

        private void StartSim()
        {
            if (_sim == null) return;
            _sim.OnDone += () => Dispatcher.Invoke(Done);
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SldSpeed.Value) };
            _timer.Tick += (_, _) => DoTick();
            _timer.Start();
        }

        private void DoTick()
        {
            if (_sim == null || _sim.Finished || _paused) return;

            _sim.Step();
            var l = _sim.Log[_sim.Log.Count - 1];

            // Rover mozgatás
            if (_roverTransform != null)
            {
                _roverTransform.OffsetX = l.Position.Col;
                _roverTransform.OffsetZ = l.Position.Row;
            }

            // Nyomvonal
            var trailMat = MakeEmissiveMat(Color.FromArgb(0x44, 0xFF, 0x6B, 0x35));
            var trail = MakeBox(l.Position.Col, 0.02, l.Position.Row, 0.3, 0.04, 0.3, trailMat);
            _sceneGroup.Children.Add(trail);

            // Begyűjtött ásványok -> lenyomott szürke
            foreach (var m in _sim.Collected)
            {
                if (_cellModels.TryGetValue(m, out var cm))
                {
                    cm.Material = MakeMat(Color.FromRgb(0x14, 0x10, 0x18));
                    cm.BackMaterial = cm.Material;
                }
            }

            // Napszak
            bool night = l.Phase == DayPhase.Night;
            NightOverlay.Visibility = night ? Visibility.Visible : Visibility.Collapsed;
            if (_sunLight != null)
            {
                _sunLight.Color = night
                    ? Color.FromRgb(0x30, 0x30, 0x60)
                    : Color.FromRgb(0xFF, 0xEE, 0xDD);
            }

            TxtPhase.Text = night ? "ÉJSZAKA" : "NAPPAL";
            TxtPhase.Foreground = new SolidColorBrush(night
                ? Color.FromRgb(0x60, 0xA5, 0xFA)
                : Color.FromRgb(0xFA, 0xCC, 0x15));
            PhaseOrb.Fill = new SolidColorBrush(night
                ? Color.FromRgb(0x60, 0xA5, 0xFA)
                : Color.FromRgb(0xFA, 0xCC, 0x15));

            UpdateDash(l);
            AddLog(l);

            if (_sim.Finished) { _timer?.Stop(); Done(); }
        }

        private void UpdateDash(LogEntry l)
        {
            TxtBatt.Text = $"{l.Battery:F0}";
            TxtBatt.Foreground = new SolidColorBrush(l.Battery switch
            {
                > 50 => Color.FromRgb(0x4A, 0xDE, 0x80),
                > 25 => Color.FromRgb(0xFA, 0xCC, 0x15),
                _ => Color.FromRgb(0xEF, 0x44, 0x44)
            });

            BarBatt.Width = Math.Max(0, l.Battery / 100.0 * 180);
            BarBatt.Background = TxtBatt.Foreground.Clone();

            string s = l.EnergyDelta >= 0 ? "+" : "";
            TxtDelta.Text = $"{s}{l.EnergyDelta:F0}";
            TxtDelta.Foreground = new SolidColorBrush(l.EnergyDelta >= 0
                ? Color.FromRgb(0x4A, 0xDE, 0x80) : Color.FromRgb(0xEF, 0x44, 0x44));

            TxtBattInfo.Text = $"Töltés:{(l.Phase == DayPhase.Day ? "+10" : "0")} Fogyasztás:-{Simulator.K * (int)l.Speed * (int)l.Speed:F0}";

            TxtMinTotal.Text = l.TotalMinerals.ToString();
            TxtMinB.Text = $"K:{l.MineralsB}";
            TxtMinY.Text = $"S:{l.MineralsY}";
            TxtMinG.Text = $"Z:{l.MineralsG}";
            double pct = _totalMin > 0 ? (double)l.TotalMinerals / _totalMin : 0;
            BarMin.Width = pct * 180;
            TxtMinPct.Text = $"{l.TotalMinerals}/{_totalMin} ({pct:P0})";

            TxtPos.Text = $"({l.Position.Row},{l.Position.Col})";
            TxtSpd.Text = l.Speed switch
            {
                SpeedLevel.Slow => "LASSÚ",
                SpeedLevel.Normal => "NORM",
                SpeedLevel.Fast => "GYORS",
                _ => "?"
            };
            TxtAct.Text = l.Action switch
            {
                RoverAction.Moving => "MOZGÁS",
                RoverAction.Mining => "BÁNYÁSZ",
                RoverAction.Standby => "STAND",
                RoverAction.Returning => "HAZA",
                RoverAction.WaitingForDawn => "VÁRÁS",
                _ => "?"
            };
            TxtAct.Foreground = new SolidColorBrush(l.Action switch
            {
                RoverAction.Mining => Color.FromRgb(0x4A, 0xDE, 0x80),
                RoverAction.Returning => Color.FromRgb(0xFA, 0xCC, 0x15),
                RoverAction.WaitingForDawn => Color.FromRgb(0x60, 0xA5, 0xFA),
                _ => Color.FromRgb(0xFF, 0x6B, 0x35)
            });

            TxtDist.Text = l.TotalDistance.ToString();
            TxtSol.Text = ((l.Tick / Simulator.CYCLE_TICKS) + 1).ToString();
            TxtPhChg.Text = $"{_sim!.TicksToPhaseChange * 0.5:F1}h";
            TxtClock.Text = $"  T+{l.SimHours:F1}h / {_sim.TotalHours}h";
        }

        private void AddLog(LogEntry l)
        {
            if (string.IsNullOrWhiteSpace(l.Event)) return;
            string ph = l.Phase == DayPhase.Day ? "D" : "N";
            LogBox.Items.Add($"[{l.SimHours:F1}h|{ph}] {l.Event}");
            if (LogBox.Items.Count > 0)
                LogBox.ScrollIntoView(LogBox.Items[LogBox.Items.Count - 1]);
        }

        private void Done()
        {
            _timer?.Stop();
            BtnPause.IsEnabled = false;
            BtnCsv.IsEnabled = true;
            BtnReset.IsEnabled = true;
            TxtStatus.Text = _sim!.Home
                ? $"// KÜLDETÉS SIKERES! {_sim.TotalMin} ásvány | Sol {(_sim.Tick / Simulator.CYCLE_TICKS) + 1}"
                : $"// IDŐ LEJÁRT. {_sim.TotalMin} ásvány.";
        }

        // ══════════════════════════════════════
        //  GOMBOK
        // ══════════════════════════════════════

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            _paused = !_paused;
            BtnPause.Content = _paused ? ">" : "||";
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();
            _sim = null;
            _paused = false;
            BtnGo.IsEnabled = true;
            BtnPause.IsEnabled = false;
            BtnPause.Content = "||";
            BtnCsv.IsEnabled = false;
            BtnReset.IsEnabled = true;
            TxtHours.IsEnabled = true;
            LogBox.Items.Clear();
            Build3DScene();
            TxtStatus.Text = "// ÚJRAINDÍTVA. KÉSZ.";
            TxtBatt.Text = "100"; TxtMinTotal.Text = "0";
            TxtMinB.Text = "K:0"; TxtMinY.Text = "S:0"; TxtMinG.Text = "Z:0";
            BarMin.Width = 0; TxtMinPct.Text = $"0/{_totalMin}";
            TxtPos.Text = _map != null ? $"({_map.StartPos.Row},{_map.StartPos.Col})" : "-";
            TxtSpd.Text = "LASSÚ"; TxtAct.Text = "STANDBY"; TxtDist.Text = "0";
            TxtSol.Text = "1"; TxtPhChg.Text = "16.0h"; TxtClock.Text = "T+0.0h";
        }

        private void BtnCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_sim == null) return;
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV|*.csv", FileName = "mars_rover_log.csv" };
            if (dlg.ShowDialog() == true)
            {
                using var sw = new StreamWriter(dlg.FileName, false, System.Text.Encoding.UTF8);
                sw.WriteLine("Tick;Óra;Sor;Oszlop;Akku;Spd;Akció;Fázis;Dist;B;Y;G;Össz;Delta;Esemény");
                foreach (var l in _sim.Log)
                    sw.WriteLine($"{l.Tick};{l.SimHours:F1};{l.Position.Row};{l.Position.Col};{l.Battery:F1};{(int)l.Speed};{l.Action};{l.Phase};{l.TotalDistance};{l.MineralsB};{l.MineralsY};{l.MineralsG};{l.TotalMinerals};{l.EnergyDelta:F1};{l.Event}");
                TxtStatus.Text = $"// NAPLÓ: {dlg.FileName}";
            }
        }
    }
}
