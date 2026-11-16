using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ForpakLauncher
{
    /// <summary>
    /// Affiche la description d'un mod Modrinth (Markdown simplifié) en texte WPF lisible.
    /// Titres, listes, gras, code et liens sont gérés ; les images et le HTML sont retirés.
    /// </summary>
    public static class Markdown
    {
        private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(0xD5, 0xDC, 0xEA));
        private static readonly Brush TitleBrush = new SolidColorBrush(Color.FromRgb(0xF3, 0xF6, 0xFB));
        private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0xE6, 0xA8));
        private static readonly Brush CodeBg = new SolidColorBrush(Color.FromRgb(0x0D, 0x15, 0x26));

        public static void Render(Panel host, string md)
        {
            host.Children.Clear();
            if (string.IsNullOrWhiteSpace(md)) return;

            // Nettoyage : images, HTML, badges
            md = Regex.Replace(md, @"!\[[^\]]*\]\([^)]*\)", "");
            md = Regex.Replace(md, @"\[!\[[^\]]*\]\([^)]*\)\]\([^)]*\)", "");
            md = Regex.Replace(md, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            md = Regex.Replace(md, @"</?(p|div|center|details|summary)[^>]*>", "\n", RegexOptions.IgnoreCase);
            md = Regex.Replace(md, @"<h([1-6])[^>]*>(.*?)</h\1>", m => new string('#', int.Parse(m.Groups[1].Value)) + " " + m.Groups[2].Value + "\n",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            md = Regex.Replace(md, @"<li[^>]*>", "\n- ", RegexOptions.IgnoreCase);
            md = Regex.Replace(md, @"<[^>]+>", "");
            md = System.Net.WebUtility.HtmlDecode(md);

            var lines = md.Replace("\r", "").Split('\n');
            var para = new List<string>();
            bool inCode = false;
            var code = new List<string>();
            int blocks = 0;

            void FlushPara()
            {
                if (para.Count == 0) return;
                host.Children.Add(Text(string.Join(" ", para), 13, TextBrush, FontWeights.Normal, new Thickness(0, 0, 0, 10)));
                para.Clear();
                blocks++;
            }

            foreach (var raw in lines)
            {
                if (blocks > 120) break;
                var line = raw.TrimEnd();

                if (line.TrimStart().StartsWith("```"))
                {
                    if (inCode)
                    {
                        var tb = new TextBlock
                        {
                            Text = string.Join("\n", code), FontFamily = new FontFamily("Consolas"), FontSize = 12,
                            Foreground = TextBrush, TextWrapping = TextWrapping.Wrap
                        };
                        host.Children.Add(new Border
                        {
                            Background = CodeBg, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8),
                            Margin = new Thickness(0, 0, 0, 10), Child = tb
                        });
                        code.Clear();
                    }
                    else FlushPara();
                    inCode = !inCode;
                    continue;
                }
                if (inCode) { code.Add(raw); continue; }

                var t = line.Trim();
                if (t.Length == 0) { FlushPara(); continue; }
                if (Regex.IsMatch(t, @"^(-{3,}|\*{3,}|_{3,})$")) { FlushPara(); continue; }

                var h = Regex.Match(t, @"^(#{1,6})\s+(.*)$");
                if (h.Success)
                {
                    FlushPara();
                    int level = h.Groups[1].Value.Length;
                    double size = level == 1 ? 22 : level == 2 ? 18 : 15;
                    host.Children.Add(Text(h.Groups[2].Value, size, TitleBrush, FontWeights.Bold,
                        new Thickness(0, blocks == 0 ? 0 : 10, 0, 8)));
                    blocks++;
                    continue;
                }

                var li = Regex.Match(t, @"^([-*+]|\d+\.)\s+(.*)$");
                if (li.Success)
                {
                    FlushPara();
                    var row = new DockPanel { Margin = new Thickness(6, 0, 0, 5) };
                    var bullet = new TextBlock { Text = "•", Foreground = AccentBrush, FontSize = 13, Margin = new Thickness(0, 0, 8, 0) };
                    DockPanel.SetDock(bullet, Dock.Left);
                    row.Children.Add(bullet);
                    row.Children.Add(Text(li.Groups[2].Value, 13, TextBrush, FontWeights.Normal, new Thickness(0)));
                    host.Children.Add(row);
                    blocks++;
                    continue;
                }

                if (t.StartsWith(">")) t = t.TrimStart('>', ' ');
                para.Add(t);
            }
            FlushPara();
        }

        /// <summary>TextBlock avec **gras**, `code` et [liens](url) simplifiés.</summary>
        private static TextBlock Text(string s, double size, Brush color, FontWeight weight, Thickness margin)
        {
            var tb = new TextBlock
            {
                FontSize = size, Foreground = color, FontWeight = weight, TextWrapping = TextWrapping.Wrap,
                Margin = margin, LineHeight = size * 1.45
            };
            // liens -> texte coloré
            var parts = Regex.Split(s, @"(\[[^\]]+\]\([^)]+\)|\*\*[^*]+\*\*|__[^_]+__|`[^`]+`)");
            foreach (var part in parts)
            {
                if (part.Length == 0) continue;
                var link = Regex.Match(part, @"^\[([^\]]+)\]\(([^)]+)\)$");
                if (link.Success) { tb.Inlines.Add(new Run(link.Groups[1].Value) { Foreground = AccentBrush }); continue; }
                if ((part.StartsWith("**") && part.EndsWith("**")) || (part.StartsWith("__") && part.EndsWith("__")))
                {
                    tb.Inlines.Add(new Bold(new Run(part.Substring(2, part.Length - 4))) { Foreground = TitleBrush });
                    continue;
                }
                if (part.StartsWith("`") && part.EndsWith("`") && part.Length > 1)
                {
                    tb.Inlines.Add(new Run(part.Trim('`')) { FontFamily = new FontFamily("Consolas"), Background = CodeBg });
                    continue;
                }
                tb.Inlines.Add(new Run(Regex.Replace(part, @"(?<!\w)[*_]{1,2}(?=\S)|(?<=\S)[*_]{1,2}(?!\w)", "")));
            }
            return tb;
        }
    }
}
