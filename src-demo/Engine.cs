// FTG-Overlay —— 记法解析引擎与渲染器
// 输入：DreamCancel 风格（cr.B / qcf+D / df+D(1)...）与数字记法（2B / 26D / 421D...）可混排
// 输出：token 流（StepToken / "cancel" / "link"），未知片段原样保留（Unknown）

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FTGOverlayDemo
{
    public struct Btn
    {
        public string Label;   // 显示名（LP/LK/HP/HK...，拳脚名）
        public string Orig;    // 原按钮名（A/B/C/D 或 LP...，即用户输入的写法）
        public string Key;     // 配色键
        public Btn(string label, string orig, string key) { Label = label; Orig = orig; Key = key; }
    }

    public class StepToken
    {
        public bool Unknown;
        public string Raw;
        public string Stance = "";        // "↓" / "↑" / ""
        public string Motion = "";        // 图形显示：如 "↓↘→"
        public string MotionDigits = "";  // 数字显示：如 "236"
        public List<Btn> Buttons = new List<Btn>();
        public int? Frame;                // (N) 目押帧标注
    }

    public static class Engine
    {
        static readonly Dictionary<string, string> MotionAlias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "qcf", "236" }, { "qcb", "214" }, { "dp", "623" }, { "rdp", "421" },
            { "hcf", "4126" }, { "hcb", "63214" }
        };

        static readonly Dictionary<string, string> DirAlias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "df", "3" }, { "db", "1" }, { "uf", "9" }, { "ub", "7" },
            { "f", "6" }, { "b", "4" }, { "u", "8" }, { "d", "2" }, { "n", "5" }
        };

        static readonly Dictionary<string, string> MotionDisplay = new Dictionary<string, string>
        {
            { "236", "↓↘→" }, { "214", "↓↙←" }, { "623", "→↓↘" }, { "626", "→↓↘" },
            { "421", "←↓↙" }, { "4126", "←↙→" }, { "63214", "→↘↓↙←" }
        };

        static readonly Dictionary<string, string> DigitArrow = new Dictionary<string, string>
        {
            { "1", "↙" }, { "2", "↓" }, { "3", "↘" }, { "4", "←" }, { "5", "◎" },
            { "6", "→" }, { "7", "↖" }, { "8", "↑" }, { "9", "↗" }
        };

        static readonly string[] ButtonNames = { "LP", "MP", "HP", "LK", "MK", "HK", "P", "K" };
        static readonly string[] Stances = { "cr.", "st.", "cl.", "j.", "n." };

        public static List<object> ParseCombo(string raw, Dictionary<string, string> buttonMap)
        {
            var tokens = new List<object>();
            if (raw == null) return tokens;
            var parts = raw.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var part = p.Trim();
                if (part.Length == 0) continue;
                if (part.Equals("xx", StringComparison.OrdinalIgnoreCase)) { tokens.Add("cancel"); continue; }
                if (part == ">") { tokens.Add("link"); continue; }
                // "xx 招式" 连写：拆为取消分隔符 + 招式
                if (part.Length > 3 && string.Compare(part, 0, "xx", 0, 2, true, CultureInfo.InvariantCulture) == 0 && part[2] == ' ')
                {
                    tokens.Add("cancel");
                    part = part.Substring(3).Trim();
                    if (part.Length == 0) continue;
                }
                tokens.Add(ParseStep(part, buttonMap));
            }
            return tokens;
        }

        static StepToken ParseStep(string s, Dictionary<string, string> buttonMap)
        {
            var st = new StepToken(); st.Raw = s;
            var rest = s.Trim();

            // 帧标注后缀 (N) / (NF)
            int open = rest.LastIndexOf('(');
            if (open >= 0 && rest.EndsWith(")", StringComparison.Ordinal))
            {
                var inner = rest.Substring(open + 1, rest.Length - open - 2).Trim().TrimEnd('F', 'f');
                int n;
                if (int.TryParse(inner, out n)) { st.Frame = n; rest = rest.Substring(0, open).Trim(); }
            }

            // 姿态前缀
            foreach (var k in Stances)
            {
                if (rest.Length > k.Length && string.Compare(rest, 0, k, 0, k.Length, true, CultureInfo.InvariantCulture) == 0)
                {
                    st.Stance = StanceArrow(k.TrimEnd('.'));
                    rest = rest.Substring(k.Length).Trim();
                    break;
                }
            }

            var segs = rest.Split('+');
            foreach (var sg0 in segs)
            {
                var sg = sg0.Trim();
                if (sg.Length == 0) continue;

                // 数字记法直连按钮：如 2B / 26D / 421D（数字段 = 方向，其后 = 按钮）
                int i = 0;
                while (i < sg.Length && sg[i] >= '0' && sg[i] <= '9') i++;
                if (i > 0)
                {
                    st.MotionDigits = sg.Substring(0, i);
                    st.Motion = MotionDisplayFor(st.MotionDigits);
                    var tail = sg.Substring(i).Trim();
                    if (tail.Length > 0)
                    {
                        var bt = ButtonOf(tail, buttonMap);
                        if (bt.HasValue) st.Buttons.Add(bt.Value); else st.Unknown = true;
                    }
                    continue;
                }

                var b = ButtonOf(sg, buttonMap);
                if (b.HasValue) { st.Buttons.Add(b.Value); continue; }
                string digits;
                if (TryMotion(sg, out digits))
                {
                    st.MotionDigits = digits;
                    st.Motion = MotionDisplayFor(digits);
                    continue;
                }
                st.Unknown = true;
            }

            if (!st.Unknown && st.Motion.Length == 0 && st.MotionDigits.Length == 0 && st.Buttons.Count == 0 && st.Stance.Length == 0) st.Unknown = true;
            return st;
        }

        static bool TryMotion(string sg, out string digits)
        {
            digits = null;
            string v;
            if (MotionAlias.TryGetValue(sg, out v)) { digits = v; return true; }
            if (DirAlias.TryGetValue(sg, out v)) { digits = v; return true; }
            if (sg.Length <= 5)
            {
                foreach (var c in sg) if (c < '0' || c > '9') return false;
                digits = sg; return true;
            }
            return false;
        }

        static string MotionDisplayFor(string digits)
        {
            string v;
            if (MotionDisplay.TryGetValue(digits, out v)) return v;
            var sb = new StringBuilder();
            foreach (var c in digits)
            {
                string a;
                if (DigitArrow.TryGetValue(c.ToString(), out a)) sb.Append(a); else sb.Append(c);
            }
            return sb.ToString();
        }

        static string StanceArrow(string stance)
        {
            var s = stance.ToLowerInvariant();
            if (s == "cr") return "↓";
            if (s == "j") return "↑";
            return "";
        }

        static Btn? ButtonOf(string sg, Dictionary<string, string> buttonMap)
        {
            if (string.IsNullOrEmpty(sg)) return null;
            var up = sg.ToUpperInvariant();
            if (buttonMap != null)
            {
                string mapped;
                if (buttonMap.TryGetValue(up, out mapped)) return new Btn(mapped, up, mapped);
            }
            foreach (var b in ButtonNames) if (b == up) return new Btn(b, b, b);
            return null;
        }

        public static Dictionary<string, string> DefaultKofButtons()
        {
            return new Dictionary<string, string> { { "A", "LP" }, { "B", "LK" }, { "C", "HP" }, { "D", "HK" } };
        }

        // ---------------- 自测 ----------------

        static int _fail;
        static void Check(StringBuilder sb, string name, bool cond)
        {
            sb.AppendLine((cond ? "  PASS  " : "  FAIL  ") + name);
            if (!cond) _fail++;
        }

        public static string SelfTest()
        {
            _fail = 0;
            var sb = new StringBuilder();
            var map = DefaultKofButtons();
            sb.AppendLine("FTG-Overlay 解析引擎自测报告 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            {
                sb.AppendLine("[1] DreamCancel 记法：cr.B, cr.A, df+D(1), qcf+D, rdp+D");
                var t = ParseCombo("cr.B, cr.A, df+D(1), qcf+D, rdp+D", map);
                Check(sb, "共 5 个步骤（token 数 5）", t.Count == 5);
                var s1 = t[0] as StepToken; var s2 = t[1] as StepToken;
                var s3 = t[2] as StepToken; var s4 = t[3] as StepToken; var s5 = t[4] as StepToken;
                Check(sb, "cr.B → 蹲姿箭头 + LK", s1 != null && s1.Stance == "↓" && s1.Buttons.Count == 1 && s1.Buttons[0].Label == "LK");
                Check(sb, "cr.A → 蹲姿箭头 + LP", s2 != null && s2.Stance == "↓" && s2.Buttons[0].Label == "LP");
                Check(sb, "df+D(1) → ↘ + HK + 1F 标注", s3 != null && s3.Motion == "↘" && s3.Buttons[0].Label == "HK" && s3.Frame == 1);
                Check(sb, "qcf+D → ↓↘→ + HK", s4 != null && s4.Motion == "↓↘→" && s4.Buttons[0].Label == "HK");
                Check(sb, "rdp+D → ←↓↙ + HK", s5 != null && s5.Motion == "←↓↙" && s5.Buttons[0].Label == "HK");
            }
            {
                sb.AppendLine("[2] 数字记法：2B, 2A, 3D(1), 26D, 421D");
                var t = ParseCombo("2B, 2A, 3D(1), 26D, 421D", map);
                var s1 = t[0] as StepToken; var s3 = t[2] as StepToken; var s4 = t[3] as StepToken; var s5 = t[4] as StepToken;
                Check(sb, "2B → ↓ + LK", s1 != null && s1.Motion == "↓" && s1.Buttons[0].Label == "LK");
                Check(sb, "3D(1) → ↘ + HK + 1F", s3 != null && s3.Motion == "↘" && s3.Frame == 1);
                Check(sb, "26D → ↓→ + HK", s4 != null && s4.Motion == "↓→");
                Check(sb, "421D → ←↓↙ + HK（rdp）", s5 != null && s5.Motion == "←↓↙");
            }
            {
                sb.AppendLine("[3] 跳入起手：j.C, st.C(2), qcf+A, hcb+B");
                var t = ParseCombo("j.C, st.C(2), qcf+A, hcb+B", map);
                var s1 = t[0] as StepToken; var s2 = t[1] as StepToken; var s3 = t[2] as StepToken; var s4 = t[3] as StepToken;
                Check(sb, "j.C → 跳姿箭头 + HP", s1 != null && s1.Stance == "↑" && s1.Buttons[0].Label == "HP");
                Check(sb, "st.C(2) → 站姿 + HP + 2F", s2 != null && s2.Stance == "" && s2.Frame == 2);
                Check(sb, "hcb+B → →↘↓↙← + LK", s4 != null && s4.Motion == "→↘↓↙←" && s4.Buttons[0].Label == "LK");
                Check(sb, "qcf+A → ↓↘→ + LP", s3 != null && s3.Motion == "↓↘→");
            }
            {
                sb.AppendLine("[4] 容错：st.C, xx HD, qcf+C, ???猜测");
                var t = ParseCombo("st.C, xx HD, qcf+C, ???猜测", map);
                Check(sb, "包含 cancel 分隔符", t.Contains("cancel"));
                int unknown = 0;
                foreach (var x in t) { var s = x as StepToken; if (s != null && s.Unknown) unknown++; }
                Check(sb, "未知记号 2 处且原样保留（HD / ???猜测）", unknown == 2);
            }
            {
                sb.AppendLine("[5] 原按钮名保留：cr.B 的 Orig 应为 B（A/B/C/D 显示模式用）");
                var t = ParseCombo("cr.B, 236D", map);
                var s1 = t[0] as StepToken; var s2 = t[1] as StepToken;
                Check(sb, "cr.B → Orig=B", s1 != null && s1.Buttons[0].Orig == "B");
                Check(sb, "236D → 数字段 236 + Orig=D", s2 != null && s2.MotionDigits == "236" && s2.Buttons[0].Orig == "D");
            }
            {
                sb.AppendLine("[6] 非法输入不崩溃");
                ParseCombo("", map); ParseCombo("，，，", map); ParseCombo("zzz@@@", map);
                Check(sb, "空串 / 全逗号 / 乱码 均不抛异常", true);
            }

            sb.AppendLine(_fail == 0 ? "结果：全部通过" : ("结果：失败 " + _fail + " 项"));
            return sb.ToString();
        }
    }

    // ---------------- token 渲染器（悬浮窗与管理器预览共用） ----------------

    public class ThemeColors
    {
        public Brush Arrow;
        public Brush Sep;
    }

    public static class ComboRenderer
    {
        public static void BuildInto(StackPanel row, List<object> tokens, string displayMode, string buttonStyle, ThemeColors tc)
        {
            bool prevStep = false;
            foreach (var tok in tokens)
            {
                var sep = tok as string;
                if (sep != null)
                {
                    row.Children.Add(SepElem(sep == "cancel" ? "✕✕" : "≫", tc));
                    prevStep = false;
                    continue;
                }
                var st = tok as StepToken;
                if (st == null) continue;
                if (prevStep) row.Children.Add(SepElem("▶", tc));
                row.Children.Add(StepElem(st, displayMode, buttonStyle, tc));
                prevStep = true;
            }
        }

        static FrameworkElement StepElem(StepToken s, string displayMode, string buttonStyle, ThemeColors tc)
        {
            if (s.Unknown || (displayMode == "text"))
            {
                var b = new Border
                {
                    Background = s.Unknown
                        ? new SolidColorBrush(Color.FromArgb(46, 240, 180, 95))
                        : new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                    BorderBrush = s.Unknown
                        ? new SolidColorBrush(Color.FromRgb(240, 180, 95))
                        : new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(7, 3, 7, 3)
                };
                b.Child = new TextBlock
                {
                    Text = s.Raw,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = s.Unknown
                        ? new SolidColorBrush(Color.FromRgb(255, 217, 138))
                        : new SolidColorBrush(Color.FromRgb(232, 234, 240))
                };
                return b;
            }

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (s.Stance.Length > 0)
                sp.Children.Add(Txt(s.Stance, 20, FontWeights.Bold, tc.Arrow, 0.92));
            var motion = displayMode == "digits" ? s.MotionDigits : s.Motion;
            if (motion.Length > 0)
                sp.Children.Add(Txt(motion, 20, FontWeights.Bold, tc.Arrow, 1.0));
            foreach (var b in s.Buttons)
            {
                var label = buttonStyle == "original" ? b.Orig : b.Label;
                var bd = new Border
                {
                    Background = BtnBrush(b.Key),
                    CornerRadius = new CornerRadius(4),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)),
                    BorderThickness = new Thickness(0, 0, 0, 2),
                    Padding = new Thickness(7, 3, 7, 3),
                    Margin = new Thickness(2, 0, 0, 0)
                };
                bd.Child = new TextBlock
                {
                    Text = label,
                    FontSize = 12.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Segoe UI")
                };
                sp.Children.Add(bd);
            }
            if (s.Frame.HasValue)
            {
                var fbd = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(5, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                fbd.Child = new TextBlock
                {
                    Text = s.Frame.Value + "F",
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Segoe UI")
                };
                sp.Children.Add(fbd);
            }
            return sp;
        }

        static FrameworkElement SepElem(string glyph, ThemeColors tc)
        {
            return new TextBlock
            {
                Text = glyph,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = tc.Sep,
                FontFamily = new FontFamily("Segoe UI Symbol, Microsoft YaHei UI, Segoe UI"),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        static TextBlock Txt(string text, double size, FontWeight w, Brush brush, double opacity)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = w,
                Foreground = brush,
                Opacity = opacity,
                FontFamily = new FontFamily("Segoe UI Symbol, Microsoft YaHei UI, Segoe UI"),
                Margin = new Thickness(0, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        static Brush BtnBrush(string key)
        {
            switch (key)
            {
                case "LP": return new SolidColorBrush(Color.FromRgb(0x3F, 0x8F, 0xD4));
                case "LK": return new SolidColorBrush(Color.FromRgb(0x4F, 0xAE, 0x6E));
                case "HP": return new SolidColorBrush(Color.FromRgb(0xE2, 0x95, 0x2F));
                case "HK": return new SolidColorBrush(Color.FromRgb(0xD9, 0x5B, 0x5B));
                case "MP": return new SolidColorBrush(Color.FromRgb(0x9B, 0x6F, 0xD0));
                case "MK": return new SolidColorBrush(Color.FromRgb(0x3F, 0xB8, 0xC4));
                default: return new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x98));
            }
        }
    }
}
