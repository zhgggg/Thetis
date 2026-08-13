// ==================================================================
// Translator.cs
// ==================================================================
// Runtime UI translation layer for Thetis.
// Source code keeps all UI strings in English. Translations live in
// plain-text language files under the Languages\ folder next to the
// executable (key=value, key is the English source string).
// This keeps the source clean and makes it easy to add languages and
// to reuse the translation tables when translating documentation.
//
// Usage:
//   Translator.Initialize();        // once at startup (default zh-CN)
//   Translator.ApplyToForm(this);   // after InitializeComponent() in forms
//   Translator.Tr("Some text");     // translate runtime-generated strings
//
// Language switching (menu):
//   Translator.SetLanguage(Translator.LANG_ZH / LANG_EN);
// ==================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Thetis
{
    public static class Translator
    {
        public const string LANG_ZH = "zh-CN";
        public const string LANG_EN = "en-US";

        private const string REG_PATH = @"Software\OpenHPSDR\Thetis";
        private const string REG_KEY = "Language";

        private static string s_language = LANG_ZH;
        private static Dictionary<string, string> s_dict = new Dictionary<string, string>();
        private static bool s_initialized = false;
        private static bool s_hasDictionary = false;

        // per-Type cache of ToolTip/ContextMenuStrip fields found by reflection
        private static Dictionary<Type, FieldInfo[]> s_tooltipFields = new Dictionary<Type, FieldInfo[]>();
        private static Dictionary<Type, FieldInfo[]> s_cmsFields = new Dictionary<Type, FieldInfo[]>();

        public static event EventHandler LanguageChanged;

        public static string CurrentLanguage { get { return s_language; } }

        public static bool IsChinese { get { return s_language != LANG_EN; } }

        public static void Initialize()
        {
            if (s_initialized) return;
            s_initialized = true;

            string saved = null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(REG_PATH))
                {
                    if (key != null) saved = key.GetValue(REG_KEY) as string;
                }
            }
            catch { }

            SetLanguageInternal(string.IsNullOrEmpty(saved) ? LANG_ZH : saved, false);
        }

        public static void SetLanguage(string language)
        {
            SetLanguageInternal(language, true);
        }

        private static void SetLanguageInternal(string language, bool notify)
        {
            string lang = (string.Compare(language, LANG_EN, StringComparison.OrdinalIgnoreCase) == 0) ? LANG_EN : LANG_ZH;
            if (lang == s_language && s_initialized) { RefreshDictionary(); return; }
            s_language = lang;
            RefreshDictionary();

            if (!s_initialized) return;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(REG_PATH))
                {
                    key.SetValue(REG_KEY, s_language);
                }
            }
            catch { }

            if (notify)
            {
                EventHandler handler = LanguageChanged;
                if (handler != null)
                {
                    try { handler(null, EventArgs.Empty); } catch { }
                }
            }
        }

        private static void RefreshDictionary()
        {
            s_dict.Clear();
            s_hasDictionary = false;
            if (s_language == LANG_EN) return;

            string path = LocateLangFile("zh-CN.lang");
            if (path == null) return;

            try
            {
                LoadLangFile(path);
                s_hasDictionary = s_dict.Count > 0;
            }
            catch { }
        }

        private static string LocateLangFile(string name)
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string p = Path.Combine(appDir, "Languages", name);
                if (File.Exists(p)) return p;
                p = Path.Combine(appDir, name);
                if (File.Exists(p)) return p;

                string appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OpenHPSDR");
                p = Path.Combine(appData, name);
                if (File.Exists(p)) return p;
            }
            catch { }
            return null;
        }

        private static void LoadLangFile(string path)
        {
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (raw.Length == 0 || raw[0] == '#') continue;
                int idx = raw.IndexOf('=');
                if (idx <= 0) continue;
                string key = Unescape(raw.Substring(0, idx));
                string val = Unescape(raw.Substring(idx + 1));
                if (key.Length == 0) continue;
                s_dict[key] = val;
            }
        }

        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    if (n == 'n') { sb.Append('\n'); i++; continue; }
                    if (n == 't') { sb.Append('\t'); i++; continue; }
                    if (n == 'r') { sb.Append('\r'); i++; continue; }
                    if (n == '\\') { sb.Append('\\'); i++; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        // Translate an English source string. Returns the input unchanged
        // when no translation exists or the language is English.
        public static string Tr(string english)
        {
            if (string.IsNullOrEmpty(english)) return english;
            if (!IsChinese || !s_hasDictionary) return english;
            string val;
            if (s_dict.TryGetValue(english, out val)) return val;
            return english;
        }

        // Map a displayed string to the other language when known.
        // zh-CN mode: EN -> CN. en-US mode: CN -> EN.
        // Returns null when the string is not known (dynamic content).
        private static string Map(string text)
        {
            if (string.IsNullOrEmpty(text) || !s_hasDictionary) return null;
            if (IsChinese)
            {
                string val;
                if (s_dict.TryGetValue(text, out val)) return val;
                return null;
            }
            else
            {
                // reverse lookup (values are unique in practice)
                foreach (KeyValuePair<string, string> kvp in s_dict)
                {
                    if (string.Equals(kvp.Value, text, StringComparison.Ordinal)) return kvp.Key;
                }
                return null;
            }
        }

        // ==================================================================
        // Form / control tree translation
        // ==================================================================

        public static void ApplyToForm(Control root)
        {
            if (root == null) return;
            try
            {
                ApplyToControlTree(root);
                ApplyToolTipsAndMenus(root);
            }
            catch { }
        }

        private static void ApplyToControlTree(Control root)
        {
            foreach (Control c in AllControls(root))
            {
                Type t = c.GetType();
                if (t == typeof(Label) || t == typeof(Button) || t == typeof(CheckBox) ||
                    t == typeof(RadioButton) || t == typeof(GroupBox) || t == typeof(LinkLabel) ||
                    t.IsSubclassOf(typeof(Label)) || t.IsSubclassOf(typeof(Button)) ||
                    t.IsSubclassOf(typeof(CheckBox)) || t.IsSubclassOf(typeof(RadioButton)) ||
                    t.IsSubclassOf(typeof(GroupBox)) || t.IsSubclassOf(typeof(LinkLabel)) ||
                    t == typeof(Form) || t.IsSubclassOf(typeof(Form)))
                {
                    TranslateText(c);
                }

                if (c is ComboBox)
                {
                    ComboBox cb = (ComboBox)c;
                    for (int i = 0; i < cb.Items.Count; i++)
                    {
                        string s = cb.Items[i] as string;
                        if (s == null) continue;
                        string nv = Map(s);
                        if (nv != null) cb.Items[i] = nv;
                    }
                }

                if (c is CheckedListBox)
                {
                    CheckedListBox clb = (CheckedListBox)c;
                    for (int i = 0; i < clb.Items.Count; i++)
                    {
                        string s = clb.Items[i] as string;
                        if (s == null) continue;
                        string nv = Map(s);
                        if (nv != null) clb.Items[i] = nv;
                    }
                }

                if (c is ListBox)
                {
                    ListBox lb = (ListBox)c;
                    for (int i = 0; i < lb.Items.Count; i++)
                    {
                        string s = lb.Items[i] as string;
                        if (s == null) continue;
                        string nv = Map(s);
                        if (nv != null) lb.Items[i] = nv;
                    }
                }

                if (c is TabControl)
                {
                    TabControl tc = (TabControl)c;
                    foreach (TabPage tp in tc.TabPages)
                    {
                        TranslateText(tp);
                    }
                }

                if (c is ListView)
                {
                    ListView lv = (ListView)c;
                    foreach (ColumnHeader ch in lv.Columns)
                    {
                        string nv = Map(ch.Text);
                        if (nv != null) ch.Text = nv;
                    }
                }

                if (c is DataGridView)
                {
                    DataGridView dgv = (DataGridView)c;
                    foreach (DataGridViewColumn col in dgv.Columns)
                    {
                        string nv = Map(col.HeaderText);
                        if (nv != null) col.HeaderText = nv;
                    }
                }

                if (c is TreeView)
                {
                    TreeView tv = (TreeView)c;
                    foreach (TreeNode node in tv.Nodes)
                    {
                        TranslateNode(node);
                    }
                }

                if (c is ToolStrip)
                {
                    ApplyToToolStrip((ToolStrip)c);
                }

                if (c.ContextMenuStrip != null)
                {
                    ApplyToToolStrip(c.ContextMenuStrip);
                }
            }
        }

        private static void TranslateNode(TreeNode node)
        {
            string nv = Map(node.Text);
            if (nv != null) node.Text = nv;
            foreach (TreeNode child in node.Nodes)
            {
                TranslateNode(child);
            }
        }

        private static void TranslateText(Control c)
        {
            string nv = Map(c.Text);
            if (nv != null) c.Text = nv;
        }

        private static void ApplyToToolStrip(ToolStrip ts)
        {
            if (ts == null || ts.IsDisposed) return;
            foreach (ToolStripItem item in ts.Items)
            {
                ApplyToToolStripItem(item);
            }
        }

        private static void ApplyToToolStripItem(ToolStripItem item)
        {
            if (item == null) return;
            if (item is ToolStripDropDownItem)
            {
                foreach (ToolStripItem child in ((ToolStripDropDownItem)item).DropDownItems)
                {
                    ApplyToToolStripItem(child);
                }
            }
            string nv = Map(item.Text);
            if (nv != null) item.Text = nv;
        }

        private static void ApplyToolTipsAndMenus(Control root)
        {
            // ToolTip / ContextMenuStrip components held in fields of the root type
            foreach (Control c in AllControls(root))
            {
                Type t = c.GetType();
                if (t.Assembly != Assembly.GetExecutingAssembly() &&
                    t.Assembly != typeof(Translator).Assembly) continue;

                FieldInfo[] tooltips = GetCachedFields(t, ref s_tooltipFields, typeof(ToolTip));
                foreach (FieldInfo fi in tooltips)
                {
                    object o;
                    try { o = fi.GetValue(c); } catch { continue; }
                    ToolTip tt = o as ToolTip;
                    if (tt == null) continue;
                    foreach (Control child in AllControls(c))
                    {
                        try
                        {
                            string s = tt.GetToolTip(child);
                            if (string.IsNullOrEmpty(s)) continue;
                            string nv = Map(s);
                            if (nv != null) tt.SetToolTip(child, nv);
                        }
                        catch { }
                    }
                }

                FieldInfo[] cmss = GetCachedFields(t, ref s_cmsFields, typeof(ContextMenuStrip));
                foreach (FieldInfo fi in cmss)
                {
                    object o;
                    try { o = fi.GetValue(c); } catch { continue; }
                    ContextMenuStrip cms = o as ContextMenuStrip;
                    if (cms != null) ApplyToToolStrip(cms);
                }
            }
        }

        private static FieldInfo[] GetCachedFields(Type t, ref Dictionary<Type, FieldInfo[]> cache, Type fieldType)
        {
            FieldInfo[] result;
            lock (cache)
            {
                if (!cache.TryGetValue(t, out result))
                {
                    List<FieldInfo> list = new List<FieldInfo>();
                    foreach (FieldInfo fi in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        if (fi.FieldType == fieldType || fi.FieldType.IsSubclassOf(fieldType))
                        {
                            list.Add(fi);
                        }
                    }
                    result = list.ToArray();
                    cache[t] = result;
                }
            }
            return result;
        }

        private static IEnumerable<Control> AllControls(Control root)
        {
            yield return root;
            foreach (Control c in root.Controls)
            {
                foreach (Control d in AllControls(c))
                {
                    yield return d;
                }
            }
        }

        // ==================================================================
        // Language switching helpers
        // ==================================================================

        public static void ApplyToAllOpenForms()
        {
            foreach (Form f in Application.OpenForms)
            {
                if (f == null || f.IsDisposed) continue;
                try { ApplyToForm(f); } catch { }
            }
        }
    }
}
