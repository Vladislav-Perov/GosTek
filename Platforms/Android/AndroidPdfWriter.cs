using Android.Graphics;
using Android.Graphics.Pdf;
using Android.Text;
using Android.Text.Style;
using GosTek.Models;
using Alignment = Android.Text.Layout.Alignment;
using Paint = Android.Graphics.Paint;
using TextAlignment = Microsoft.Maui.TextAlignment;

namespace GosTek.Platforms.Android;

internal static class AndroidPdfWriter
{
    // A4, с полями 30/15 мм по горизонтали и 20 мм сверху и снизу (в пунктах).
    private const int PageWidth = 595;
    private const int PageHeight = 842;
    private const int Left = 85;
    private const int Right = 43;
    private const int Top = 57;
    private const int Bottom = 57;
    private const int FooterHeight = 16;
    private const int ContentWidth = PageWidth - Left - Right;
    private const int ContentEnd = PageHeight - Bottom - FooterHeight;

    public static void Write(string title, IReadOnlyList<TemplateField> fields, IReadOnlyList<PreviewPara> paragraphs,
        Func<string, string> getValue, Stream output)
    {
        using var document = new PdfDocument();
        using var font = Typeface.Create("serif", TypefaceStyle.Normal);
        using var textPaint = new TextPaint(PaintFlags.AntiAlias) { Color = global::Android.Graphics.Color.Black };
        textPaint.SetTypeface(font);
        using var footer = new Paint(PaintFlags.AntiAlias)
        {
            Color = global::Android.Graphics.Color.Black,
            TextSize = 10,
            TextAlign = Paint.Align.Center,
        };
        PdfDocument.Page? page = null;
        int pageNumber = 0;
        int y = Top;

        void FinishPage()
        {
            if (page is not { } current)
                return;

            page = null;
            try
            {
                current.Canvas.DrawText(pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    PageWidth / 2f, PageHeight - Bottom, footer);
            }
            finally
            {
                document.FinishPage(current);
                current.Dispose();
            }
        }

        void StartPage()
        {
            FinishPage();
            using var info = new PdfDocument.PageInfo.Builder(PageWidth, PageHeight, ++pageNumber).Create();
            page = document.StartPage(info);
            y = Top;
        }

        void Draw(string content, TextAlignment alignment, double size, TypefaceStyle style, int spacing, int emphasizedLength = 0)
        {
            if (string.IsNullOrEmpty(content))
            {
                y += spacing;
                return;
            }

            // Ограничение защищает перенос страниц от аномальных размеров шрифта в пользовательском DOCX.
            textPaint.TextSize = size > 0 ? (float)Math.Clamp(size, 1, 200) : 14;
            using var text = new SpannableString(content);
            using var emphasis = new StyleSpan(emphasizedLength > 0 ? TypefaceStyle.Bold : style);
            if (emphasizedLength > 0 || style != TypefaceStyle.Normal)
                text.SetSpan(emphasis, 0, emphasizedLength > 0 ? emphasizedLength : text.Length(), SpanTypes.ExclusiveExclusive);

            using var builder = StaticLayout.Builder.Obtain(text, 0, text.Length(), textPaint, ContentWidth);
            builder.SetAlignment(alignment switch
            {
                TextAlignment.Center => Alignment.AlignCenter,
                TextAlignment.End => Alignment.AlignOpposite,
                _ => Alignment.AlignNormal,
            });
            using var layout = builder.SetIncludePad(false).Build();

            for (int line = 0; line < layout.LineCount;)
            {
                int firstTop = layout.GetLineTop(line);
                if (y + layout.GetLineTop(line + 1) - firstTop > ContentEnd)
                    StartPage();

                int next = line + 1;
                while (next < layout.LineCount && y + layout.GetLineTop(next + 1) - firstTop <= ContentEnd)
                    next++;

                int height = layout.GetLineTop(next) - firstTop;
                var canvas = page!.Canvas;
                int state = canvas.Save();
                try
                {
                    canvas.ClipRect(Left, y, Left + ContentWidth, y + height);
                    canvas.Translate(Left, y - firstTop);
                    layout.Draw(canvas);
                }
                finally
                {
                    canvas.RestoreToCount(state);
                }
                y += height;
                line = next;
            }

            y += spacing;
        }

        try
        {
            StartPage();
            foreach (var paragraph in paragraphs)
            {
                if (paragraph.Blank)
                {
                    y += 8;
                    continue;
                }

                var style = (paragraph.Bold, paragraph.Italic) switch
                {
                    (true, true) => TypefaceStyle.BoldItalic,
                    (true, false) => TypefaceStyle.Bold,
                    (false, true) => TypefaceStyle.Italic,
                    _ => TypefaceStyle.Normal,
                };
                var content = string.Concat(paragraph.Parts.Select(part => part.FieldId is { } id ? getValue(id) : part.Text));
                Draw(content, paragraph.Align, paragraph.Pt, style, 4);
            }

            if (paragraphs.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(title))
                    Draw(title, TextAlignment.Center, 16, TypefaceStyle.Bold, 14);
                foreach (var field in fields)
                {
                    string label = $"{field.Label}: ";
                    Draw(label + getValue(field.Id), TextAlignment.Start, 14, TypefaceStyle.Normal, 6, label.Length);
                }
            }
            FinishPage();
            document.WriteTo(output);
        }
        finally
        {
            try
            {
                FinishPage();
            }
            finally
            {
                document.Close();
            }
        }
    }
}
