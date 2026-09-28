// FTG-Overlay —— 管理器窗口
// 两个标签页：
//   「导入」= 选游戏 / 选角色 + 录入连招（连招信息必填，标题留空自动命名 Combo x）+ 已存连招管理
//   「展示」= 悬浮窗设置（开关、锁定、主题、透明度、大小；表现形式等不常更改的设置折叠收纳）
// 所有修改自动保存并同步到悬浮窗；关闭本窗口 = 退出整个程序

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FTGOverlayDemo
{
    public class ManagerWindow : Window
    {
        readonly Store _store;
        readonly OverlayWindow _overlay;
        readonly string _shotPath;

        ComboBox _gameBox, _charBox;
        TextBox _rawBox, _nameBox, _notesBox;
        StackPanel _listHost, _previewHost;
        Border _previewBorder;
        RadioButton _rbArrows, _rbDigits, _rbText, _rbMnemo, _rbOrig, _rbThemeA, _rbThemeC;
        CheckBox _lockBox;
        Slider _alphaSlider, _scaleSlider;
        Button _overlayBtn;
        TextBlock _status;
        TabControl _tabs;
        string _editingId;
        int _updating;   // >0 = 正在程序化更新界面（可嵌套），屏蔽事件回写

        static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x36));
        static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x9B));
        static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x4F, 0x6E, 0xF7));

        public ManagerWindow(Store store, OverlayWindow overlay, string shotPath)
        {
            _store = store;
            _overlay = overlay;
            _shotPath = shotPath;
            _updating++;   // 构建界面期间屏蔽所有变更事件

            Title = "FTG-Overlay 管理器";
            Width = 760;
            Height = 620;
            MinWidth = 660;
            MinHeight = 520;
            Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF5, 0xF7));

            Content = BuildUi();
            SyncContextToUi();
            RebuildList();
            PreviewRefresh();
            // 锁头图标 / 管理器以外的途径切换锁定后，同步这里的勾选框
            _overlay.LockStateChanged += v =>
            {
                if (_updating > 0) return;
                _updating++;
                _lockBox.IsChecked = !v;
                _updating = Math.Max(0, _updating - 1);
            };
            _updating = Math.Max(0, _updating - 1);

            if (_shotPath != null)
                SourceInitialized += (s, e) => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(DoShot));
        }

        // ---------------- 界面构建 ----------------

        UIElement BuildUi()
        {
            var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF5, 0xF7)) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var tab = new TabControl
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF5, 0xF7)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 6, 10, 6)
            };
            _tabs = tab;
            tab.Items.Add(new TabItem { Header = "导　入", Content = BuildImportTab(), FontSize = 14, FontWeight = FontWeights.Bold });
            tab.Items.Add(new TabItem { Header = "展　示", Content = BuildShowTab(), FontSize = 14, FontWeight = FontWeights.Bold });
            Grid.SetRow(tab, 0);
            root.Children.Add(tab);

            // 底部状态栏
            var foot = new Border { Background = new SolidColorBrush(Color.FromRgb(0xEA, 0xEC, 0xF0)), Padding = new Thickness(14, 6, 14, 6) };
            _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Foreground = Dim };
            var btnReload = Btn("重载数据文件", ReloadDataFiles, Brushes.White, Ink, 110, 8);
            var btnFolder = Btn("打开数据文件夹", OpenDataFolder, Brushes.White, Ink, 120, 8);
            var btnSave = Btn("保存全部", (s, e) => { _store.SaveAll(); Status("已保存全部数据"); }, Brushes.White, Ink, 90, 8);
            var spacer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            spacer.Children.Add(btnReload); spacer.Children.Add(btnFolder); spacer.Children.Add(btnSave);
            var footGrid = new Grid();
            footGrid.Children.Add(_status);
            footGrid.Children.Add(spacer);
            foot.Child = footGrid;
            Grid.SetRow(foot, 1);
            root.Children.Add(foot);

            return root;
        }

        UIElement BuildImportTab()
        {
            var sp = new StackPanel { Margin = new Thickness(4, 8, 4, 8) };

            // 游戏 + 角色
            var ctxRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            ctxRow.Children.Add(new TextBlock { Text = "游戏　", VerticalAlignment = VerticalAlignment.Center, Foreground = Ink, FontSize = 12.5 });
            _gameBox = new ComboBox { Width = 200, Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
            _gameBox.SelectionChanged += (s, e) => { if (_updating == 0) ApplyGame(); };
            ctxRow.Children.Add(_gameBox);
            ctxRow.Children.Add(new TextBlock { Text = "　　角色　", VerticalAlignment = VerticalAlignment.Center, Foreground = Ink, FontSize = 12.5 });
            _charBox = new ComboBox { Width = 200, Height = 28, IsEditable = true, VerticalContentAlignment = VerticalAlignment.Center };
            _charBox.SelectionChanged += (s, e) => { if (_updating == 0) ApplyCharacter(); };
            _charBox.LostFocus += (s, e) => { if (_updating == 0) ApplyCharacter(); };
            _charBox.KeyDown += (s, e) => { if (e.Key == Key.Enter && _updating == 0) ApplyCharacter(); };
            ctxRow.Children.Add(_charBox);
            sp.Children.Add(ctxRow);
            sp.Children.Add(new TextBlock
            {
                Text = "连招按角色保存：「全角色通用」下的连招在该游戏任何角色下都会显示；直接输入新名字即可创建角色。",
                FontSize = 11.5, Foreground = Dim, Margin = new Thickness(0, 2, 0, 8), TextWrapping = TextWrapping.Wrap
            });

            // 录入区
            _rawBox = new TextBox
            {
                Height = 56,
                TextWrapping = TextWrapping.Wrap,
                VerticalContentAlignment = VerticalAlignment.Top,
                FontSize = 13.5
            };
            _rawBox.TextChanged += (s, e) => { if (_updating == 0) PreviewRefresh(); };
            sp.Children.Add(Labeled("连招信息（必填——直接粘贴网上给出的连段代码，DreamCancel / 数字记法均可，逗号分隔步骤）", _rawBox));

            sp.Children.Add(Labeled("预览（随输入与「展示」页的表现形式实时更新）", PreviewPanel()));

            _nameBox = new TextBox { Height = 26, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13 };
            sp.Children.Add(Labeled("标题（可选——留空自动命名为 Combo x）", _nameBox));

            _notesBox = new TextBox { Height = 26, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13 };
            sp.Children.Add(Labeled("备注（可选）", _notesBox));

            var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 4) };
            saveRow.Children.Add(Btn("保存连招", SaveEditor, Accent, Brushes.White, 110));
            saveRow.Children.Add(Btn("清空编辑器", ClearEditor, Brushes.White, Ink, 96, 8));
            sp.Children.Add(saveRow);
            sp.Children.Add(new TextBlock
            {
                Text = "保存后到「展示」页勾选「显示」即可上悬浮窗；列表的编辑 / 删除也在「展示」页。",
                FontSize = 11.5, Foreground = Dim, Margin = new Thickness(0, 2, 0, 0)
            });

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
            return scroll;
        }

        UIElement BuildShowTab()
        {
            var sp = new StackPanel { Margin = new Thickness(4, 12, 4, 8) };

            // 一、要显示的连招列表（主体）
            sp.Children.Add(new TextBlock
            {
                Text = "要显示的连招（当前游戏 / 角色）——勾选「显示」才会上悬浮窗；点名称可编辑：",
                FontSize = 12.5, Foreground = Ink, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4)
            });
            _listHost = new StackPanel();
            var listBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xE0, 0xE6)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6)
            };
            listBorder.Child = _listHost;
            sp.Children.Add(listBorder);

            _overlayBtn = new Button
            {
                Content = "打开悬浮窗",
                Height = 32,
                Padding = new Thickness(14, 0, 14, 0),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 10, 0, 2)
            };
            _overlayBtn.Click += (s, e) => ToggleOverlay();
            sp.Children.Add(_overlayBtn);

            // 二、选项（默认折叠）
            _lockBox = new CheckBox
            {
                Content = "允许拖动面板（不勾选 = 锁定：鼠标穿透、不可移动）",
                FontSize = 12.5,
                Foreground = Ink,
                Margin = new Thickness(0, 3, 0, 3)
            };
            _lockBox.Checked += LockBoxChanged;
            _lockBox.Unchecked += LockBoxChanged;
            _rbThemeA = Radio("A 简约深色（默认）", "A", "theme");
            _rbThemeC = Radio("C 霓虹街机", "C", "theme");
            _rbThemeA.Checked += StyleChanged; _rbThemeC.Checked += StyleChanged;
            _alphaSlider = new Slider { Minimum = 15, Maximum = 95, Value = _store.Settings.Alpha * 100, TickFrequency = 5, IsSnapToTickEnabled = false, Width = 260 };
            _alphaSlider.ValueChanged += (s, e) => { if (_updating == 0) { _store.Settings.Alpha = _alphaSlider.Value / 100.0; _overlay.ApplyStyle(); _store.SaveSettings(); } };
            _scaleSlider = new Slider { Minimum = 0.7, Maximum = 1.8, Value = _store.Settings.Scale, TickFrequency = 0.05, Width = 260 };
            _scaleSlider.ValueChanged += (s, e) => { if (_updating == 0) { _store.Settings.Scale = _scaleSlider.Value; _overlay.ApplyStyle(); _store.SaveSettings(); } };
            _rbArrows = Radio("图形化箭头（推荐：236 → ↓↘→，按钮变彩色块）", "arrows", "displayMode");
            _rbDigits = Radio("数字写法（保留 236 / 421 等数字指令）", "digits", "displayMode");
            _rbText = Radio("文本原样（按输入原文显示，如 cr.B）", "text", "displayMode");
            _rbArrows.Checked += DisplayChanged; _rbDigits.Checked += DisplayChanged; _rbText.Checked += DisplayChanged;
            _rbMnemo = Radio("拳脚名 LP / LK / HP / HK（KOF 的 A/B/C/D 自动映射）", "mnemonic", "buttonStyle");
            _rbOrig = Radio("原按钮名 A / B / C / D", "original", "buttonStyle");
            _rbMnemo.Checked += DisplayChanged; _rbOrig.Checked += DisplayChanged;

            var optionsInner = new StackPanel { Margin = new Thickness(4, 2, 0, 2) };
            optionsInner.Children.Add(Group("面板移动与否（悬浮窗右侧的小锁头图标也能切换，任何状态下都可点击）：", _lockBox));
            optionsInner.Children.Add(Group("主题风格：", _rbThemeA, _rbThemeC));
            optionsInner.Children.Add(Row("面板透明度", _alphaSlider));
            optionsInner.Children.Add(Row("整体大小", _scaleSlider));
            optionsInner.Children.Add(Group("连招显示形式：", _rbArrows, _rbDigits, _rbText));
            optionsInner.Children.Add(Group("按钮显示名：", _rbMnemo, _rbOrig));
            var optionsExp = new Expander
            {
                Header = "选项（锁定 / 主题 / 透明度 / 大小 / 表现形式——默认收起，需要更改时展开）",
                IsExpanded = false,
                Content = optionsInner
            };
            sp.Children.Add(optionsExp);

            var helpExp = new Expander
            {
                Header = "说明（热键与游戏内可见性）",
                IsExpanded = false,
                Content = new TextBlock
                {
                    Text = "· 悬浮窗默认鼠标穿透、置顶显示，不占用任务栏。\n" +
                           "· 全局热键仅保留 Ctrl+Alt+Q（退出程序）；其余状态调整全部在本窗口完成。\n" +
                           "· 游戏内请使用「无边框全屏」或窗口模式——独占全屏下悬浮窗会被游戏画面覆盖。\n" +
                           "· 设计红线：本工具不读游戏内存、不注入进程、不模拟按键。",
                    FontSize = 11.5,
                    Foreground = Dim,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(4, 2, 0, 2)
                }
            };
            sp.Children.Add(helpExp);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
            return scroll;
        }

        // ---------------- 控件辅助 ----------------

        TextBlock Note(string text)
        {
            return new TextBlock { Text = text, FontSize = 11.5, Foreground = Dim, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        }

        UIElement Labeled(string label, UIElement control)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            sp.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Dim, Margin = new Thickness(0, 0, 0, 3) });
            sp.Children.Add(control);
            return sp;
        }

        UIElement Group(string title, params UIElement[] items)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
            sp.Children.Add(new TextBlock { Text = title, FontSize = 12.5, Foreground = Ink, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
            foreach (var i in items) sp.Children.Add(i);
            var border = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xE0, 0xE6)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 8)
            };
            border.Child = sp;
            return border;
        }

        StackPanel Row(string label, UIElement control)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            sp.Children.Add(new TextBlock { Text = label, Width = 80, VerticalAlignment = VerticalAlignment.Center, Foreground = Ink, FontSize = 12.5 });
            sp.Children.Add(control);
            return sp;
        }

        Grid Row2(string label, UIElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Ink, FontSize = 12.5 };
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(control, 1);
            g.Children.Add(lbl); g.Children.Add(control);
            return g;
        }

        Button Btn(string text, RoutedEventHandler onClick, Brush bg, Brush fg, double width, double ml = 0)
        {
            var b = new Button
            {
                Content = text,
                Width = width,
                Height = 28,
                Background = bg,
                Foreground = fg,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xCC, 0xD4)),
                Margin = new Thickness(ml, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            b.Click += onClick;
            return b;
        }

        UIElement PreviewPanel()
        {
            _previewBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 0x0A, 0x0B, 0x0E)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                MinHeight = 46
            };
            _previewHost = new StackPanel();
            _previewBorder.Child = _previewHost;
            return _previewBorder;
        }

        RadioButton Radio(string text, string value, string group)
        {
            return new RadioButton
            {
                Content = text,
                GroupName = group,
                Margin = new Thickness(0, 3, 0, 3),
                FontSize = 12.5,
                Foreground = Ink
            };
        }

        // ---------------- 数据绑定与操作 ----------------

        void SyncContextToUi()
        {
            _updating++;
            var s = _store.Settings;
            foreach (var g in _store.Games.Values)
            {
                var item = new ComboBoxItem { Tag = g.Id, Content = g.Name };
                _gameBox.Items.Add(item);
                if (g.Id == s.Game) _gameBox.SelectedItem = item;
            }
            if (_gameBox.SelectedItem == null && _gameBox.Items.Count > 0) _gameBox.SelectedIndex = 0;
            RebuildCharBox();
            SetRadiosFromSettings();
            _updating = Math.Max(0, _updating - 1);
        }

        void SetRadiosFromSettings()
        {
            var s = _store.Settings;
            _rbArrows.IsChecked = s.DisplayMode == "arrows";
            _rbDigits.IsChecked = s.DisplayMode == "digits";
            _rbText.IsChecked = s.DisplayMode == "text";
            _rbMnemo.IsChecked = s.ButtonStyle == "mnemonic";
            _rbOrig.IsChecked = s.ButtonStyle == "original";
            _rbThemeA.IsChecked = s.Theme != "C";
            _rbThemeC.IsChecked = s.Theme == "C";
            _lockBox.IsChecked = !s.Locked;
            _alphaSlider.Value = Math.Max(15, Math.Min(95, s.Alpha * 100));
            _scaleSlider.Value = Math.Max(0.7, Math.Min(1.8, s.Scale));
        }

        void RebuildCharBox()
        {
            var prev = _updating;
            _updating++;
            var cur = _store.Settings.Character;
            _charBox.Items.Clear();
            _charBox.Items.Add(new ComboBoxItem { Tag = null, Content = "全角色通用" });
            var seen = new List<string>();
            foreach (var c in _store.Combos)
            {
                if (c.Game != _store.Settings.Game || c.Character == null || seen.Contains(c.Character)) continue;
                seen.Add(c.Character);
                _charBox.Items.Add(new ComboBoxItem { Tag = c.Character, Content = c.Character });
            }
            foreach (var item in _charBox.Items)
            {
                var ci = (ComboBoxItem)item;
                if ((cur == null && ci.Tag == null) || (cur != null && Equals(ci.Tag, cur))) { _charBox.SelectedItem = ci; break; }
            }
            if (_charBox.SelectedItem == null) _charBox.SelectedIndex = 0;
            _updating = prev;
        }

        string SelectedGame()
        {
            var item = _gameBox.SelectedItem as ComboBoxItem;
            return item != null ? (string)item.Tag : _store.Settings.Game;
        }

        string ReadCharacter()
        {
            var item = _charBox.SelectedItem as ComboBoxItem;
            if (item != null) return (string)item.Tag;
            var text = _charBox.Text != null ? _charBox.Text.Trim() : "";
            if (text.Length == 0 || text == "全角色通用") return null;
            return text;
        }

        void ApplyGame()
        {
            var oldChar = _store.Settings.Character;
            _store.Settings.Game = SelectedGame();
            var chars = new List<string>();
            foreach (var c in _store.Combos)
                if (c.Game == _store.Settings.Game && c.Character != null && !chars.Contains(c.Character)) chars.Add(c.Character);
            if (oldChar != null && !chars.Contains(oldChar)) _store.Settings.Character = null;
            _updating++;
            RebuildCharBox();
            _updating = Math.Max(0, _updating - 1);
            _store.SaveSettings();
            _overlay.RefreshAll();
            RebuildList();
            PreviewRefresh();
            Status("已切换游戏：" + _store.GameName(_store.Settings.Game));
        }

        void ApplyCharacter()
        {
            _store.Settings.Character = ReadCharacter();
            _store.SaveSettings();
            _overlay.RefreshAll();
            RebuildList();
            PreviewRefresh();
            Status("已切换角色：" + (_store.Settings.Character ?? "全角色通用"));
        }

        void DisplayChanged(object sender, RoutedEventArgs e)
        {
            if (_updating > 0) return;
            _store.Settings.DisplayMode = _rbArrows.IsChecked == true ? "arrows" : (_rbDigits.IsChecked == true ? "digits" : "text");
            _store.Settings.ButtonStyle = _rbMnemo.IsChecked == true ? "mnemonic" : "original";
            _store.SaveSettings();
            _overlay.RefreshAll();
            PreviewRefresh();
        }

        void StyleChanged(object sender, RoutedEventArgs e)
        {
            if (_updating > 0) return;
            _store.Settings.Theme = _rbThemeC.IsChecked == true ? "C" : "A";
            _store.SaveSettings();
            _overlay.ApplyStyle();
        }

        void LockBoxChanged(object sender, RoutedEventArgs e)
        {
            if (_updating > 0) return;
            _overlay.SetLocked(!(_lockBox.IsChecked == true));
            Status(_overlay.Locked ? "面板已锁定（鼠标穿透、不可移动）" : "面板已解锁（可拖动）");
        }

        void ToggleOverlay()
        {
            _overlay.SetVisible(!_overlay.IsShown);
            UpdateOverlayBtn();
            Status(_overlay.IsShown ? "悬浮窗已打开" : "悬浮窗已隐藏");
        }

        public void UpdateOverlayBtn()
        {
            _overlayBtn.Content = _overlay.IsShown ? "隐藏悬浮窗" : "打开悬浮窗";
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            // 锁头图标等外部途径可能改过设置，回到管理器时同步一遍
            _updating++;
            SetRadiosFromSettings();
            _updating = Math.Max(0, _updating - 1);
            UpdateOverlayBtn();
        }

        List<ComboItem> CombosForContext()
        {
            var s = _store.Settings;
            return _store.Combos.FindAll(c =>
                c.Game == s.Game &&
                (s.Character == null ? c.Character == null : (c.Character == null || c.Character == s.Character)));
        }

        void RebuildList()
        {
            _listHost.Children.Clear();
            var list = CombosForContext();
            if (list.Count == 0)
            {
                _listHost.Children.Add(new TextBlock { Text = "（还没有连招——粘贴连招信息后点「保存连招」）", FontSize = 12, Foreground = Dim, Margin = new Thickness(2, 6, 2, 6) });
                return;
            }
            foreach (var c in list)
                _listHost.Children.Add(ComboRow(c));
        }

        UIElement ComboRow(ComboItem c)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var show = new CheckBox { IsChecked = c.Show, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            show.ToolTip = new ToolTip { Content = "勾选后显示在悬浮窗" };
            show.Checked += (s, e) => { c.Show = true; _store.SaveAll(); _overlay.RefreshAll(); };
            show.Unchecked += (s, e) => { c.Show = false; _store.SaveAll(); _overlay.RefreshAll(); };
            Grid.SetColumn(show, 0);

            var info = new StackPanel { Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand };
            info.Children.Add(new TextBlock
            {
                Text = (c.Name.Length > 0 ? c.Name : "(未命名)") + (c.Character == null ? "　· 通用" : ""),
                FontWeight = FontWeights.Bold,
                FontSize = 12.5,
                Foreground = Ink
            });
            info.Children.Add(new TextBlock { Text = c.Raw, FontSize = 11.5, Foreground = Dim, TextTrimming = TextTrimming.CharacterEllipsis });
            info.MouseLeftButtonDown += (s, e) => EditCombo(c);
            Grid.SetColumn(info, 1);

            var edit = Btn("编辑", (s, e) => EditCombo(c), Brushes.White, Ink, 52);
            Grid.SetColumn(edit, 2);
            var del = Btn("删除", (s, e) => DeleteCombo(c), Brushes.White, new SolidColorBrush(Color.FromRgb(0xC8, 0x3B, 0x3B)), 52, 6);
            Grid.SetColumn(del, 3);

            g.Children.Add(show); g.Children.Add(info); g.Children.Add(edit); g.Children.Add(del);
            return g;
        }

        void EditCombo(ComboItem c)
        {
            _updating++;
            _editingId = c.Id;
            _rawBox.Text = c.Raw;
            _nameBox.Text = c.Name;
            _notesBox.Text = c.Notes;
            _updating = Math.Max(0, _updating - 1);
            PreviewRefresh();
            Status("正在编辑：" + (c.Name.Length > 0 ? c.Name : c.Raw) + "（保存后生效）");
        }

        void SaveEditor(object sender, RoutedEventArgs e)
        {
            var raw = _rawBox.Text.Trim();
            if (raw.Length == 0) { Status("请先输入连招信息"); return; }
            ComboItem c;
            bool wasEdit = _editingId != null;
            if (wasEdit)
            {
                c = _store.Combos.Find(x => x.Id == _editingId);
                if (c == null) { c = new ComboItem { Id = Guid.NewGuid().ToString("N") }; _store.Combos.Add(c); }
            }
            else
            {
                c = new ComboItem { Id = Guid.NewGuid().ToString("N"), Show = true };
                _store.Combos.Add(c);
            }
            var title = _nameBox.Text.Trim();
            if (title.Length == 0) title = "Combo " + _store.Combos.Count;
            c.Game = _store.Settings.Game;
            c.Character = _store.Settings.Character;
            c.Raw = raw;
            c.Name = title;
            c.Condition = "";
            c.Position = "—";
            c.Notes = _notesBox.Text.Trim();
            c.Tokens = Engine.ParseCombo(c.Raw, _store.ButtonsOf(c.Game));
            _store.SaveAll();
            _overlay.RefreshAll();
            RebuildCharBox();
            RebuildList();
            if (!wasEdit) ClearEditor(null, null);
            Status("已保存连招：" + c.Name);
        }

        void ClearEditor(object sender, RoutedEventArgs e)
        {
            _updating++;
            _editingId = null;
            _rawBox.Text = "";
            _nameBox.Text = "";
            _notesBox.Text = "";
            _updating = Math.Max(0, _updating - 1);
            PreviewRefresh();
        }

        void DeleteCombo(ComboItem c)
        {
            var r = MessageBox.Show(this, "确定删除连招「" + (c.Name.Length > 0 ? c.Name : c.Raw) + "」吗？", "删除确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            _store.Combos.Remove(c);
            if (_editingId == c.Id) ClearEditor(null, null);
            _store.SaveAll();
            _overlay.RefreshAll();
            RebuildList();
            Status("已删除：" + (c.Name.Length > 0 ? c.Name : c.Raw));
        }

        void ReloadDataFiles(object sender, RoutedEventArgs e)
        {
            _store.Reload();
            _updating++;
            SyncContextToUi();
            RebuildList();
            _updating = Math.Max(0, _updating - 1);
            PreviewRefresh();
            _overlay.RefreshAll();
            Status("已重新载入 data 下的数据文件");
        }

        void PreviewRefresh()
        {
            _previewHost.Children.Clear();
            var raw = _rawBox.Text.Trim();
            if (raw.Length == 0)
            {
                _previewHost.Children.Add(new TextBlock
                {
                    Text = "输入连招信息后此处实时预览（示例：cr.B, cr.A, df+D(1), qcf+D, rdp+D）",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
                    FontFamily = new FontFamily("Microsoft YaHei UI")
                });
                return;
            }
            var tokens = Engine.ParseCombo(raw, _store.ButtonsOf(_store.Settings.Game));
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            ComboRenderer.BuildInto(row, tokens, _store.Settings.DisplayMode, _store.Settings.ButtonStyle,
                new ThemeColors
                {
                    Arrow = new SolidColorBrush(Color.FromRgb(238, 240, 245)),
                    Sep = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255))
                });
            _previewHost.Children.Add(row);
        }

        void OpenDataFolder(object sender, RoutedEventArgs e)
        {
            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            try { Process.Start("explorer.exe", "\"" + dir + "\""); } catch { }
        }

        void Status(string msg)
        {
            _status.Text = "已自动保存 " + DateTime.Now.ToString("HH:mm:ss") + " · " + msg;
        }

        // ---------------- 截图（开发验证用） ----------------

        void DoShot()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(_shotPath);
                var name = System.IO.Path.GetFileNameWithoutExtension(_shotPath);
                ShootTab(System.IO.Path.Combine(dir, name + ".png"), 0);
                ShootTab(System.IO.Path.Combine(dir, name + "-show.png"), 1);
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

        void ShootTab(string path, int tabIndex)
        {
            _tabs.SelectedIndex = tabIndex;
            var root = (FrameworkElement)Content;
            root.UpdateLayout();
            var w = (int)Math.Ceiling(root.ActualWidth);
            var h = (int)Math.Ceiling(root.ActualHeight);
            var rtb = new RenderTargetBitmap(Math.Max(w, 1), Math.Max(h, 1), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(path)) enc.Save(fs);
        }

        protected override void OnClosed(EventArgs e)
        {
            _store.SaveAll();
            // 管理窗口与悬浮窗关联：关闭管理窗口 = 退出整个程序（悬浮窗随之关闭）
            if (Application.Current != null)
                Application.Current.Shutdown();
            base.OnClosed(e);
        }
    }
}
