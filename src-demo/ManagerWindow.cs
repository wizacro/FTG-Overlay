// FTG-Overlay —— 管理器窗口（现代深色侧边栏 + 浅色卡片内容区）
// 「导入」= 选游戏/角色 + 录入连招（必填）+ 标题/备注（可选，留空自动命名 Combo x）
// 「展示」= 连招列表（勾选显示）+ 悬浮窗开关 + 选项（默认折叠）
// 文字说明集中收纳：右上角「?」可随时打开使用说明；首次运行自动弹出一次
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
        RadioButton _rbArrows, _rbDigits, _rbText, _rbMnemo, _rbOrig, _rbThemeA, _rbThemeC;
        CheckBox _lockBox;
        Slider _alphaSlider, _scaleSlider;
        Button _overlayBtn;
        TextBlock _status, _pageTitle;
        Border _navImport, _navShow;
        Grid _pageImport, _pageShow;
        string _editingId;
        int _updating;   // >0 = 正在程序化更新界面（可嵌套），屏蔽事件回写

        // ---- 视觉常量（现代 agent 风：深色侧栏 + 浅色卡片 + 蓝紫强调色）----
        static readonly Color CSidebar = Color.FromRgb(0x1B, 0x1E, 0x27);
        static readonly Color CNavActive = Color.FromRgb(0x30, 0x36, 0x4B);
        static readonly Color CPageBg = Color.FromRgb(0xF5, 0xF6, 0xF8);
        static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x26, 0x29, 0x32));
        static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x9B));
        static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x5B, 0x7C, 0xFA));
        static readonly Brush AccentDim = new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xFD));
        static readonly Brush CardBg = Brushes.White;
        static readonly Brush CardBorder = new SolidColorBrush(Color.FromRgb(0xE4, 0xE7, 0xEC));

        public ManagerWindow(Store store, OverlayWindow overlay, string shotPath)
        {
            _store = store;
            _overlay = overlay;
            _shotPath = shotPath;
            _updating++;   // 构建界面期间屏蔽所有变更事件

            Title = "FTG-Overlay 管理器";
            Width = 820;
            Height = 620;
            MinWidth = 700;
            MinHeight = 540;
            Background = new SolidColorBrush(CPageBg);
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");

            Content = BuildUi();
            SyncContextToUi();
            RebuildList();
            PreviewRefresh();
            _overlay.LockStateChanged += v =>
            {
                if (_updating > 0) return;
                _updating++;
                _lockBox.IsChecked = !v;
                _updating = Math.Max(0, _updating - 1);
            };
            _updating = Math.Max(0, _updating - 1);

            // 首次运行：自动弹出一次使用说明，并告知以后在哪里看
            if (_store.Settings.FirstRun)
            {
                _store.Settings.FirstRun = false;
                _store.SaveSettings();
                Loaded += (s, e) => { ShowHelp(); Status("首次使用：说明已弹出，右上角「?」可随时查看"); };
            }

            if (_shotPath != null)
                SourceInitialized += (s, e) => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(DoShot));
        }

        // ---------------- 界面构建 ----------------

        UIElement BuildUi()
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // ---- 左侧深色导航栏 ----
            var sidebar = new Border { Background = new SolidColorBrush(CSidebar) };
            var dock = new DockPanel();
            var navStack = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            navStack.Children.Add(new TextBlock
            {
                Text = "FTG-Overlay",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(18, 0, 0, 1)
            });
            navStack.Children.Add(new TextBlock
            {
                Text = "格斗连段小抄",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x76, 0x7C, 0x90)),
                Margin = new Thickness(18, 0, 0, 16)
            });
            _navImport = NavItem("导入连段", true);
            _navShow = NavItem("悬浮展示", false);
            navStack.Children.Add(_navImport);
            navStack.Children.Add(_navShow);
            dock.Children.Add(navStack);

            var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 12) };
            var help = NavItem("?  使用说明", false);
            help.MouseLeftButtonDown += (s, e) => ShowHelp();
            bottom.Children.Add(help);
            bottom.Children.Add(new TextBlock
            {
                Text = "v0.1.0 demo",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x56, 0x5B, 0x6B)),
                Margin = new Thickness(18, 8, 0, 0)
            });
            DockPanel.SetDock(bottom, Dock.Bottom);
            dock.Children.Add(bottom);
            sidebar.Child = dock;
            Grid.SetColumn(sidebar, 0);
            root.Children.Add(sidebar);

            // ---- 右侧内容区 ----
            var content = new Grid { Background = new SolidColorBrush(CPageBg) };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Grid { Margin = new Thickness(22, 16, 18, 6) };
            _pageTitle = new TextBlock { Text = "导入连段", FontSize = 19, FontWeight = FontWeights.Bold, Foreground = Ink };
            var helpBtn = HelpButton();
            header.Children.Add(_pageTitle);
            header.Children.Add(helpBtn);
            Grid.SetRow(header, 0);
            content.Children.Add(header);

            var pageHost = new Grid { Margin = new Thickness(22, 4, 18, 12) };
            _pageImport = BuildImportPage();
            _pageShow = BuildShowPage();
            pageHost.Children.Add(_pageImport);
            pageHost.Children.Add(_pageShow);
            Grid.SetRow(pageHost, 1);
            content.Children.Add(pageHost);

            var footer = new Border { Background = new SolidColorBrush(Color.FromRgb(0xEC, 0xEE, 0xF2)), Padding = new Thickness(22, 5, 18, 5) };
            _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 11.5, Foreground = Dim };
            var spacer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            spacer.Children.Add(Btn2("重载数据文件", ReloadDataFiles));
            var folderBtn = Btn2("打开数据文件夹", OpenDataFolder);
            folderBtn.Margin = new Thickness(6, 0, 0, 0);
            spacer.Children.Add(folderBtn);
            var saveAllBtn = Btn2("保存全部", (s, e) => { _store.SaveAll(); Status("已保存全部数据"); });
            saveAllBtn.Margin = new Thickness(6, 0, 0, 0);
            spacer.Children.Add(saveAllBtn);
            var footGrid = new Grid();
            footGrid.Children.Add(_status);
            footGrid.Children.Add(spacer);
            footer.Child = footGrid;
            Grid.SetRow(footer, 2);
            content.Children.Add(footer);
            Grid.SetColumn(content, 1);
            root.Children.Add(content);

            return root;
        }

        Border NavItem(string text, bool active)
        {
            var b = new Border
            {
                Height = 38,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(10, 2, 10, 2),
                Background = active ? new SolidColorBrush(CNavActive) : Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            b.Child = new TextBlock
            {
                Text = text,
                FontSize = 13.5,
                FontWeight = active ? FontWeights.Bold : FontWeights.Normal,
                Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xB4)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            return b;
        }

        Button HelpButton()
        {
            var b = new Button
            {
                Content = "?",
                Width = 28,
                Height = 28,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = Accent,
                Background = AccentDim,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "使用说明（记法写法 / 悬浮窗操作 / 数据说明）"
            };
            b.Click += (s, e) => ShowHelp();
            return b;
        }

        void ActivateNav(int page)
        {
            var activeBg = new SolidColorBrush(CNavActive);
            var transparent = Brushes.Transparent;
            _navImport.Background = page == 0 ? activeBg : transparent;
            _navShow.Background = page == 1 ? activeBg : transparent;
            _pageImport.Visibility = page == 0 ? Visibility.Visible : Visibility.Collapsed;
            _pageShow.Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;
            _pageTitle.Text = page == 0 ? "导入连段" : "悬浮展示";
        }

        // ---- 卡片 / 控件辅助 ----

        Border Card(string title, params UIElement[] children)
        {
            var sp = new StackPanel();
            if (title != null)
                sp.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Ink, Margin = new Thickness(0, 0, 0, 8) });
            foreach (var c in children) sp.Children.Add(c);
            return new Border
            {
                Background = CardBg,
                BorderBrush = CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 12, 16, 14),
                Margin = new Thickness(0, 0, 0, 12),
                Child = sp,
                Effect = null
            };
        }

        TextBlock FieldLabel(string text)
        {
            return new TextBlock { Text = text, FontSize = 11.5, Foreground = Dim, Margin = new Thickness(0, 8, 0, 4) };
        }

        Button Btn(string text, RoutedEventHandler onClick, Brush bg, Brush fg, double width, double ml = 0)
        {
            var b = new Button
            {
                Content = text,
                Width = width,
                Height = 30,
                Background = bg,
                Foreground = fg,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(ml, 0, 0, 0),
                Cursor = Cursors.Hand,
                Template = RoundedButtonTemplate()
            };
            b.Click += onClick;
            return b;
        }

        ControlTemplate RoundedButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var f = new FrameworkElementFactory(typeof(Border));
            f.Name = "bd";
            f.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            f.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            f.AppendChild(cp);
            var trig = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            trig.Setters.Add(new Setter(Border.OpacityProperty, 0.85, "bd"));
            template.Triggers.Add(trig);
            var trig2 = new Trigger { Property = Button.IsPressedProperty, Value = true };
            trig2.Setters.Add(new Setter(Border.OpacityProperty, 0.7, "bd"));
            template.Triggers.Add(trig2);
            template.VisualTree = f;
            return template;
        }

        TextBox RoundTextBox(double height, bool multiline)
        {
            var tb = new TextBox
            {
                Height = height,
                FontSize = 13,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xDC, 0xE4)),
                Background = Brushes.White,
                VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center,
                TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap
            };
            var template = new ControlTemplate(typeof(TextBox));
            var f = new FrameworkElementFactory(typeof(Border));
            f.Name = "bd";
            f.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            f.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(TextBox.BackgroundProperty));
            f.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(TextBox.BorderBrushProperty));
            f.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(TextBox.BorderThicknessProperty));
            var sv = new FrameworkElementFactory(typeof(ScrollViewer));
            sv.Name = "PART_ContentEditor";
            sv.SetValue(ScrollViewer.MarginProperty, new Thickness(8, 4, 8, 4));
            f.AppendChild(sv);
            template.VisualTree = f;
            tb.Template = template;
            return tb;
        }

        UIElement PreviewPanel()
        {
            _previewBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 0x0A, 0x0B, 0x0E)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                MinHeight = 44
            };
            _previewHost = new StackPanel();
            _previewBorder.Child = _previewHost;
            return _previewBorder;
        }

        Border _previewBorder;

        RadioButton Radio(string text, string group)
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

        StackPanel Row(string label, UIElement control)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            sp.Children.Add(new TextBlock { Text = label, Width = 76, VerticalAlignment = VerticalAlignment.Center, Foreground = Ink, FontSize = 12.5 });
            sp.Children.Add(control);
            return sp;
        }

        // ---------------- 页面 ----------------

        Grid BuildImportPage()
        {
            var g = new Grid();
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var sp = new StackPanel();

            sp.Children.Add(Card("选择对局", CtxRow()));
            sp.Children.Add(Card("录入连招",
                FieldLabel("连招信息（必填，支持 DreamCancel / 数字 / 2002UM 攻略记法，详见右上角 ?）"),
                RawBox(),
                FieldLabel("预览"),
                PreviewPanel(),
                FieldLabel("标题（可留空，自动命名 Combo x）"),
                TitleBox(),
                FieldLabel("备注（可选）"),
                NotesBox(),
                SaveRow()));

            scroll.Content = sp;
            g.Children.Add(scroll);
            return g;
        }

        UIElement CtxRow()
        {
            var g = new Grid { Margin = new Thickness(0, 0, 12, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = new StackPanel();
            left.Children.Add(FieldLabel("游戏"));
            _gameBox = new ComboBox { Height = 30 };
            _gameBox.SelectionChanged += (s, e) => { if (_updating == 0) ApplyGame(); };
            left.Children.Add(_gameBox);
            var right = new StackPanel();
            right.Children.Add(FieldLabel("角色"));
            _charBox = new ComboBox { Height = 30, IsEditable = true };
            _charBox.SelectionChanged += (s, e) => { if (_updating == 0) ApplyCharacter(); };
            _charBox.LostFocus += (s, e) => { if (_updating == 0) ApplyCharacter(); };
            _charBox.KeyDown += (s, e) => { if (e.Key == Key.Enter && _updating == 0) ApplyCharacter(); };
            right.Children.Add(_charBox);
            Grid.SetColumn(left, 0); Grid.SetColumn(right, 1);
            g.Children.Add(left); g.Children.Add(right);
            return g;
        }

        TextBox RawBox()
        {
            _rawBox = RoundTextBox(60, true);
            _rawBox.TextChanged += (s, e) => { if (_updating == 0) PreviewRefresh(); };
            return _rawBox;
        }

        TextBox TitleBox()
        {
            _nameBox = RoundTextBox(30, false);
            return _nameBox;
        }

        TextBox NotesBox()
        {
            _notesBox = RoundTextBox(30, false);
            return _notesBox;
        }

        StackPanel SaveRow()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            sp.Children.Add(Btn("保存连招", SaveEditor, Accent, Brushes.White, 110));
            sp.Children.Add(Btn("清空", ClearEditor, new SolidColorBrush(Color.FromRgb(0xEE, 0xF0, 0xF4)), Ink, 76, 8));
            return sp;
        }

        Grid BuildShowPage()
        {
            var g = new Grid();
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var sp = new StackPanel();

            // 连招列表卡片
            _listHost = new StackPanel();
            var listCard = Card("要显示的连招", _listHost);
            sp.Children.Add(listCard);

            // 悬浮窗卡片
            _overlayBtn = new Button
            {
                Content = "打开悬浮窗",
                Height = 32,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Left,
                Template = RoundedButtonTemplate()
            };
            _overlayBtn.Click += (s, e) => ToggleOverlay();
            _lockBox = new CheckBox
            {
                Content = "允许拖动面板（不勾选 = 锁定：鼠标穿透、不可移动）",
                FontSize = 12.5,
                Foreground = Ink,
                Margin = new Thickness(0, 8, 0, 0)
            };
            _lockBox.Checked += LockBoxChanged;
            _lockBox.Unchecked += LockBoxChanged;
            sp.Children.Add(Card("悬浮窗", _overlayBtn, _lockBox));

            // 选项（默认折叠）
            _rbThemeA = Radio("A 简约深色（默认）", "theme");
            _rbThemeC = Radio("C 霓虹街机", "theme");
            _rbThemeA.Checked += StyleChanged; _rbThemeC.Checked += StyleChanged;
            _alphaSlider = new Slider { Minimum = 15, Maximum = 95, Value = _store.Settings.Alpha * 100, TickFrequency = 5, IsSnapToTickEnabled = false, Width = 240 };
            _alphaSlider.ValueChanged += (s, e) => { if (_updating == 0) { _store.Settings.Alpha = _alphaSlider.Value / 100.0; _overlay.ApplyStyle(); _store.SaveSettings(); } };
            _scaleSlider = new Slider { Minimum = 0.7, Maximum = 1.8, Value = _store.Settings.Scale, TickFrequency = 0.05, Width = 240 };
            _scaleSlider.ValueChanged += (s, e) => { if (_updating == 0) { _store.Settings.Scale = _scaleSlider.Value; _overlay.ApplyStyle(); _store.SaveSettings(); } };
            _rbArrows = Radio("图形化箭头（推荐）", "displayMode");
            _rbDigits = Radio("数字写法", "displayMode");
            _rbText = Radio("文本原样", "displayMode");
            _rbArrows.Checked += DisplayChanged; _rbDigits.Checked += DisplayChanged; _rbText.Checked += DisplayChanged;
            _rbMnemo = Radio("拳脚名 LP / LK / HP / HK", "buttonStyle");
            _rbOrig = Radio("原按钮名 A / B / C / D", "buttonStyle");
            _rbMnemo.Checked += DisplayChanged; _rbOrig.Checked += DisplayChanged;

            var optionsInner = new StackPanel { Margin = new Thickness(2, 2, 0, 2) };
            optionsInner.Children.Add(Group("主题风格", _rbThemeA, _rbThemeC));
            optionsInner.Children.Add(Row("透明度", _alphaSlider));
            optionsInner.Children.Add(Row("大小", _scaleSlider));
            optionsInner.Children.Add(Group("连招显示形式", _rbArrows, _rbDigits, _rbText));
            optionsInner.Children.Add(Group("按钮显示名", _rbMnemo, _rbOrig));
            sp.Children.Add(CollapsedCard("选项（锁定 / 主题 / 透明度 / 大小 / 表现形式）", optionsInner));
            sp.Children.Add(CollapsedCard("记法与热键速查", new TextBlock
            {
                Text = "· 记法：cr.=蹲  st.=站  c5C=近立5C  j.=跳  qcf=236  rdp=421  hcb=63214\n" +
                       "· 符号：xx / xxx = 取消　• = 派生键　(N) = 帧数或段数\n" +
                       "· 热键：全局仅 Ctrl+Alt+Q（退出）；其余操作都在本窗口。\n" +
                       "· 游戏内用「无边框全屏」或窗口模式；本工具不读内存、不注入、不模拟按键。",
                FontSize = 11.5,
                Foreground = Dim,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 2, 0, 2)
            }));

            scroll.Content = sp;
            g.Children.Add(scroll);
            return g;
        }

        Expander CollapsedCard(string header, UIElement content)
        {
            return new Expander
            {
                Header = header,
                IsExpanded = false,
                Foreground = Ink,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Background = CardBg,
                Margin = new Thickness(0, 0, 0, 12),
                Content = new Border
                {
                    Background = CardBg,
                    Child = content
                }
            };
        }

        UIElement Group(string title, params UIElement[] items)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
            sp.Children.Add(new TextBlock { Text = title, FontSize = 12, Foreground = Dim, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 2) });
            foreach (var i in items) sp.Children.Add(i);
            return sp;
        }

        Button Btn2(string text, RoutedEventHandler onClick)
        {
            var b = new Button
            {
                Content = text,
                Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF0, 0xF4)),
                Foreground = Ink,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Template = RoundedButtonTemplate()
            };
            b.Click += onClick;
            return b;
        }

        // ---------------- 使用说明（帮助窗口） ----------------

        void ShowHelp()
        {
            var w = new Window
            {
                Title = "FTG-Overlay 使用说明",
                Width = 600,
                Height = 620,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(CPageBg),
                Owner = this
            };
            var sp = new StackPanel { Margin = new Thickness(22, 16, 22, 16) };
            sp.Children.Add(HelpH("快速上手"));
            sp.Children.Add(HelpB("1. 「导入」页选好游戏与角色，粘贴连段信息，点「保存连招」。\n2. 「展示」页勾选要显示的连招，点「打开悬浮窗」。\n3. 游戏内使用「无边框全屏」或窗口模式（独占全屏下悬浮窗会被画面覆盖）。"));
            sp.Children.Add(HelpH("连招信息写法（几种记法可混用）"));
            sp.Children.Add(HelpB("· DreamCancel / KOF 字母风：\n   cr.B, cr.A, df+D(1), qcf+D, rdp+D\n   cr.=蹲  st.=站  cl./c=近立  f=远立  j.=跳\n   qcf=236  qcb=214  dp=623/626  rdp=421  hcf=4126/41236  hcb=63214"));
            sp.Children.Add(HelpB("· 数字风（numpad）：\n   2B, 2A, 3D(1), 26D, 421D\n   数字即小键盘方向（5=中立站立，不画箭头）"));
            sp.Children.Add(HelpB("· 2002UM 攻略风（本站新增支持）：\n   c5C xx 3D xx 63214B+C xx 236D•D, 421D\n   c5C=近立5C  •=同一必杀的派生键  xxx=超杀取消  (N)=帧数/段数标注"));
            sp.Children.Add(HelpB("· 符号约定：xx / xxx = 取消　> = 连携　,(逗号) = 衔接　(1) = 目押帧或段数\n· 无法识别的记号会原样显示并标黄，绝不丢失。"));
            sp.Children.Add(HelpH("悬浮窗操作"));
            sp.Children.Add(HelpB("· 锁定 = 鼠标穿透、不可移动；点击右侧小锁头图标（任何状态下都可点击、不抢游戏焦点）切换锁定，或在「展示」页勾选。\n· 解锁后直接拖动面板；锁定状态、位置、透明度都会自动记住。\n· 面板上不显示任何文字说明，保持画面干净。"));
            sp.Children.Add(HelpH("数据与安全"));
            sp.Children.Add(HelpB("· 连段库存于 data\\combos.json（记事本可编辑，改完点底部「重载数据文件」）。\n· 本工具不读游戏内存、不注入进程、不模拟按键；离线可用、无遥测。MIT 开源。"));
            w.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
            w.Show();
        }

        TextBlock HelpH(string t)
        {
            return new TextBlock { Text = t, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Ink, Margin = new Thickness(0, 12, 0, 5) };
        }

        TextBlock HelpB(string t)
        {
            return new TextBlock { Text = t, FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(0x3A, 0x3E, 0x48)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
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
        }

        void ApplyCharacter()
        {
            _store.Settings.Character = ReadCharacter();
            _store.SaveSettings();
            _overlay.RefreshAll();
            RebuildList();
            PreviewRefresh();
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
        }

        void ToggleOverlay()
        {
            _overlay.SetVisible(!_overlay.IsShown);
            UpdateOverlayBtn();
        }

        public void UpdateOverlayBtn()
        {
            _overlayBtn.Content = _overlay.IsShown ? "隐藏悬浮窗" : "打开悬浮窗";
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
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
                _listHost.Children.Add(new TextBlock { Text = "（该角色下还没有连招——去「导入」页添加）", FontSize = 12, Foreground = Dim, Margin = new Thickness(2, 6, 2, 6) });
                return;
            }
            foreach (var c in list)
                _listHost.Children.Add(ComboRow(c));
        }

        UIElement ComboRow(ComboItem c)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var show = new CheckBox { IsChecked = c.Show, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
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

            var edit = Btn2("编辑", (s, e) => EditCombo(c));
            Grid.SetColumn(edit, 2);
            var del = Btn2("删除", (s, e) => DeleteCombo(c));
            del.Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0x3B, 0x3B));
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
            ActivateNav(0);
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
            Status("已保存：" + c.Name + "（到「展示」页勾选显示）");
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
            var r = MessageBox.Show(this, "确定删除「" + (c.Name.Length > 0 ? c.Name : c.Raw) + "」吗？", "删除确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            _store.Combos.Remove(c);
            if (_editingId == c.Id) ClearEditor(null, null);
            _store.SaveAll();
            _overlay.RefreshAll();
            RebuildList();
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
                    Text = "输入连招后实时预览　示例：cr.B, cr.A, df+D(1), qcf+D, rdp+D",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
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
                ActivateNav(0);
                Shoot(_shotPath);
                ActivateNav(1);
                Shoot(System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(_shotPath),
                    System.IO.Path.GetFileNameWithoutExtension(_shotPath) + "-show.png"));
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

        void Shoot(string path)
        {
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
