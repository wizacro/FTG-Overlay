// FTG-Overlay —— 数据与设置存取（data/combos.json + data/settings.json）

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace FTGOverlayDemo
{
    public class GameInfo
    {
        public string Id;
        public string Name;
        public Dictionary<string, string> Buttons;
    }

    public class ComboItem
    {
        public string Id;
        public string Game;
        public string Character;   // null = 全角色通用
        public string Name;
        public string Raw;
        public string Condition;
        public string Position;
        public string Notes;
        public bool Show = true;   // 是否在悬浮窗显示
        public bool Mastered;
        public List<object> Tokens;
    }

    public class Settings
    {
        public double X = 80, Y = 110, Alpha = 0.78, Scale = 1.0;
        public string Theme = "A";                 // A 简约深色 | C 霓虹街机
        public string ButtonStyle = "mnemonic";    // mnemonic 拳脚名 | original 原按钮名
        public string DisplayMode = "arrows";      // arrows 图形箭头 | digits 数字写法 | text 文本原样
        public string Game = "kof13";
        public string Character;                   // null = 全角色通用
        public bool OverlayVisible = true;
        public bool Locked = true;                 // true=锁定（穿透不可拖）| false=解锁（可拖动）
        public bool FirstRun = true;               // 首次运行：自动弹出使用说明
    }

    public class Store
    {
        public Dictionary<string, GameInfo> Games = new Dictionary<string, GameInfo>();
        public List<ComboItem> Combos = new List<ComboItem>();
        public Settings Settings = new Settings();
        string _base;

        static JavaScriptSerializer NewJs() { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }; }

        public void Load(string baseDir)
        {
            _base = baseDir;
            var js = NewJs();
            var path = Path.Combine(_base, "data", "combos.json");
            if (File.Exists(path))
            {
                try
                {
                    var root = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                    object o;
                    if (root.TryGetValue("games", out o) && o != null)
                        foreach (var g in (IEnumerable)o)
                        {
                            var kv = (KeyValuePair<string, object>)g;
                            var gd = kv.Value as Dictionary<string, object>;
                            if (gd == null) continue;
                            var gi = new GameInfo { Id = kv.Key, Name = GetStr(gd, "name", kv.Key), Buttons = Engine.DefaultKofButtons() };
                            object bo;
                            if (gd.TryGetValue("buttons", out bo) && bo != null)
                            {
                                var m = new Dictionary<string, string>();
                                foreach (var b in (IEnumerable)bo)
                                {
                                    var bk = (KeyValuePair<string, object>)b;
                                    m[bk.Key.ToUpperInvariant()] = Convert.ToString(bk.Value);
                                }
                                gi.Buttons = m;
                            }
                            Games[gi.Id] = gi;
                        }
                    if (root.TryGetValue("combos", out o) && o != null)
                        foreach (var item in (IEnumerable)o)
                        {
                            var d = item as Dictionary<string, object>;
                            if (d == null || GetStr(d, "raw", "").Length == 0) continue;
                            var c = new ComboItem
                            {
                                Id = GetStr(d, "id", Guid.NewGuid().ToString("N")),
                                Game = GetStr(d, "game", "kof13"),
                                Character = GetStrOrNull(d, "character"),
                                Name = GetStr(d, "name", ""),
                                Raw = GetStr(d, "raw", ""),
                                Condition = GetStr(d, "condition", ""),
                                Position = GetStr(d, "position", ""),
                                Notes = GetStr(d, "notes", ""),
                                Show = GetBool(d, "show", true),
                                Mastered = GetBool(d, "mastered", false)
                            };
                            c.Tokens = Engine.ParseCombo(c.Raw, ButtonsOf(c.Game));
                            Combos.Add(c);
                        }
                }
                catch { }
            }
            if (Games.Count == 0)
                Games["kof13"] = new GameInfo { Id = "kof13", Name = "拳皇 XIII", Buttons = Engine.DefaultKofButtons() };

            var sp = Path.Combine(_base, "data", "settings.json");
            if (File.Exists(sp))
            {
                try
                {
                    var s = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(sp));
                    Settings.X = GetNum(s, "x", Settings.X);
                    Settings.Y = GetNum(s, "y", Settings.Y);
                    Settings.Alpha = GetNum(s, "alpha", Settings.Alpha);
                    Settings.Scale = GetNum(s, "scale", Settings.Scale);
                    Settings.Theme = GetStr(s, "theme", Settings.Theme);
                    Settings.ButtonStyle = GetStr(s, "buttonStyle", Settings.ButtonStyle);
                    Settings.DisplayMode = GetStr(s, "displayMode", Settings.DisplayMode);
                    Settings.Game = GetStr(s, "game", Settings.Game);
                    Settings.Character = GetStrOrNull(s, "character");
                    Settings.OverlayVisible = GetBool(s, "overlayVisible", Settings.OverlayVisible);
                    Settings.Locked = GetBool(s, "locked", Settings.Locked);
                    Settings.FirstRun = GetBool(s, "firstRun", Settings.FirstRun);
                }
                catch { }
            }
        }

        public void Reload()
        {
            Games.Clear();
            Combos.Clear();
            Settings = new Settings();
            Load(_base);
        }

        public Dictionary<string, string> ButtonsOf(string game)
        {
            GameInfo gi;
            if (Games.TryGetValue(game, out gi) && gi.Buttons != null) return gi.Buttons;
            return Engine.DefaultKofButtons();
        }

        public string GameName(string game)
        {
            GameInfo gi;
            if (Games.TryGetValue(game, out gi)) return gi.Name;
            return game;
        }

        public void SaveAll() { SaveCombos(); SaveSettings(); }

        public void SaveCombos()
        {
            var games = new Dictionary<string, object>();
            foreach (var g in Games.Values)
            {
                var btns = new Dictionary<string, object>();
                foreach (var b in g.Buttons) btns[b.Key] = b.Value;
                games[g.Id] = new Dictionary<string, object> { { "name", g.Name }, { "notation", "dreamcancel-kof" }, { "buttons", btns } };
            }
            var combos = new List<object>();
            foreach (var c in Combos)
                combos.Add(new Dictionary<string, object>
                {
                    { "id", c.Id }, { "game", c.Game },
                    { "character", c.Character }, { "name", c.Name },
                    { "condition", c.Condition }, { "position", c.Position },
                    { "tags", new string[0] }, { "mastered", c.Mastered },
                    { "show", c.Show }, { "raw", c.Raw }, { "notes", c.Notes }
                });
            var root = new Dictionary<string, object> { { "version", 1 }, { "games", games }, { "combos", combos } };
            WriteJson(Path.Combine(_base, "data", "combos.json"), root);
        }

        public void SaveSettings()
        {
            var s = new Dictionary<string, object>
            {
                { "x", Settings.X }, { "y", Settings.Y },
                { "alpha", Settings.Alpha }, { "scale", Settings.Scale },
                { "theme", Settings.Theme }, { "buttonStyle", Settings.ButtonStyle },
                { "displayMode", Settings.DisplayMode },
                { "game", Settings.Game }, { "character", Settings.Character },
                { "overlayVisible", Settings.OverlayVisible }, { "locked", Settings.Locked },
                { "firstRun", Settings.FirstRun }
            };
            WriteJson(Path.Combine(_base, "data", "settings.json"), s);
        }

        void WriteJson(string path, object obj)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, NewJs().Serialize(obj), new UTF8Encoding(false));
            }
            catch { }
        }

        static string GetStr(Dictionary<string, object> d, string key, string def)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null) return Convert.ToString(v);
            return def;
        }

        static string GetStrOrNull(Dictionary<string, object> d, string key)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null)
            {
                var s = Convert.ToString(v);
                return s.Length == 0 ? null : s;
            }
            return null;
        }

        static bool GetBool(Dictionary<string, object> d, string key, bool def)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null)
            {
                try { return Convert.ToBoolean(v); } catch { }
            }
            return def;
        }

        static double GetNum(Dictionary<string, object> d, string key, double def)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null)
            {
                try { return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); } catch { }
            }
            return def;
        }
    }
}
