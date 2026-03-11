using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MarsRover.Engine;
using MarsRover.Models;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace MarsRover
{
    public partial class MainWindow : Window

        //TESZT
    {
        private MarsMap? _map;
        private Simulator? _sim;
        private DispatcherTimer? _timer;
        private bool _paused;
        private int _totalMin;

        // 2D Camera
        private float _camX, _camY;
        private float _zoom = 10f;
        private bool _dragging;
        private Point _dragStart;
        private float _dragCamX, _dragCamY;

        // Render state
        private readonly HashSet<Pos> _trailSet = new();
        private Pos _roverPos;
        private bool _isNight;

        // SkiaSharp paint cache
        private static readonly SKPaint _paintEmpty = MakeFill(0xFF151515);
        private static readonly SKPaint _paintWall = MakeFill(0xFF383838);
        private static readonly SKPaint _paintWallTop = MakeFill(0xFF484848);
        private static readonly SKPaint _paintMineralB = MakeFill(0xFF2060CC);
        private static readonly SKPaint _paintMineralY = MakeFill(0xFFCC9E00);
        private static readonly SKPaint _paintMineralG = MakeFill(0xFF20A050);
        private static readonly SKPaint _paintStart = MakeFill(0xFF33FF33);
        private static readonly SKPaint _paintRover = MakeFill(0xFFFF2020);
        private static readonly SKPaint _paintRoverGlow = new() { Color = new SKColor(0xFF, 0x20, 0x20, 0x55), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4) };
        private static readonly SKPaint _paintTrail = MakeFill(0x3033FF33);
        private static readonly SKPaint _paintCollected = MakeFill(0xFF0C0C0C);
        private static readonly SKPaint _paintTarget = MakeFill(0x44FFB833);
        private static readonly SKPaint _paintNightOverlay = new() { Color = new SKColor(0, 8, 32, 55) };

        // Glow
        private static readonly SKPaint _glowB = new() { Color = new SKColor(0x20, 0x60, 0xCC, 0x40), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3) };
        private static readonly SKPaint _glowY = new() { Color = new SKColor(0xCC, 0x9E, 0x00, 0x40), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3) };
        private static readonly SKPaint _glowG = new() { Color = new SKColor(0x20, 0xA0, 0x50, 0x40), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3) };

        // Text paint for labels (created once, size adjusted at draw time)
        private static readonly SKPaint _textPaint = new()
        {
            Color = SKColors.White,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold)
        };

        private static readonly SKPaint _coordPaint = new()
        {
            Color = new SKColor(0x55, 0x55, 0x55),
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Consolas")
        };

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
                _roverPos = _map.StartPos;

                _camX = _map.W / 2f;
                _camY = _map.H / 2f;
                _zoom = Math.Min(
                    (float)(MapCanvas.ActualWidth > 0 ? MapCanvas.ActualWidth : 1000) / _map.W,
                    (float)(MapCanvas.ActualHeight > 0 ? MapCanvas.ActualHeight : 800) / _map.H
                ) * 0.9f;
                if (_zoom < 5) _zoom = 5;

                TxtMinBar.Text = FormatAsciiBar(0, _totalMin);
                TxtStatus.Text = $"> TÉRKÉP: {_map.W}x{_map.H} | START: {_map.StartPos} | ÁSVÁNYOK: {_totalMin}_";
                MapCanvas.InvalidateVisual();
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
        //  2D SKIA RENDER
        // ══════════════════════════════════════

        private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(new SKColor(0x08, 0x08, 0x08));

            if (_map == null) return;

            float cx = info.Width / 2f;
            float cy = info.Height / 2f;

            canvas.Save();
            canvas.Translate(cx - _camX * _zoom, cy - _camY * _zoom);
            canvas.Scale(_zoom, _zoom);

            float gap = 0.05f;
            bool showLabels = _zoom >= 14;  // Ásvány betűk megjelenítése
            bool showCoords = _zoom >= 22;  // Koordináta számok

            // Visible area kiszámítás → csak a látható cellákat rajzoljuk
            float left = _camX - cx / _zoom;
            float top = _camY - cy / _zoom;
            float right = _camX + cx / _zoom;
            float bottom = _camY + cy / _zoom;

            int rMin = Math.Max(0, (int)top - 1);
            int rMax = Math.Min(_map.H - 1, (int)bottom + 1);
            int cMin = Math.Max(0, (int)left - 1);
            int cMax = Math.Min(_map.W - 1, (int)right + 1);

            // Cellák
            for (int r = rMin; r <= rMax; r++)
            for (int c = cMin; c <= cMax; c++)
            {
                var ct = _map.Grid[r, c];
                var pos = new Pos(r, c);
                float x = c + gap;
                float y = r + gap;
                float s = 1f - gap * 2;

                bool collected = _sim != null && _sim.Collected.Contains(pos);
                SKPaint paint;

                if (collected)
                    paint = _paintCollected;
                else
                    paint = ct switch
                    {
                        CellType.Wall => _paintWall,
                        CellType.MineralB => _paintMineralB,
                        CellType.MineralY => _paintMineralY,
                        CellType.MineralG => _paintMineralG,
                        CellType.Start => _paintStart,
                        _ => _paintEmpty
                    };

                canvas.DrawRect(x, y, s, s, paint);

                // Fal teteje
                if (ct == CellType.Wall)
                    canvas.DrawRect(x, y, s, 0.12f, _paintWallTop);

                // Glow (nem collected)
                if (!collected)
                {
                    if (ct == CellType.MineralB) canvas.DrawCircle(c + 0.5f, r + 0.5f, 0.7f, _glowB);
                    else if (ct == CellType.MineralY) canvas.DrawCircle(c + 0.5f, r + 0.5f, 0.7f, _glowY);
                    else if (ct == CellType.MineralG) canvas.DrawCircle(c + 0.5f, r + 0.5f, 0.7f, _glowG);
                }

                // Ásvány felirat kinagyítva
                if (showLabels && !collected && ct is CellType.MineralB or CellType.MineralY or CellType.MineralG)
                {
                    string label = ct switch
                    {
                        CellType.MineralB => "B",
                        CellType.MineralY => "Y",
                        CellType.MineralG => "G",
                        _ => ""
                    };
                    _textPaint.TextSize = 0.55f;
                    _textPaint.Color = SKColors.White;
                    canvas.DrawText(label, c + 0.5f, r + 0.72f, _textPaint);
                }

                // Koordináta label max zoom-nál
                if (showCoords && ct == CellType.Empty)
                {
                    _coordPaint.TextSize = 0.25f;
                    canvas.DrawText($"{r},{c}", c + 0.5f, r + 0.62f, _coordPaint);
                }
            }

            // Nyomvonal
            foreach (var tp in _trailSet)
            {
                if (tp.Row < rMin || tp.Row > rMax || tp.Col < cMin || tp.Col > cMax) continue;
                canvas.DrawRect(tp.Col + 0.3f, tp.Row + 0.3f, 0.4f, 0.4f, _paintTrail);
            }

            // Tervezett célok
            if (_sim != null)
            {
                foreach (var t in _sim.PlannedRoute)
                {
                    if (_sim.Collected.Contains(t)) continue;
                    if (t.Row < rMin || t.Row > rMax || t.Col < cMin || t.Col > cMax) continue;
                    canvas.DrawRect(t.Col + 0.1f, t.Row + 0.1f, 0.8f, 0.8f, _paintTarget);
                }
            }

            // Rover
            canvas.DrawCircle(_roverPos.Col + 0.5f, _roverPos.Row + 0.5f, 1.5f, _paintRoverGlow);
            canvas.DrawRect(_roverPos.Col + 0.12f, _roverPos.Row + 0.12f, 0.76f, 0.76f, _paintRover);

            // Rover label
            if (showLabels)
            {
                _textPaint.TextSize = 0.4f;
                _textPaint.Color = SKColors.White;
                canvas.DrawText("R", _roverPos.Col + 0.5f, _roverPos.Row + 0.65f, _textPaint);
            }

            // Start label
            if (showLabels && _map.StartPos.Row >= rMin && _map.StartPos.Row <= rMax)
            {
                _textPaint.TextSize = 0.4f;
                _textPaint.Color = new SKColor(0x0A, 0x0A, 0x0A);
                canvas.DrawText("S", _map.StartPos.Col + 0.5f, _map.StartPos.Row + 0.65f, _textPaint);
            }

            canvas.Restore();

            // Éjszakai overlay (screen-space)
            if (_isNight)
                canvas.DrawRect(0, 0, info.Width, info.Height, _paintNightOverlay);

            // Zoom indicator (jobb alsó)
            var zoomPaint = new SKPaint
            {
                Color = new SKColor(0x44, 0x44, 0x44),
                IsAntialias = true,
                TextSize = 14,
                Typeface = SKTypeface.FromFamilyName("Consolas")
            };
            canvas.DrawText($"x{_zoom:F1}", info.Width - 60, info.Height - 12, zoomPaint);
        }

        // ══════════════════════════════════════
        //  KAMERA VEZÉRLÉS
        // ══════════════════════════════════════

        private void OnMapMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Zoom towards mouse position
            var mousePos = e.GetPosition(MapCanvas);
            float factor = e.Delta > 0 ? 1.2f : 0.83f;
            float newZoom = Math.Clamp(_zoom * factor, 4f, 50f);

            // Adjust cam so the point under mouse stays put
            float worldX = _camX + ((float)mousePos.X - (float)MapCanvas.ActualWidth / 2f) / _zoom;
            float worldY = _camY + ((float)mousePos.Y - (float)MapCanvas.ActualHeight / 2f) / _zoom;

            _camX = worldX - ((float)mousePos.X - (float)MapCanvas.ActualWidth / 2f) / newZoom;
            _camY = worldY - ((float)mousePos.Y - (float)MapCanvas.ActualHeight / 2f) / newZoom;

            _zoom = newZoom;
            MapCanvas.InvalidateVisual();
        }

        private void OnMapMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragging = true;
            _dragStart = e.GetPosition(MapCanvas);
            _dragCamX = _camX;
            _dragCamY = _camY;
            MapCanvas.CaptureMouse();
        }

        private void OnMapMouseUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
            MapCanvas.ReleaseMouseCapture();
        }

        private void OnMapMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var pos = e.GetPosition(MapCanvas);
            _camX = _dragCamX - (float)(pos.X - _dragStart.X) / _zoom;
            _camY = _dragCamY - (float)(pos.Y - _dragStart.Y) / _zoom;
            MapCanvas.InvalidateVisual();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            float moveSpd = 40f / _zoom;
            switch (e.Key)
            {
                case Key.W: _camY -= moveSpd; break;
                case Key.S: _camY += moveSpd; break;
                case Key.A: _camX -= moveSpd; break;
                case Key.D: _camX += moveSpd; break;
                case Key.OemPlus:
                case Key.Add:
                    _zoom = Math.Min(50, _zoom * 1.2f); break;
                case Key.OemMinus:
                case Key.Subtract:
                    _zoom = Math.Max(4, _zoom * 0.83f); break;
                default: return;
            }
            MapCanvas.InvalidateVisual();
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
            TxtStatus.Text = "> AI ÚTVONALTERVEZÉS..._";

            Task.Run(() =>
            {
                _sim.PlanInitial();
                Dispatcher.Invoke(() =>
                {
                    TxtStatus.Text = $"> CÉLOK: {_sim.PlannedRoute.Count} | SZIMULÁCIÓ INDÍTVA_";
                    StartSim();
                });
            });
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

            _roverPos = l.Position;
            foreach (var t in _sim.Trail) _trailSet.Add(t);
            _isNight = l.Phase == DayPhase.Night;
            NightOverlay.Opacity = _isNight ? 1 : 0;

            UpdateDash(l);
            AddLog(l);
            MapCanvas.InvalidateVisual();

            if (_sim.Finished) { _timer?.Stop(); Done(); }
        }

        private void UpdateDash(LogEntry l)
        {
            TxtBatt.Text = $"{l.Battery:F0}";
            TxtBatt.Foreground = new SolidColorBrush(l.Battery switch
            {
                > 50 => Color.FromRgb(0x33, 0xFF, 0x33),
                > 25 => Color.FromRgb(0xFF, 0xD6, 0x33),
                _ => Color.FromRgb(0xFF, 0x33, 0x33)
            });

            string s = l.EnergyDelta >= 0 ? "+" : "";
            TxtDelta.Text = $"{s}{l.EnergyDelta:F1}";
            TxtDelta.Foreground = new SolidColorBrush(l.EnergyDelta >= 0
                ? Color.FromRgb(0x33, 0xFF, 0x33) : Color.FromRgb(0xFF, 0x33, 0x33));

            TxtBattBar.Text = FormatAsciiBattBar(l.Battery);
            TxtBattBar.Foreground = TxtBatt.Foreground.Clone();
            TxtBattInfo.Text = $"TÖLT:{(l.Phase == DayPhase.Day ? "+10" : " 0")} FOGY:-{Simulator.K * (int)l.Speed * (int)l.Speed:F0}";

            TxtMinTotal.Text = l.TotalMinerals.ToString();
            TxtMinB.Text = $"KÉK:{l.MineralsB}";
            TxtMinY.Text = $"SÁR:{l.MineralsY}";
            TxtMinG.Text = $"ZÖL:{l.MineralsG}";
            TxtMinBar.Text = FormatAsciiBar(l.TotalMinerals, _totalMin);

            RunPos.Text = $"({l.Position.Row},{l.Position.Col})";
            RunSpd.Text = l.Speed switch { SpeedLevel.Slow => "LASSÚ", SpeedLevel.Normal => "NORM", SpeedLevel.Fast => "GYORS", _ => "?" };
            RunAct.Text = l.Action switch
            {
                RoverAction.Moving => "MOZGÁS", RoverAction.Mining => "BÁNYÁSZ",
                RoverAction.Standby => "STAND", RoverAction.Returning => "HAZA",
                RoverAction.WaitingForDawn => "VÁR", RoverAction.Charging => "TÖLT", _ => "?"
            };
            ((System.Windows.Documents.Run)RunAct).Foreground = new SolidColorBrush(l.Action switch
            {
                RoverAction.Mining => Color.FromRgb(0x33, 0xFF, 0x33),
                RoverAction.Returning => Color.FromRgb(0xFF, 0xD6, 0x33),
                RoverAction.WaitingForDawn => Color.FromRgb(0x33, 0x88, 0xFF),
                RoverAction.Charging => Color.FromRgb(0x33, 0xFF, 0xCC),
                _ => Color.FromRgb(0xFF, 0xB8, 0x33)
            });

            RunDist.Text = l.TotalDistance.ToString();
            RunSol.Text = ((l.Tick / Simulator.CYCLE_TICKS) + 1).ToString();
            RunPhChg.Text = $"{_sim!.TicksToPhaseChange * 0.5:F1}h";
            TxtClock.Text = $"  T+{l.SimHours:F1}h/{_sim.TotalHours}h";

            bool night = l.Phase == DayPhase.Night;
            TxtPhase.Text = night ? "ÉJSZAKA" : "NAPPAL";
            TxtPhaseIcon.Text = night ? "●" : "☀";
            var phCol = night ? Color.FromRgb(0x33, 0x88, 0xFF) : Color.FromRgb(0xFF, 0xD6, 0x33);
            TxtPhase.Foreground = new SolidColorBrush(phCol);
            TxtPhaseIcon.Foreground = new SolidColorBrush(phCol);
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
                ? $"> SIKERES! {_sim.TotalMin}/{_totalMin} ásvány | Sol {(_sim.Tick / Simulator.CYCLE_TICKS) + 1}_"
                : $"> IDŐ LEJÁRT. {_sim.TotalMin}/{_totalMin} ásvány._";
        }

        // ══════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════

        private static string FormatAsciiBattBar(double pct)
        {
            int filled = Math.Clamp((int)(pct / 100.0 * 20), 0, 20);
            return $"[{new string('█', filled)}{new string('░', 20 - filled)}] {pct:F0}%";
        }

        private static string FormatAsciiBar(int current, int total)
        {
            double pct = total > 0 ? (double)current / total : 0;
            int filled = Math.Clamp((int)(pct * 20), 0, 20);
            return $"[{new string('█', filled)}{new string('░', 20 - filled)}] {current}/{total}";
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            _paused = !_paused;
            BtnPause.Content = _paused ? "▶" : "║║";
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();
            _sim = null;
            _paused = false;
            _trailSet.Clear();
            _isNight = false;
            NightOverlay.Opacity = 0;

            BtnGo.IsEnabled = true;
            BtnPause.IsEnabled = false;
            BtnPause.Content = "║║";
            BtnCsv.IsEnabled = false;
            BtnReset.IsEnabled = true;
            TxtHours.IsEnabled = true;
            LogBox.Items.Clear();

            if (_map != null)
            {
                _roverPos = _map.StartPos;
                _camX = _map.W / 2f;
                _camY = _map.H / 2f;
            }

            MapCanvas.InvalidateVisual();
            TxtStatus.Text = "> ÚJRAINDÍTVA._";
            TxtBatt.Text = "100"; TxtBatt.Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0xFF, 0x33));
            TxtMinTotal.Text = "0";
            TxtMinB.Text = "KÉK:0"; TxtMinY.Text = "SÁR:0"; TxtMinG.Text = "ZÖL:0";
            TxtMinBar.Text = FormatAsciiBar(0, _totalMin);
            TxtBattBar.Text = FormatAsciiBattBar(100);
            RunPos.Text = _map != null ? $"({_map.StartPos.Row},{_map.StartPos.Col})" : "-";
            RunSpd.Text = "LASSÚ"; RunAct.Text = "STANDBY"; RunDist.Text = "0";
            RunSol.Text = "1"; RunPhChg.Text = "16.0h"; TxtClock.Text = "T+0.0h";
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
                TxtStatus.Text = $"> CSV: {dlg.FileName}_";
            }
        }

        private static SKPaint MakeFill(uint argb) => new()
        {
            Color = new SKColor(argb),
            IsAntialias = false,
            Style = SKPaintStyle.Fill
        };
    }
}
