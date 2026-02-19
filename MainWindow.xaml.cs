using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using MarsRover.Engine;
using MarsRover.Models;
using SkiaSharp;

namespace MarsRover
{
    public partial class MainWindow : Window
    {
        // ── Térkép és szimuláció ──
        private MarsMap? _map;
        private Simulator? _sim;
        private DispatcherTimer? _timer;
        private bool _isPaused = false;

        // ── Térkép rajzolás ──
        private const int CELL_SIZE = 12;
        private readonly Dictionary<Pos, System.Windows.Shapes.Rectangle> _cellRects = new();
        private Ellipse? _roverDot;
        private readonly List<Ellipse> _pathDots = new();

        // ── Grafikon adatok ──
        private readonly List<ObservablePoint> _batteryData  = new();
        private readonly List<ObservablePoint> _mineralBData = new();
        private readonly List<ObservablePoint> _mineralYData = new();
        private readonly List<ObservablePoint> _mineralGData = new();
        private readonly List<ObservablePoint> _speedData    = new();

        // ── Ásvány összszám a térképen ──
        private int _totalMapMinerals = 0;

        public MainWindow()
        {
            InitializeComponent();
        }

        // ════════════════════════════════════════════
        //  INICIALIZÁCIÓ
        // ════════════════════════════════════════════

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Térkép betöltése
            string csvPath = FindCsvPath();
            try
            {
                _map = MarsMap.LoadFromCsv(csvPath);
                _totalMapMinerals = _map.GetAllMinerals().Count;
                TxtMineralPercent.Text = $"0 / {_totalMapMinerals} ásvány (0%)";
                TxtStatus.Text = $"✅ Térkép betöltve: {_map.Width}×{_map.Height} | Start: {_map.StartPos} | Ásványok: {_totalMapMinerals}";
                DrawMap();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hiba a térkép betöltésekor:\n{ex.Message}\n\nKeresett útvonal: {csvPath}",
                    "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // Animáció sebesség slider
            SpeedSlider.ValueChanged += (_, _) =>
            {
                int ms = (int)SpeedSlider.Value;
                TxtAnimSpeed.Text = $"{ms}ms";
                if (_timer != null) _timer.Interval = TimeSpan.FromMilliseconds(ms);
            };

            // Grafikon inicializáció
            InitCharts();
        }

        private string FindCsvPath()
        {
            // Keresési sorrend
            string[] candidates = {
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mars_map_50x50.csv"),
                System.IO.Path.Combine(Directory.GetCurrentDirectory(), "mars_map_50x50.csv"),
                "mars_map_50x50.csv"
            };
            foreach (var p in candidates)
                if (File.Exists(p)) return p;

            // Ha nincs mellette, prompt
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "CSV files|*.csv",
                Title  = "Mars térkép CSV kiválasztása"
            };
            if (dlg.ShowDialog() == true) return dlg.FileName;
            throw new FileNotFoundException("Nem található a mars_map_50x50.csv fájl.");
        }

        // ════════════════════════════════════════════
        //  TÉRKÉP RAJZOLÁS
        // ════════════════════════════════════════════

        private void DrawMap()
        {
            if (_map == null) return;
            MapCanvas.Children.Clear();
            _cellRects.Clear();

            for (int r = 0; r < _map.Height; r++)
            for (int c = 0; c < _map.Width; c++)
            {
                var cell = _map.Grid[r, c];
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width  = CELL_SIZE - 1,
                    Height = CELL_SIZE - 1,
                    Fill   = GetCellBrush(cell),
                    RadiusX = 1,
                    RadiusY = 1
                };
                Canvas.SetLeft(rect, c * CELL_SIZE);
                Canvas.SetTop(rect, r * CELL_SIZE);
                MapCanvas.Children.Add(rect);

                var pos = new Pos(r, c);
                _cellRects[pos] = rect;
            }

            // Start pont jelölése
            DrawStartMarker();

            // Rover pont
            _roverDot = new Ellipse
            {
                Width  = CELL_SIZE + 4,
                Height = CELL_SIZE + 4,
                Fill   = new SolidColorBrush(Color.FromRgb(0xE8, 0x63, 0x2B)),
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 2
            };
            Canvas.SetLeft(_roverDot, _map.StartPos.Col * CELL_SIZE - 2);
            Canvas.SetTop(_roverDot, _map.StartPos.Row * CELL_SIZE - 2);
            Canvas.SetZIndex(_roverDot, 100);
            MapCanvas.Children.Add(_roverDot);
        }

        private void DrawStartMarker()
        {
            if (_map == null) return;
            var marker = new Ellipse
            {
                Width  = CELL_SIZE + 8,
                Height = CELL_SIZE + 8,
                Stroke = new SolidColorBrush(Color.FromRgb(0xE8, 0x63, 0x2B)),
                StrokeThickness = 2,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(marker, _map.StartPos.Col * CELL_SIZE - 4);
            Canvas.SetTop(marker, _map.StartPos.Row * CELL_SIZE - 4);
            Canvas.SetZIndex(marker, 50);
            MapCanvas.Children.Add(marker);
        }

        private static SolidColorBrush GetCellBrush(CellType cell) => cell switch
        {
            CellType.Empty    => new SolidColorBrush(Color.FromRgb(0x2A, 0x1A, 0x22)), // sötét marsi talaj
            CellType.Wall     => new SolidColorBrush(Color.FromRgb(0x55, 0x45, 0x50)), // szürke szikla
            CellType.MineralB => new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA)), // kék
            CellType.MineralY => new SolidColorBrush(Color.FromRgb(0xFA, 0xCC, 0x15)), // sárga
            CellType.MineralG => new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)), // zöld
            CellType.Start    => new SolidColorBrush(Color.FromRgb(0xE8, 0x63, 0x2B)), // narancssárga
            _                 => Brushes.Black
        };

        private void UpdateRoverPosition(Pos pos)
        {
            if (_roverDot == null) return;
            Canvas.SetLeft(_roverDot, pos.Col * CELL_SIZE - 2);
            Canvas.SetTop(_roverDot, pos.Row * CELL_SIZE - 2);

            // Útvonal nyomvonal pont
            var dot = new Ellipse
            {
                Width  = 3,
                Height = 3,
                Fill   = new SolidColorBrush(Color.FromArgb(0x88, 0xE8, 0x63, 0x2B))
            };
            Canvas.SetLeft(dot, pos.Col * CELL_SIZE + CELL_SIZE / 2 - 1);
            Canvas.SetTop(dot, pos.Row * CELL_SIZE + CELL_SIZE / 2 - 1);
            Canvas.SetZIndex(dot, 40);
            MapCanvas.Children.Add(dot);
            _pathDots.Add(dot);
        }

        private void MarkMineralCollected(Pos pos)
        {
            if (_cellRects.TryGetValue(pos, out var rect))
            {
                rect.Fill = new SolidColorBrush(Color.FromRgb(0x1A, 0x11, 0x18)); // elsötétítés
                rect.Stroke = new SolidColorBrush(Color.FromArgb(0x66, 0x4A, 0xDE, 0x80));
                rect.StrokeThickness = 1;
            }
        }

        // ════════════════════════════════════════════
        //  GRAFIKONOK
        // ════════════════════════════════════════════

        private void InitCharts()
        {
            // Akkumulátor grafikon
            ChartBattery.Series = new ISeries[]
            {
                new LineSeries<ObservablePoint>
                {
                    Values = _batteryData,
                    Stroke = new SolidColorPaint(SKColors.LimeGreen, 2),
                    Fill   = new SolidColorPaint(new SKColor(0x4A, 0xDE, 0x80, 0x33)),
                    GeometrySize = 0,
                    LineSmoothness = 0.3
                }
            };
            ChartBattery.XAxes = new[] { MakeAxis("Idő (óra)") };
            ChartBattery.YAxes = new[] { MakeAxis("", 0, 100) };

            // Ásvány grafikon
            ChartMinerals.Series = new ISeries[]
            {
                new LineSeries<ObservablePoint>
                {
                    Values = _mineralBData, Name = "Kék",
                    Stroke = new SolidColorPaint(SKColors.CornflowerBlue, 2),
                    Fill = null, GeometrySize = 0
                },
                new LineSeries<ObservablePoint>
                {
                    Values = _mineralYData, Name = "Sárga",
                    Stroke = new SolidColorPaint(SKColors.Gold, 2),
                    Fill = null, GeometrySize = 0
                },
                new LineSeries<ObservablePoint>
                {
                    Values = _mineralGData, Name = "Zöld",
                    Stroke = new SolidColorPaint(SKColors.LimeGreen, 2),
                    Fill = null, GeometrySize = 0
                }
            };
            ChartMinerals.XAxes = new[] { MakeAxis("Idő (óra)") };
            ChartMinerals.YAxes = new[] { MakeAxis("") };

            // Sebesség grafikon
            ChartSpeed.Series = new ISeries[]
            {
                new StepLineSeries<ObservablePoint>
                {
                    Values = _speedData, Name = "Sebesség",
                    Stroke = new SolidColorPaint(new SKColor(0xE8, 0x63, 0x2B), 2),
                    Fill   = new SolidColorPaint(new SKColor(0xE8, 0x63, 0x2B, 0x33)),
                    GeometrySize = 0
                }
            };
            ChartSpeed.XAxes = new[] { MakeAxis("Idő (óra)") };
            ChartSpeed.YAxes = new[] { MakeAxis("blokk/tick", 0, 4) };
        }

        private static Axis MakeAxis(string title, double? min = null, double? max = null) => new()
        {
            Name = title,
            NameTextSize = 10,
            TextSize = 9,
            LabelsPaint   = new SolidColorPaint(new SKColor(0x8A, 0x7E, 0x85)),
            NamePaint      = new SolidColorPaint(new SKColor(0x8A, 0x7E, 0x85)),
            SeparatorsPaint = new SolidColorPaint(new SKColor(0x3A, 0x2A, 0x35, 0x66)),
            MinLimit = min,
            MaxLimit = max
        };

        // ════════════════════════════════════════════
        //  SZIMULÁCIÓ VEZÉRLÉS
        // ════════════════════════════════════════════

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_map == null) return;

            if (!int.TryParse(TxtHours.Text, out int hours) || hours < 24)
            {
                MessageBox.Show("Az időkeret legalább 24 óra kell legyen.", "Figyelmeztetés",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnStart.IsEnabled    = false;
            BtnPause.IsEnabled    = true;
            BtnExportLog.IsEnabled = false;
            TxtHours.IsEnabled    = false;

            // Szimuláció inicializálás
            _sim = new Simulator(_map, hours);
            TxtStatus.Text = "🧠 Útvonal tervezése (A* + Greedy TSP + 2-opt)...";

            // Háttérszálon tervezés, UI-n szimuláció
            Task.Run(() =>
            {
                _sim.PlanRoute();
                Dispatcher.Invoke(() =>
                {
                    TxtStatus.Text = $"✅ Útvonal megtervezve: {_sim.PlannedRoute.Count} ásvány célpont | Indulás...";
                    DrawPlannedRoute();
                    StartSimulation();
                });
            });
        }

        private void DrawPlannedRoute()
        {
            if (_sim == null || _map == null) return;

            // Tervezett célpontok megjelölése a térképen
            for (int i = 0; i < _sim.PlannedRoute.Count; i++)
            {
                var pos = _sim.PlannedRoute[i];
                var marker = new Border
                {
                    Width  = CELL_SIZE + 2,
                    Height = CELL_SIZE + 2,
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 0xE8, 0x63, 0x2B)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(2)
                };
                Canvas.SetLeft(marker, pos.Col * CELL_SIZE - 1);
                Canvas.SetTop(marker, pos.Row * CELL_SIZE - 1);
                Canvas.SetZIndex(marker, 30);
                MapCanvas.Children.Add(marker);
            }
        }

        private void StartSimulation()
        {
            if (_sim == null) return;

            _sim.OnSimulationFinished += () => Dispatcher.Invoke(OnSimFinished);

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(SpeedSlider.Value)
            };
            _timer.Tick += (_, _) => SimStep();
            _timer.Start();
        }

        private void SimStep()
        {
            if (_sim == null || _sim.IsFinished || _isPaused) return;

            _sim.SimulateTick();
            var log = _sim.Log[_sim.Log.Count - 1]; // utolsó bejegyzés

            // ── UI frissítés ──
            UpdateRoverPosition(log.Position);
            UpdateDashboard(log);
            UpdateCharts(log);
            UpdateLog(log);
            UpdateNightOverlay(log.Phase);

            // Begyűjtött ásványok megjelölése
            foreach (var m in _sim.CollectedMinerals)
                MarkMineralCollected(m);

            if (_sim.IsFinished)
            {
                _timer?.Stop();
                OnSimFinished();
            }
        }

        private void UpdateDashboard(LogEntry log)
        {
            // Akkumulátor
            TxtBattery.Text = $"{log.Battery:F1}";
            double battPct = log.Battery / 100.0;
            BatteryBar.Width = Math.Max(0, battPct * (BatteryBar.Parent as Border)!.ActualWidth * 0.95);
            BatteryBar.Background = log.Battery switch
            {
                > 50 => new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)),
                > 25 => new SolidColorBrush(Color.FromRgb(0xFA, 0xCC, 0x15)),
                _    => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
            };

            string chargeStr = log.Phase == DayPhase.Day ? "+10/tick" : "0 (éjszaka)";
            TxtBatteryInfo.Text = $"Töltés: {chargeStr} | Akció: {log.Action}";

            // Ásványok
            TxtMineralTotal.Text = log.TotalMinerals.ToString();
            TxtMineralB.Text     = log.MineralsB.ToString();
            TxtMineralY.Text     = log.MineralsY.ToString();
            TxtMineralG.Text     = log.MineralsG.ToString();

            double minPct = _totalMapMinerals > 0 ? (double)log.TotalMinerals / _totalMapMinerals : 0;
            MineralProgress.Width = Math.Max(0, minPct * 380);
            TxtMineralPercent.Text = $"{log.TotalMinerals} / {_totalMapMinerals} ásvány ({minPct:P0})";

            // Rover info
            TxtPosition.Text = $"({log.Position.Row}, {log.Position.Col})";
            TxtSpeed.Text = log.Speed switch
            {
                SpeedLevel.Slow   => "🐢 Lassú (1 blokk/tick)",
                SpeedLevel.Normal => "🚶 Normál (2 blokk/tick)",
                SpeedLevel.Fast   => "🏃 Gyors (3 blokk/tick)",
                _                 => "?"
            };
            TxtAction.Text = log.Action switch
            {
                RoverAction.Moving    => "🚗 Mozgás",
                RoverAction.Mining    => "⛏ Bányászás",
                RoverAction.Standby   => "⏸ Standby",
                RoverAction.Returning => "🏠 Visszatérés",
                _                     => "?"
            };
            TxtDistance.Text = $"{log.TotalDistance} blokk";

            int dayNum = (log.Tick / Simulator.CYCLE_TICKS) + 1;
            TxtDayCycle.Text = $"{dayNum}. nap";

            int phaseChange = _sim!.TicksUntilPhaseChange;
            TxtPhaseChange.Text = $"{phaseChange * 0.5:F1} óra múlva";

            // Időkijelzés
            TxtTime.Text = $"{log.SimHours:F1}h / {_sim.TotalHours}h";

            // Napszak
            TxtPhase.Text = log.Phase == DayPhase.Day ? "☀ NAPPAL" : "🌙 ÉJSZAKA";
            TxtPhase.Foreground = log.Phase == DayPhase.Day
                ? new SolidColorBrush(Color.FromRgb(0xFA, 0xCC, 0x15))
                : new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA));
        }

        private void UpdateCharts(LogEntry log)
        {
            double h = log.SimHours;
            _batteryData.Add(new ObservablePoint(h, log.Battery));
            _mineralBData.Add(new ObservablePoint(h, log.MineralsB));
            _mineralYData.Add(new ObservablePoint(h, log.MineralsY));
            _mineralGData.Add(new ObservablePoint(h, log.MineralsG));
            _speedData.Add(new ObservablePoint(h, (int)log.Speed));
        }

        private void UpdateLog(LogEntry log)
        {
            if (string.IsNullOrWhiteSpace(log.EventText)) return;

            string prefix = log.Phase == DayPhase.Day ? "☀" : "🌙";
            string line = $"[{log.SimHours:F1}h] {prefix} {log.EventText}";
            LogList.Items.Add(line);

            // Auto-scroll lefelé
            if (LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        }

        private void UpdateNightOverlay(DayPhase phase)
        {
            NightOverlay.Visibility = phase == DayPhase.Night
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnSimFinished()
        {
            _timer?.Stop();
            BtnPause.IsEnabled     = false;
            BtnExportLog.IsEnabled = true;

            string status = _sim!.ReturnedHome
                ? $"✅ Szimuláció kész! Visszatért a bázisra. Összesen {_sim.TotalMinerals} ásvány begyűjtve."
                : $"⏱ Idő lejárt! {_sim.TotalMinerals} ásvány begyűjtve. A rover nem ért vissza.";
            TxtStatus.Text = status;
        }

        // ════════════════════════════════════════════
        //  GOMBOK
        // ════════════════════════════════════════════

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = !_isPaused;
            BtnPause.Content = _isPaused ? "▶" : "⏸";
            TxtStatus.Text = _isPaused ? "⏸ Szüneteltetve" : "▶ Folytatás...";
        }

        private void BtnExportLog_Click(object sender, RoutedEventArgs e)
        {
            if (_sim == null) return;

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV fájl|*.csv",
                FileName = "mars_rover_log.csv"
            };

            if (dlg.ShowDialog() == true)
            {
                ExportLogCsv(dlg.FileName);
                TxtStatus.Text = $"📋 Log exportálva: {dlg.FileName}";
            }
        }

        private void ExportLogCsv(string path)
        {
            if (_sim == null) return;
            using var sw = new StreamWriter(path, false, System.Text.Encoding.UTF8);
            sw.WriteLine("Tick;Óra;Sor;Oszlop;Akkumulátor;Sebesség;Tevékenység;Napszak;Távolság;Kék;Sárga;Zöld;Összesen;Esemény");

            foreach (var log in _sim.Log)
            {
                sw.WriteLine($"{log.Tick};{log.SimHours:F1};{log.Position.Row};{log.Position.Col};" +
                             $"{log.Battery:F1};{(int)log.Speed};{log.Action};{log.Phase};" +
                             $"{log.TotalDistance};{log.MineralsB};{log.MineralsY};{log.MineralsG};" +
                             $"{log.TotalMinerals};{log.EventText}");
            }
        }
    }
}
