// FTG-Overlay —— 悬浮小抄窗：置顶 / 鼠标穿透 / 热键 / 黑底面板一行一连段
// 显示内容按管理器当前选择的 游戏 + 角色 过滤，且只显示勾选了"显示"的连招

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FTGOverlayDemo
{
    public class OverlayWindow : Window
    {
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        const int GWL_EXSTYLE = -20;
        const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x8000000;
        const int WM_HOTKEY = 0x0312;
        const uint MOD_CONTROL = 0x2, MOD_ALT = 0x1;

        readonly Store _store;
        HwndSource _src;
        Border _panel; StackPanel _host;
        LockWindow _lockWin;
        bool _visible = true;
        readonly string _shotPath;

        /// <summary>锁定状态变化（true=锁定/穿透，false=解锁/可拖动），管理器据此同步</summary>
        public event Action<bool> LockStateChanged;

        public OverlayWindow(Store store, string shotPath)
        {
            _store = store;
            _shotPath = shotPath;

            AllowsTransparency = true;
            WindowStyle = WindowStyle.None;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Title = "FTG-Overlay";
            Left = Clamp(_store.Settings.X, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 200);
            Top = Clamp(_store.Settings.Y, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100);

            var root = new StackPanel();
            _panel = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(18, 12, 18, 12),
                SnapsToDevicePixels = true
            };
            _host = new StackPanel();
            _panel.Child = _host;
            root.Children.Add(_panel);
            Content = root;

            _panel.MouseLeftButtonDown += PanelOnMouseLeftButtonDown;

            // 小锁头：独立小窗，任何状态下都可点击切换锁定；吸附在面板右侧
            _lockWin = new LockWindow(_store.Settings.Locked);
            _lockWin.LockClicked += v => SetLocked(v);
            LocationChanged += (s, e) => UpdateLockPos();
            SizeChanged += (s, e) => UpdateLockPos();
            ContentRendered += (s, e) => { if (IsVisible) { _lockWin.Show(); UpdateLockPos(); } };

            ApplyTheme();
            ApplyScale();
            RenderCombos();

            if (_shotPath != null)
                SourceInitialized += (s, e) => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(DoShot));
        }

        // ---------- 数据与渲染 ----------

        public void RefreshAll()
        {
            ApplyTheme();
            ApplyScale();
            RenderCombos();
        }

        public void ApplyStyle()   // 仅样式（主题/透明度/大小），不重解析数据
        {
            ApplyTheme();
            ApplyScale();
        }

        void RenderCombos()
        {
            _host.Children.Clear();
            var s = _store.Settings;
            var list = _store.Combos.FindAll(c =>
                c.Game == s.Game && c.Show &&
                (s.Character == null ? c.Character == null : (c.Character == null || c.Character == s.Character)));
            var tc = ThemeColorsFor();

            if (list.Count == 0)
            {
                _host.Children.Add(new TextBlock
                {
                    Text = "（当前游戏 / 角色下没有勾选显示的连招）",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                    FontFamily = new FontFamily("Microsoft YaHei UI"),
                    Margin = new Thickness(2, 4, 2, 4)
                });
                _host.Children.Add(new TextBlock
                {
                    Text = "在管理器中添加连招并勾选「显示」即可在此处看到",
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush(Color.FromArgb(130, 255, 255, 255)),
                    FontFamily = new FontFamily("Microsoft YaHei UI"),
                    Margin = new Thickness(2, 0, 2, 4)
                });
            }
            else
            {
                foreach (var c in list)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                    row.SetValue(ToolTipProperty, (c.Name.Length > 0 ? c.Name + "：" : "") + c.Raw);
                    ComboRenderer.BuildInto(row, c.Tokens, s.DisplayMode, s.ButtonStyle, tc);
                    _host.Children.Add(row);
                }
            }
        }

        ThemeColors ThemeColorsFor()
        {
            return _store.Settings.Theme == "C"
                ? new ThemeColors
                {
                    Arrow = new SolidColorBrush(Color.FromRgb(40, 224, 224)),
                    Sep = new SolidColorBrush(Color.FromArgb(150, 40, 224, 224))
                }
                : new ThemeColors
                {
                    Arrow = new SolidColorBrush(Color.FromRgb(238, 240, 245)),
                    Sep = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255))
                };
        }

        // ---------- 主题 / 透明度 / 大小 ----------

        void ApplyTheme()
        {
            var a = Math.Max(0.15, Math.Min(0.95, _store.Settings.Alpha));
            if (_store.Settings.Theme == "C")
            {
                _panel.Background = new SolidColorBrush(Color.FromArgb((byte)(a * 255), 0x07, 0x09, 0x12));
                _panel.BorderBrush = new SolidColorBrush(Color.FromArgb(150, 40, 224, 224));
                _panel.BorderThickness = new Thickness(1);
                _panel.Effect = new DropShadowEffect { Color = Color.FromRgb(40, 224, 224), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.55 };
            }
            else
            {
                _panel.Background = new SolidColorBrush(Color.FromArgb((byte)(a * 255), 0x0A, 0x0B, 0x0E));
                _panel.BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255));
                _panel.BorderThickness = new Thickness(1);
                _panel.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 2, Opacity = 0.45 };
            }
        }

        void ApplyScale()
        {
            var sc = Math.Max(0.7, Math.Min(1.8, _store.Settings.Scale));
            ((StackPanel)Content).LayoutTransform = new ScaleTransform(sc, sc);
        }

        public void SetVisible(bool vis)
        {
            _visible = vis;
            _store.Settings.OverlayVisible = vis;
            if (vis)
            {
                Show();
                _lockWin.Show();
                UpdateLockPos();
            }
            else
            {
                Hide();
                _lockWin.Hide();
            }
            _store.SaveSettings();
        }

        // ---------- 锁定状态 ----------

        public bool Locked { get { return _store.Settings.Locked; } }

        public void SetLocked(bool locked)
        {
            _store.Settings.Locked = locked;
            _lockWin.SetState(locked);
            ApplyExStyle();
            _store.SaveSettings();
            if (LockStateChanged != null) LockStateChanged(locked);
        }

        void UpdateLockPos()
        {
            if (_lockWin == null || !IsVisible) return;
            _lockWin.Left = Left + ActualWidth + 6;
            _lockWin.Top = Top + 2;
        }

        public bool IsShown { get { return _visible; } }

        // ---------- 全局热键（仅保留安全的组合键退出；其余状态调整全部经由管理器） ----------

        void RegisterHotKeys()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Reg(hwnd, 10, MOD_CONTROL | MOD_ALT, 'Q'); // Ctrl+Alt+Q 退出
        }

        void Reg(IntPtr hwnd, int id, uint mods, uint vk)
        {
            RegisterHotKey(hwnd, id, mods, vk);
        }

        IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                HandleHotkey(wParam.ToInt32());
                handled = true;
            }
            return IntPtr.Zero;
        }

        void HandleHotkey(int id)
        {
            if (id == 10) Application.Current.Shutdown();
        }

        void ApplyExStyle()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (!Locked) ex &= ~(WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
            else ex |= (WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }

        void PanelOnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!Locked)
            {
                try { DragMove(); } catch { }
                _store.Settings.X = Left;
                _store.Settings.Y = Top;
                _store.SaveSettings();
            }
        }

        static double Clamp(double v, double min, double max) { return v < min ? min : (v > max ? max : v); }

        // ---------- 生命周期 ----------

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _src.AddHook(HwndHook);
            RegisterHotKeys();
            ApplyExStyle();
        }

        void DoShot()
        {
            try
            {
                var root = (FrameworkElement)Content;
                root.UpdateLayout();
                var w = (int)Math.Ceiling(root.ActualWidth);
                var h = (int)Math.Ceiling(root.ActualHeight);
                var rtb = new RenderTargetBitmap(Math.Max(w, 1), Math.Max(h, 1), 96, 96, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = File.Create(_shotPath)) enc.Save(fs);
            }
            catch (Exception ex)
            {
                File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shot-error.txt"), ex.ToString());
            }
            finally
            {
                Application.Current.Shutdown();
            }
        }

        public void ShootLock(string path)
        {
            try
            {
                var root = (FrameworkElement)_lockWin.Content;
                root.UpdateLayout();
                var w = (int)Math.Ceiling(root.ActualWidth);
                var h = (int)Math.Ceiling(root.ActualHeight);
                // 4 倍渲染，便于查看细节
                var rtb = new RenderTargetBitmap(Math.Max(w, 1) * 4, Math.Max(h, 1) * 4, 384, 384, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = File.Create(path)) enc.Save(fs);
            }
            catch (Exception ex)
            {
                File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shot-error.txt"), ex.ToString());
            }
            finally
            {
                Application.Current.Shutdown();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_src != null)
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                for (int i = 1; i <= 10; i++) UnregisterHotKey(hwnd, i);
                _src.RemoveHook(HwndHook);
            }
            _store.Settings.X = Left;
            _store.Settings.Y = Top;
            _store.SaveSettings();
            base.OnClosed(e);
        }
    }

    /// <summary>
    /// 小锁头窗：独立于悬浮窗本体，因此悬浮窗锁定（鼠标穿透）时它仍可点击。
    /// 锁定 = 闭锁图标；解锁 = 开锁图标（描边高亮提示可拖动）。
    /// </summary>
    public class LockWindow : Window
    {
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        const int GWL_EXSTYLE = -20;
        const int WS_EX_NOACTIVATE = 0x8000000;

        bool _locked = true;
        readonly Border _button;
        readonly UIElement _shackleClosed, _shackleOpen;

        /// <summary>用户点击锁头，参数 = 切换后的状态（true=锁定）</summary>
        public event Action<bool> LockClicked;

        public LockWindow(bool initialLocked)
        {
            _locked = initialLocked;
            Width = 17;
            Height = 17;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Title = "FTG-Overlay 锁";

            var icon = BuildIcon(out _shackleClosed, out _shackleOpen);
            icon.LayoutTransform = new ScaleTransform(0.55, 0.55);
            _button = new Border
            {
                Width = 15,
                Height = 15,
                CornerRadius = new CornerRadius(7.5),
                Background = new SolidColorBrush(Color.FromArgb(215, 12, 13, 18)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = icon,
                ToolTip = "锁头：点击切换锁定（锁定=鼠标穿透不可移动；解锁=可拖动面板）"
            };
            _button.MouseLeftButtonDown += (s, e) =>
            {
                var now = !_locked;
                SetState(now);
                if (LockClicked != null) LockClicked(now);
            };
            Content = _button;
            SetState(_locked);
        }

        static Grid BuildIcon(out UIElement shackleClosed, out UIElement shackleOpen)
        {
            var grid = new Grid { Width = 20, Height = 20 };

            var brush = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF0));
            // 锁梁：闭锁 = 两腿都插进锁体
            var closed = new System.Windows.Shapes.Path
            {
                Stroke = brush,
                StrokeThickness = 2.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = Geometry.Parse("M 6.8,10 L 6.8,6.6 A 3.2,3.2 0 0 1 13.2,6.6 L 13.2,10")
            };
            // 锁梁：开锁 = 右腿留在锁体，左腿抬起（开口明显）
            var open = new System.Windows.Shapes.Path
            {
                Stroke = brush,
                StrokeThickness = 2.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = Geometry.Parse("M 13.2,10 L 13.2,6.6 A 3.2,3.2 0 0 0 8.2,4.6")
            };

            // 锁体 + 钥匙孔
            var body = new Border
            {
                Width = 12,
                Height = 8.5,
                CornerRadius = new CornerRadius(2.2),
                Background = brush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 2.5)
            };
            var keyhole = new Grid { Width = 12, Height = 8.5 };
            keyhole.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 3.4,
                Height = 3.4,
                Fill = new SolidColorBrush(Color.FromRgb(0x12, 0x13, 0x1A)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2.1, 0, 0)
            });
            keyhole.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width = 1.7,
                Height = 3.2,
                RadiusX = 0.8,
                RadiusY = 0.8,
                Fill = new SolidColorBrush(Color.FromRgb(0x12, 0x13, 0x1A)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4.8, 0, 0)
            });
            body.Child = keyhole;

            shackleClosed = closed;
            shackleOpen = open;
            grid.Children.Add(closed);
            grid.Children.Add(open);
            grid.Children.Add(body);
            return grid;
        }

        public void SetState(bool locked)
        {
            _locked = locked;
            _shackleClosed.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
            _shackleOpen.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
            _button.BorderBrush = locked
                ? new SolidColorBrush(Color.FromArgb(90, 255, 255, 255))
                : new SolidColorBrush(Color.FromArgb(220, 110, 168, 255));
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // 不抢焦点：点击锁头不影响游戏
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE);
        }
    }
}
