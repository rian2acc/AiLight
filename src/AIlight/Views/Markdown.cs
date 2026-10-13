using System.Diagnostics;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

namespace AIlight.Views;

/// <summary>
/// Kleiner Markdown-Renderer: Überschriften, Absätze, Listen, Codeblöcke,
/// Zitate, Trennlinien und die üblichen Inline-Auszeichnungen. Bewusst
/// überschaubar — was AIlight nicht braucht, wird auch nicht geraten.
/// </summary>
public static class Markdown
{
    public static IReadOnlyList<UIElement> Render(string text, double fontSize)
    {
        var blocks = new List<UIElement>();
        var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var index = 0;

        while (index < lines.Length)
        {
            var line = lines[index];

            // Codeblock
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                var code = new List<string>();
                index++;
                while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    code.Add(lines[index]);
                    index++;
                }

                index++; // schließende Zeile
                blocks.Add(CodeBlock(string.Join("\n", code), fontSize));
                continue;
            }

            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                index++;
                continue;
            }

            // Trennlinie
            if (trimmed is "---" or "***" or "___" || (trimmed.Length >= 3 && trimmed.All(c => c == '-')))
            {
                blocks.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(0, 8, 0, 8),
                    Background = Resource<Brush>("Brush.Separator", new SolidColorBrush(Color.FromArgb(26, 255, 255, 255))),
                });
                index++;
                continue;
            }

            // Überschrift
            if (trimmed.StartsWith('#'))
            {
                var level = trimmed.TakeWhile(c => c == '#').Count();
                var heading = trimmed[level..].Trim();
                blocks.Add(Paragraph(BuildInlines(heading, fontSize + Math.Max(0, 4 - level) * 1.5, bold: true),
                    new Thickness(0, blocks.Count == 0 ? 0 : 8, 0, 4)));
                index++;
                continue;
            }

            // Liste
            if (IsBullet(trimmed) || IsNumbered(trimmed))
            {
                var items = new List<string>();
                while (index < lines.Length)
                {
                    var itemLine = lines[index].Trim();
                    if (!IsBullet(itemLine) && !IsNumbered(itemLine))
                    {
                        break;
                    }

                    var marker = IsBullet(itemLine) ? "•" : itemLine.Split('.', 2)[0] + ".";
                    items.Add(marker + "\u2002" + Strip(itemLine));
                    index++;
                }

                var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
                foreach (var item in items)
                {
                    panel.Children.Add(Paragraph(BuildInlines(item, fontSize), new Thickness(10, 1, 0, 1)));
                }

                blocks.Add(panel);
                continue;
            }

            // Zitat
            if (trimmed.StartsWith('>'))
            {
                var quote = new List<string>();
                while (index < lines.Length && lines[index].TrimStart().StartsWith('>'))
                {
                    quote.Add(lines[index].TrimStart().TrimStart('>').Trim());
                    index++;
                }

                var content = Paragraph(BuildInlines(string.Join(" ", quote), fontSize, italic: true),
                    new Thickness(10, 4, 8, 4), foreground: Resource<Brush>("Brush.Muted", new SolidColorBrush(Colors.Gray)));
                blocks.Add(new Border
                {
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    BorderBrush = Resource<Brush>("Brush.Accent", new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF))),
                    Margin = new Thickness(0, 4, 0, 4),
                    Child = content,
                });
                continue;
            }

            // Tabelle: schlicht, aber lesbar in fester Schrift
            if (trimmed.StartsWith('|') && trimmed.EndsWith('|'))
            {
                var rows = new List<string>();
                while (index < lines.Length && lines[index].Trim().StartsWith('|'))
                {
                    var row = lines[index].Trim();
                    if (!row.All(c => c is '|' or '-' or ':' or ' '))
                    {
                        rows.Add(row.Trim('|').Replace("|", "\u2002│\u2002").Trim());
                    }

                    index++;
                }

                blocks.Add(CodeBlock(string.Join("\n", rows), fontSize));
                continue;
            }

            // Absatz
            var paragraph = new List<string> { trimmed };
            index++;
            while (index < lines.Length)
            {
                var next = lines[index].Trim();
                if (next.Length == 0 || next.StartsWith('#') || next.StartsWith("```") || next.StartsWith('>')
                    || IsBullet(next) || IsNumbered(next) || next.StartsWith('|'))
                {
                    break;
                }

                paragraph.Add(next);
                index++;
            }

            blocks.Add(Paragraph(BuildInlines(string.Join(" ", paragraph), fontSize), new Thickness(0, 3, 0, 3)));
        }

        return blocks;
    }

    private static bool IsBullet(string line)
        => line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal)
           || line.StartsWith("• ", StringComparison.Ordinal);

    private static bool IsNumbered(string line)
    {
        var dot = line.IndexOf('.');
        return dot is > 0 and <= 3 && line[..dot].All(char.IsDigit);
    }

    private static string Strip(string line)
        => IsBullet(line) ? line[2..].Trim() : line[(line.IndexOf('.') + 1)..].Trim();

    private static TextBlock Paragraph(IEnumerable<Inline> inlines, Thickness margin, Brush? foreground = null)
    {
        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = margin,
            FontFamily = Resource("Font.Ui", new FontFamily("Segoe UI")),
            Foreground = foreground ?? Resource<Brush>("Brush.Text", new SolidColorBrush(Colors.WhiteSmoke)),
        };

        return block.AddInlines(inlines);
    }

    /// <summary>Codeblock mit eigenem „Kopieren“-Knopf — praktischer als markieren.</summary>
    private static Border CodeBlock(string code, double fontSize)
    {
        var body = code.Trim('\n');
        var text = new TextBlock
        {
            FontFamily = Resource("Font.Mono", new FontFamily("Consolas")),
            FontSize = Math.Max(11, fontSize - 1.5),
            Foreground = Resource<Brush>("Brush.Text", new SolidColorBrush(Colors.WhiteSmoke)),
            TextWrapping = TextWrapping.Wrap,
            Text = body,
            Margin = new Thickness(0, 0, 34, 0),
        };

        var copy = new Button
        {
            Content = "⧉",
            FontSize = 12,
            Width = 24,
            Height = 22,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = Cursors.Hand,
            ToolTip = "Code kopieren",
        };

        if (Application.Current?.TryFindResource("Bar.Button") is Style style)
        {
            copy.Style = style;
        }

        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(body);
                copy.Content = "✓";
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
                timer.Tick += (sender, _) =>
                {
                    copy.Content = "⧉";
                    if (sender is DispatcherTimer running)
                    {
                        running.Stop();
                    }
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                Core.Log.Warn($"Code konnte nicht kopiert werden: {ex.Message}");
            }
        };

        var grid = new Grid();
        grid.Children.Add(text);
        grid.Children.Add(copy);

        return new Border
        {
            Background = Resource<Brush>("Brush.Code", new SolidColorBrush(Color.FromArgb(89, 0, 0, 0))),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 5, 0, 5),
            Child = grid,
        };
    }

    private static TextBlock AddInlines(this TextBlock target, IEnumerable<Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            target.Inlines.Add(inline);
        }

        return target;
    }

    /// <summary>Inline-Auszeichnungen in WPF-Elemente übersetzen.</summary>
    public static IEnumerable<Inline> BuildInlines(string text, double fontSize, bool bold = false, bool italic = false)
    {
        var result = new List<Inline>();
        var buffer = new System.Text.StringBuilder();

        void Flush()
        {
            if (buffer.Length > 0)
            {
                result.Add(new Run(buffer.ToString())
                {
                    FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                    FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
                });
                buffer.Clear();
            }
        }

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];

            // **fett** / __fett__
            if ((current == '*' || current == '_') && index + 1 < text.Length && text[index + 1] == current)
            {
                var end = text.IndexOf(new string(current, 2), index + 2, StringComparison.Ordinal);
                if (end > index)
                {
                    Flush();
                    result.AddRange(BuildInlines(text[(index + 2)..end], fontSize, bold: true, italic));
                    index = end + 1;
                    continue;
                }
            }

            // *kursiv*
            if (current == '*' || current == '_')
            {
                var end = text.IndexOf(current, index + 1);
                if (end > index + 1)
                {
                    Flush();
                    result.AddRange(BuildInlines(text[(index + 1)..end], fontSize, bold, italic: true));
                    index = end;
                    continue;
                }
            }

            // `Code`
            if (current == '`')
            {
                var end = text.IndexOf('`', index + 1);
                if (end > index)
                {
                    Flush();
                    result.Add(new Run(text[(index + 1)..end])
                    {
                        FontFamily = Resource("Font.Mono", new FontFamily("Consolas")),
                        FontSize = Math.Max(11, fontSize - 1),
                        Background = Resource<Brush>("Brush.Code", new SolidColorBrush(Color.FromArgb(89, 0, 0, 0))),
                    });
                    index = end;
                    continue;
                }
            }

            // [Text](Adresse)
            if (current == '[')
            {
                var close = text.IndexOf("](", index, StringComparison.Ordinal);
                var end = close > 0 ? text.IndexOf(')', close) : -1;
                if (close > 0 && end > close)
                {
                    Flush();
                    var label = text[(index + 1)..close];
                    var address = text[(close + 2)..end];
                    var link = new Hyperlink(new Run(label))
                    {
                        Foreground = Resource<Brush>("Brush.Accent", new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF))),
                        TextDecorations = null,
                    };
                    link.Click += (_, _) => OpenLink(address);
                    result.Add(link);
                    index = end;
                    continue;
                }
            }

            buffer.Append(current);
        }

        Flush();
        return result;
    }

    private static void OpenLink(string address)
    {
        try
        {
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Core.Log.Warn($"Link konnte nicht geöffnet werden: {ex.Message}");
        }
    }

    private static T Resource<T>(string key, T fallback)
        => Application.Current?.TryFindResource(key) is T value ? value : fallback;

    /// <summary>Rückwärtskompatibler Name für den Prüfstand.</summary>
    internal static int CountBlocks(string text, double fontSize) => Render(text, fontSize).Count;
}
