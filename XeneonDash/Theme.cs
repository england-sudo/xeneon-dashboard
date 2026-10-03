using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace XeneonDash;

#region Gfx — sharp-geometry primitives shared by every theme (no rounded corners, ever)

internal static class Gfx
{
    /// <summary>Sharp-cornered polygon: a rectangle with cut (chamfered) corners. No rounding.</summary>
    public static GraphicsPath Chamfer(Rectangle r, int cut)
    {
        cut = Math.Max(1, Math.Min(cut, Math.Min(r.Width, r.Height) / 2 - 1));
        var p = new GraphicsPath();
        p.AddLines(new[]
        {
            new Point(r.X + cut, r.Y), new Point(r.Right - cut, r.Y),
            new Point(r.Right, r.Y + cut), new Point(r.Right, r.Bottom - cut),
            new Point(r.Right - cut, r.Bottom), new Point(r.X + cut, r.Bottom),
            new Point(r.X, r.Bottom - cut), new Point(r.X, r.Y + cut),
        });
        p.CloseFigure();
        return p;
    }

    public static void Diamond(Graphics g, float cx, float cy, float r, Brush brush)
    {
        g.FillPolygon(brush, new[]
        {
            new PointF(cx, cy - r), new PointF(cx + r, cy),
            new PointF(cx, cy + r), new PointF(cx - r, cy),
        });
    }

    public static Color Lighten(Color c, float amt)
    {
        amt = Math.Clamp(amt, 0, 1);
        return Color.FromArgb(c.A,
            (int)(c.R + (255 - c.R) * amt),
            (int)(c.G + (255 - c.G) * amt),
            (int)(c.B + (255 - c.B) * amt));
    }

    /// <summary>L-shaped corner brackets drawn just inside r (sci-fi HUD corners).</summary>
    public static void Brackets(Graphics g, Rectangle r, int len, float width, Color color)
    {
        using var pen = new Pen(color, width);
        int x0 = r.X + 4, y0 = r.Y + 4, x1 = r.Right - 4, y1 = r.Bottom - 4;
        g.DrawLine(pen, x0, y0 + len, x0, y0); g.DrawLine(pen, x0, y0, x0 + len, y0);
        g.DrawLine(pen, x1 - len, y0, x1, y0); g.DrawLine(pen, x1, y0, x1, y0 + len);
        g.DrawLine(pen, x0, y1 - len, x0, y1); g.DrawLine(pen, x0, y1, x0 + len, y1);
        g.DrawLine(pen, x1 - len, y1, x1, y1); g.DrawLine(pen, x1, y1, x1, y1 - len);
    }

    /// <summary>Metal rivet: dark disc with an offset highlight.</summary>
    public static void Rivet(Graphics g, float cx, float cy, float r, Color disc, Color hi)
    {
        using var b = new SolidBrush(disc);
        g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
        using var h = new SolidBrush(hi);
        g.FillEllipse(h, cx - r * 0.6f, cy - r * 0.65f, r * 0.75f, r * 0.75f);
        using var p = new Pen(Color.FromArgb(110, Color.Black), 1f);
        g.DrawEllipse(p, cx - r, cy - r, r * 2, r * 2);
    }

    /// <summary>Deterministic lava-crack glow running along the panel edges.</summary>
    /// <summary>Eight-sided HUD polygon.</summary>
    public static GraphicsPath Octagon(Rectangle r, int cut)
    {
        var p = new GraphicsPath();
        p.AddPolygon(new[]
        {
            new Point(r.X + cut, r.Y), new Point(r.Right - cut, r.Y),
            new Point(r.Right, r.Y + cut), new Point(r.Right, r.Bottom - cut),
            new Point(r.Right - cut, r.Bottom), new Point(r.X + cut, r.Bottom),
            new Point(r.X, r.Bottom - cut), new Point(r.X, r.Y + cut),
        });
        return p;
    }

    /// <summary>Obsidian shard: slanted parallelogram corners.</summary>
    public static PointF[] ShardCorners(Rectangle r, int slant) => new[]
    {
        new PointF(r.X + slant, r.Y), new PointF(r.Right, r.Y),
        new PointF(r.Right - slant, r.Bottom), new PointF(r.X, r.Bottom),
    };

    public static GraphicsPath ShardPath(Rectangle r, int slant)
    {
        var p = new GraphicsPath();
        p.AddPolygon(ShardCorners(r, slant));
        return p;
    }

    /// <summary>Lava-crack seam following any closed polygon loop.</summary>
    public static void CrackEdge(Graphics g, PointF[] poly, Color glow, int seed, Rectangle clamp)
    {
        var rnd = new Random(seed * 7919 + (int)clamp.X * 31 + (int)clamp.Y * 17);
        using var glowPen = new Pen(Color.FromArgb(64, glow), 7f)
            { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var corePen = new Pen(Color.FromArgb(215, glow), 1.6f)
            { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        // molten seam: one closed loop hugging the polygon, jittered inward
        float cx = clamp.X + clamp.Width / 2f, cy = clamp.Y + clamp.Height / 2f;
        var pts = new List<PointF>();
        for (int e = 0; e < poly.Length; e++)
        {
            var a = poly[e]; var b = poly[(e + 1) % poly.Length];
            float len = (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            int steps = Math.Max(1, (int)(len / 44));
            for (int i = (e == 0 ? 0 : 1); i <= steps; i++)
            {
                float t = i / (float)steps;
                float x = a.X + (b.X - a.X) * t, y = a.Y + (b.Y - a.Y) * t;
                float dx = cx - x, dy = cy - y;
                float d = Math.Max(1, (float)Math.Sqrt(dx * dx + dy * dy));
                float inward = (float)(rnd.NextDouble() * 6 + 1.5);   // 1.5..7.5px toward center
                float wob = (float)((rnd.NextDouble() - 0.5) * 5);    // lateral wobble
                pts.Add(new PointF(x + dx / d * inward - dy / d * wob,
                                   y + dy / d * inward + dx / d * wob));
            }
        }
        var arr = pts.ToArray();
        g.DrawLines(glowPen, arr);
        g.DrawLines(corePen, arr);
        // a few short branch cracks, kept inside the panel
        for (int k = 0; k < 3; k++)
        {
            var bp = pts[rnd.Next(pts.Count)];
            float ang = (float)(rnd.NextDouble() * Math.PI * 2);
            float blen = (float)(rnd.NextDouble() * 26 + 10);
            var tip = new PointF(
                Math.Clamp(bp.X + (float)Math.Cos(ang) * blen, clamp.X + 8, clamp.Right - 8),
                Math.Clamp(bp.Y + (float)Math.Sin(ang) * blen, clamp.Y + 8, clamp.Bottom - 8));
            var branch = new[] { bp, tip };
            g.DrawLines(glowPen, branch);
            g.DrawLines(corePen, branch);
        }
    }

    /// <summary>Slanted parallelogram progress bar with glow.</summary>
    public static void AngularBar(Graphics g, Rectangle track, float frac, Color fill, Color line)
    {
        int slant = 9;
        var tp = new GraphicsPath();
        tp.AddLines(new[]
        {
            new Point(track.X, track.Bottom), new Point(track.X + slant, track.Y),
            new Point(track.Right, track.Y), new Point(track.Right - slant, track.Bottom),
        });
        tp.CloseFigure();
        using var tbrush = new SolidBrush(Color.FromArgb(22, 10, 8));
        g.FillPath(tbrush, tp);
        using var tpen = new Pen(line, 1f);
        g.DrawPath(tpen, tp);
        tp.Dispose();

        int fw = (int)(track.Width * Math.Clamp(frac, 0, 1));
        if (fw > slant + 2)
        {
            using var fp = new GraphicsPath();
            fp.AddLines(new[]
            {
                new Point(track.X, track.Bottom), new Point(track.X + slant, track.Y),
                new Point(track.X + fw, track.Y), new Point(track.X + fw - slant, track.Bottom),
            });
            fp.CloseFigure();
            using var glow = new Pen(Color.FromArgb(70, fill), 5f);
            g.DrawPath(glow, fp);
            using var brush = new LinearGradientBrush(track, Lighten(fill, 0.33f), fill, LinearGradientMode.Horizontal);
            g.FillPath(brush, fp);
        }
    }

    /// <summary>Slanted bar with diagonal energy stripes (Nebula).</summary>
    public static void StripedBar(Graphics g, Rectangle track, float frac, Color fill, Color line)
    {
        int slant = 9;
        using var tp = new GraphicsPath();
        tp.AddLines(new[]
        {
            new Point(track.X, track.Bottom), new Point(track.X + slant, track.Y),
            new Point(track.Right, track.Y), new Point(track.Right - slant, track.Bottom),
        });
        tp.CloseFigure();
        using var tbrush = new SolidBrush(Color.FromArgb(26, 18, 26));
        g.FillPath(tbrush, tp);
        using var tpen = new Pen(line, 1f);
        g.DrawPath(tpen, tp);

        int fw = (int)(track.Width * Math.Clamp(frac, 0, 1));
        if (fw > slant + 4)
        {
            using var fp = new GraphicsPath();
            fp.AddLines(new[]
            {
                new Point(track.X, track.Bottom), new Point(track.X + slant, track.Y),
                new Point(track.X + fw, track.Y), new Point(track.X + fw - slant, track.Bottom),
            });
            fp.CloseFigure();
            using var dark = new SolidBrush(Color.FromArgb(52, fill));
            g.FillPath(dark, fp);
            var clip = g.Clip;
            g.SetClip(fp);
            using var stripe = new Pen(fill, 5f);
            for (int x = track.X - track.Height; x < track.X + fw + track.Height; x += 12)
                g.DrawLine(stripe, x, track.Bottom + 2, x + track.Height, track.Y - 2);
            g.Clip = clip;
            using var edge = new Pen(Color.FromArgb(120, fill), 1.5f);
            g.DrawPath(edge, fp);
        }
    }
}

#endregion

#region ThemeStyle — one instance per visual theme

/// <summary>Where a theme places the page tabs.</summary>
public enum NavPlace { Bottom, InHeader }

/// <summary>Everything a theme needs to paint the header strip.</summary>
public sealed class HeaderInfo
{
    public Rectangle Bounds;
    public string Title = "";
    public string Machine = "";
    public string Clock = "";
    public string Date = "";
    /// <summary>Page tabs; the last entry is always SETUP.</summary>
    public string[] Tabs = Array.Empty<string>();
    public int ActiveTab;
}

/// <summary>A complete visual theme: palette, display font, and the paint
/// routines that give each theme its distinct decoration.</summary>
public class ThemeStyle
{
    public string Id = "";
    public string Name = "";

    // palette
    public Color Bg, Card, Border, BorderDim, Accent, AccentHi, AccentDeep,
                Text, Dim, Faint, Data, DataDeep, Good, Warn, Danger, Ink, Purple, Track;

    // fonts
    public string[] FontFiles = Array.Empty<string>();
    public string DisplayFamily = "";
    public string FallbackFamily = "Segoe UI";
    internal FontFamily? ResolvedFamily;

    protected Font DF(float basePx, FontStyle style) => Theme.FontFor(this, basePx, style);

    /// <summary>Chamfer cut used for the panel body + frame.</summary>
    public virtual int FrameCut => 18;
    /// <summary>Vertical space reserved at the top of a Card for the title.</summary>
    public virtual int TitleReserve => 64;

    public virtual void PaintFrame(Graphics g, Rectangle r)
    {
        using var path = Gfx.Chamfer(r, FrameCut);
        using var pen = new Pen(Border, 1.5f);
        g.DrawPath(pen, path);
    }

    public virtual void PaintTitle(Graphics g, string text, Rectangle panel)
    {
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        var font = DF(13, FontStyle.Bold);
        TextRenderer.DrawText(g, text, font, new Rectangle(panel.X, panel.Y + 8, panel.Width, 40), Accent, flags);
    }

    public virtual void PaintDivider(Graphics g, int x, int y, int width)
    {
        using var pen = new Pen(BorderDim, 1f);
        int mid = x + width / 2;
        g.DrawLine(pen, x, y, mid - 24, y);
        g.DrawLine(pen, mid + 24, y, x + width, y);
        using var b = new SolidBrush(Accent);
        Gfx.Diamond(g, mid, y, 4.5f, b);
    }

    public virtual void PaintNav(Graphics g, Rectangle r, string text, bool active)
    {
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        var font = DF(15, FontStyle.Bold);
        if (active)
        {
            using var path = Gfx.Chamfer(r, 12);
            using var bg = new SolidBrush(Card);
            g.FillPath(bg, path);
            using var pen = new Pen(Accent, 2f);
            g.DrawPath(pen, path);
            TextRenderer.DrawText(g, text, font, r, Accent, flags);
        }
        else
        {
            TextRenderer.DrawText(g, text, font, r, Dim, flags);
        }
    }

    public virtual void PaintBar(Graphics g, Rectangle track, float frac, Color fill)
        => Gfx.AngularBar(g, track, frac, fill, BorderDim);

    // ---- composition hooks: a theme owns the chrome, not just the colors ----

    /// <summary>Where this theme puts the page tabs.</summary>
    public virtual NavPlace NavLocation => NavPlace.Bottom;

    /// <summary>Full-bleed background for a region. Phase = the region's
    /// top-left relative to the form, so grids line up across panels.</summary>
    public virtual void PaintBackground(Graphics g, Rectangle r, Point phase) => g.Clear(Bg);

    /// <summary>Body path used to fill a Card.</summary>
    public virtual GraphicsPath CardPath(Rectangle r) => Gfx.Chamfer(r, FrameCut);

    /// <summary>Header strip. Default: title/machine left, clock/date right.</summary>
    public virtual void PaintHeader(Graphics g, HeaderInfo info)
    {
        var r = info.Bounds;
        g.Clear(Bg);
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(g, info.Title, DF(26, FontStyle.Bold), new Point(r.X + 30, r.Y + 10), Accent, flags);
        TextRenderer.DrawText(g, info.Machine, DF(13, FontStyle.Regular),
            new Point(r.X + 32, r.Y + 50), Dim, flags);
        // Right-aligned inside a rect ending 30px before the edge: cannot clip.
        var right = new Rectangle(r.X, r.Y + 2, r.Width - 30, 60);
        TextRenderer.DrawText(g, info.Clock, DF(30, FontStyle.Bold), right, Text,
            flags | TextFormatFlags.Right);
        var dateRect = new Rectangle(r.X, r.Y + 50, r.Width - 30, 34);
        TextRenderer.DrawText(g, info.Date, DF(13, FontStyle.Regular), dateRect, Dim,
            flags | TextFormatFlags.Right);
    }

    /// <summary>Tab hit-rects when NavLocation == InHeader (last tab = SETUP).</summary>
    public virtual Rectangle[] LayoutHeaderTabs(Rectangle header, int tabCount)
        => Array.Empty<Rectangle>();

    /// <summary>Gauge. Default: tick ring + glow arc + engraved number.</summary>
    public virtual void PaintGauge(Graphics g, Rectangle r, float frac,
        string number, string unit, string caption, Color valueColor)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Card);
        int size = Math.Min(r.Width, r.Height - 52) - 10;
        if (size < 60) return;
        float cx = r.X + r.Width / 2f;
        float cy = r.Y + 10 + size / 2f;
        float rTick = size / 2f - 4;

        for (int i = 0; i < 60; i++)
        {
            double a = i * Math.PI / 30.0;
            bool major = i % 5 == 0;
            float r1 = rTick - (major ? 13 : 7);
            using var pen = new Pen(major ? Accent : Faint, major ? 2f : 1f);
            g.DrawLine(pen,
                cx + (float)(Math.Cos(a) * r1), cy + (float)(Math.Sin(a) * r1),
                cx + (float)(Math.Cos(a) * rTick), cy + (float)(Math.Sin(a) * rTick));
        }
        using (var db = new SolidBrush(Accent))
            foreach (double a in new[] { 0.0, Math.PI / 2, Math.PI, 3 * Math.PI / 2 })
                Gfx.Diamond(g, cx + (float)(Math.Cos(a) * (rTick - 21)),
                    cy + (float)(Math.Sin(a) * (rTick - 21)), 4, db);

        float rArc = rTick - 30;
        var rect = new RectangleF(cx - rArc, cy - rArc, rArc * 2, rArc * 2);
        using (var track = new Pen(Track, 13))
            g.DrawArc(track, rect, 135, 270);
        if (frac > 0.005f)
        {
            using var glow = new Pen(Color.FromArgb(80, valueColor), 22);
            g.DrawArc(glow, rect, 135, 270 * frac);
            using var pen = new Pen(valueColor, 13);
            g.DrawArc(pen, rect, 135, 270 * frac);
        }

        var f = DF(Math.Max(24, size / 4), FontStyle.Bold);
        var tsz = TextRenderer.MeasureText(g, number, f);
        int ny = (int)(cy - tsz.Height / 2f) - 4;
        TextRenderer.DrawText(g, number, f, new Point((int)(cx - tsz.Width / 2f), ny), Text,
            TextFormatFlags.NoPrefix);
        var usz = TextRenderer.MeasureText(g, unit, DF(13, FontStyle.Regular));
        TextRenderer.DrawText(g, unit, DF(13, FontStyle.Regular),
            new Point((int)(cx - usz.Width / 2f), ny + tsz.Height - 2), Dim,
            TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        if (!string.IsNullOrEmpty(caption))
        {
            var csz = TextRenderer.MeasureText(g, caption, DF(13, FontStyle.Regular));
            TextRenderer.DrawText(g, caption, DF(13, FontStyle.Regular),
                new Point((int)(cx - csz.Width / 2f), (int)(cy + rTick + 6)), Dim,
                TextFormatFlags.NoPrefix);
        }
    }

    public virtual Color TempColor(float? c)
    {
        if (!c.HasValue) return Dim;
        if (c.Value >= 85) return Danger;
        if (c.Value >= 70) return Warn;
        if (c.Value >= 55) return Accent;
        return Good;
    }
}

/// <summary>Grimoire: dark tooled leather, aged-gold double frames, ribbon titles.</summary>
public sealed class GrimoireTheme : ThemeStyle
{
    public GrimoireTheme()
    {
        Id = "grimoire"; Name = "Grimoire";
        Bg = Color.FromArgb(15, 12, 8); Card = Color.FromArgb(23, 18, 12);
        Border = Color.FromArgb(128, 100, 48); BorderDim = Color.FromArgb(72, 56, 28);
        Accent = Color.FromArgb(212, 175, 55); AccentHi = Color.FromArgb(242, 220, 140); AccentDeep = Color.FromArgb(150, 116, 40);
        Text = Color.FromArgb(233, 221, 193); Dim = Color.FromArgb(152, 136, 102); Faint = Color.FromArgb(96, 84, 60);
        Data = Color.FromArgb(111, 245, 245); DataDeep = Color.FromArgb(44, 150, 160);
        Good = Color.FromArgb(125, 216, 125); Warn = Color.FromArgb(255, 122, 60); Danger = Color.FromArgb(255, 82, 82);
        Ink = Color.FromArgb(43, 31, 14); Purple = Color.FromArgb(186, 142, 255); Track = Color.FromArgb(46, 36, 22);
        FontFiles = new[] { "Cinzel-Regular.ttf", "Cinzel-Bold.ttf" };
        DisplayFamily = "Cinzel";
    }

    public override void PaintFrame(Graphics g, Rectangle r)
    {
        using var outer = Gfx.Chamfer(r, FrameCut);
        using var pen = new Pen(Border, 2f);
        g.DrawPath(pen, outer);
        var inner = r;
        inner.Inflate(-6, -6);
        if (inner.Width > 10 && inner.Height > 10)
        {
            using var ip = Gfx.Chamfer(inner, Math.Max(FrameCut - 5, 2));
            using var ipen = new Pen(BorderDim, 1f);
            g.DrawPath(ipen, ip);
        }
        using var db = new SolidBrush(Accent);
        float o = FrameCut / 2f + 1;
        Gfx.Diamond(g, r.X + o, r.Y + o, 4, db);
        Gfx.Diamond(g, r.Right - o, r.Y + o, 4, db);
        Gfx.Diamond(g, r.X + o, r.Bottom - o, 4, db);
        Gfx.Diamond(g, r.Right - o, r.Bottom - o, 4, db);
    }

    public override void PaintTitle(Graphics g, string text, Rectangle panel)
    {
        var font = DF(13, FontStyle.Bold);
        var ts = TextRenderer.MeasureText(g, text, font,
            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        int w = Math.Max(220, ts.Width + 90);
        int h = 36;
        int x = panel.X + panel.Width / 2 - w / 2;
        int y = panel.Y + 10;
        int pt = 22;
        var pts = new[]
        {
            new Point(x, y + h / 2),
            new Point(x + pt, y), new Point(x + w - pt, y),
            new Point(x + w, y + h / 2),
            new Point(x + w - pt, y + h), new Point(x + pt, y + h),
        };
        using var path = new GraphicsPath();
        path.AddLines(pts);
        path.CloseFigure();
        using var brush = new LinearGradientBrush(new Rectangle(x, y, w, h),
            AccentHi, AccentDeep, LinearGradientMode.Vertical);
        g.FillPath(brush, path);
        using var edge = new Pen(Color.FromArgb(120, Ink), 1.5f);
        g.DrawPath(edge, path);
        using var db = new SolidBrush(Ink);
        Gfx.Diamond(g, x + 12, y + h / 2f, 3.5f, db);
        Gfx.Diamond(g, x + w - 12, y + h / 2f, 3.5f, db);
        TextRenderer.DrawText(g, text, font, new Rectangle(x + pt, y, w - pt * 2, h), Ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    public override void PaintDivider(Graphics g, int x, int y, int width)
    {
        using var pen = new Pen(BorderDim, 1f);
        int mid = x + width / 2;
        g.DrawLine(pen, x, y, mid - 30, y);
        g.DrawLine(pen, mid + 30, y, x + width, y);
        using var b = new SolidBrush(Accent);
        Gfx.Diamond(g, mid, y, 5, b);
        using var b2 = new SolidBrush(Border);
        Gfx.Diamond(g, mid - 40, y, 2.5f, b2);
        Gfx.Diamond(g, mid + 40, y, 2.5f, b2);
    }

    public override void PaintNav(Graphics g, Rectangle r, string text, bool active)
    {
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        var font = DF(15, FontStyle.Bold);
        if (active)
        {
            using var path = Gfx.Chamfer(r, 14);
            using var bg = new SolidBrush(Color.FromArgb(44, 33, 18));
            g.FillPath(bg, path);
            using var pen = new Pen(Accent, 2f);
            g.DrawPath(pen, path);
            var inner = r;
            inner.Inflate(-5, -5);
            using var ip = Gfx.Chamfer(inner, 10);
            using var ipen = new Pen(BorderDim, 1f);
            g.DrawPath(ipen, ip);
            using var db = new SolidBrush(Accent);
            Gfx.Diamond(g, r.X + 16, r.Y + r.Height / 2f, 3.5f, db);
            Gfx.Diamond(g, r.Right - 16, r.Y + r.Height / 2f, 3.5f, db);
            TextRenderer.DrawText(g, text, font, r, Accent, flags);
        }
        else
        {
            TextRenderer.DrawText(g, text, font, r, Dim, flags);
        }
    }
}

/// <summary>Nebula HUD: deep-space navy, cyan hairlines, corner brackets, striped energy bars.</summary>
public sealed class NebulaTheme : ThemeStyle
{
    public NebulaTheme()
    {
        Id = "nebula"; Name = "Nebula HUD";
        Bg = Color.FromArgb(4, 6, 14); Card = Color.FromArgb(10, 15, 30);
        Border = Color.FromArgb(38, 110, 150); BorderDim = Color.FromArgb(20, 58, 84);
        Accent = Color.FromArgb(70, 230, 255); AccentHi = Color.FromArgb(181, 246, 255); AccentDeep = Color.FromArgb(26, 156, 196);
        Text = Color.FromArgb(214, 244, 255); Dim = Color.FromArgb(110, 147, 168); Faint = Color.FromArgb(51, 80, 95);
        Data = Color.FromArgb(70, 230, 255); DataDeep = Color.FromArgb(26, 156, 196);
        Good = Color.FromArgb(74, 222, 128); Warn = Color.FromArgb(255, 179, 64); Danger = Color.FromArgb(255, 90, 90);
        Ink = Color.FromArgb(3, 16, 24); Purple = Color.FromArgb(150, 140, 255); Track = Color.FromArgb(16, 40, 54);
        FontFiles = new[] { "orbitron-latin-400-normal.ttf", "orbitron-latin-700-normal.ttf" };
        DisplayFamily = "Orbitron";
    }

    public override int FrameCut => 22;
    public override int TitleReserve => 58;

    public override void PaintDivider(Graphics g, int x, int y, int width)
    {
        using var pen = new Pen(Border, 1f);
        int mid = x + width / 2;
        g.DrawLine(pen, x, y, mid - 20, y);
        g.DrawLine(pen, mid + 20, y, x + width, y);
        using var b = new SolidBrush(Accent);
        Gfx.Diamond(g, mid, y, 4, b);
        using var b2 = new SolidBrush(BorderDim);
        Gfx.Diamond(g, mid - 28, y, 2.2f, b2);
        Gfx.Diamond(g, mid + 28, y, 2.2f, b2);
    }

    public override void PaintNav(Graphics g, Rectangle r, string text, bool active)
    {
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        var font = DF(15, FontStyle.Bold);
        if (active)
        {
            Gfx.Brackets(g, r, 22, 3f, Accent);
            TextRenderer.DrawText(g, text, font, r, Accent, flags);
        }
        else
        {
            TextRenderer.DrawText(g, text, font, r, Dim, flags);
        }
    }

    public override void PaintBar(Graphics g, Rectangle track, float frac, Color fill)
        => Gfx.StripedBar(g, track, frac, fill, BorderDim);

    public override NavPlace NavLocation => NavPlace.InHeader;

    public override void PaintBackground(Graphics g, Rectangle r, Point phase)
    {
        g.Clear(Bg);
        using var pen = new Pen(Color.FromArgb(13, Accent), 1f);
        int step = 44;
        int x0 = -(phase.X % step), y0 = -(phase.Y % step);
        for (int x = x0; x <= r.Width; x += step)
            g.DrawLine(pen, r.X + x, r.Y, r.X + x, r.Bottom);
        for (int y = y0; y <= r.Height; y += step)
            g.DrawLine(pen, r.X, r.Y + y, r.Right, r.Y + y);
    }

    public override GraphicsPath CardPath(Rectangle r) => Gfx.Octagon(r, 26);

    public override void PaintFrame(Graphics g, Rectangle r)
    {
        using var path = Gfx.Octagon(r, 26);
        using var pen = new Pen(Border, 1.5f);
        g.DrawPath(pen, path);
        var inner = r; inner.Inflate(-6, -6);
        if (inner.Width > 20 && inner.Height > 20)
        {
            using var ip = Gfx.Octagon(inner, 20);
            using var ipen = new Pen(BorderDim, 1f);
            g.DrawPath(ipen, ip);
        }
        // HUD node squares on the top/bottom edge midpoints
        using var b = new SolidBrush(Accent);
        float s = 3.5f, mx = r.X + r.Width / 2f;
        g.FillRectangle(b, mx - s, r.Y - s, s * 2, s * 2);
        g.FillRectangle(b, mx - s, r.Bottom - s, s * 2, s * 2);
    }

    public override void PaintTitle(Graphics g, string text, Rectangle panel)
    {
        var font = DF(13, FontStyle.Bold);
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        int x = panel.X + 28, y = panel.Y + 12;
        TextRenderer.DrawText(g, text, font, new Point(x, y), Accent, flags);
        var ts = TextRenderer.MeasureText(g, text, font,
            new Size(int.MaxValue, int.MaxValue), flags);
        TextRenderer.DrawText(g, "//", font, new Point(x + ts.Width + 8, y), Dim, flags);
        using var pen = new Pen(Accent, 2f);
        g.DrawLine(pen, x, y + ts.Height + 5, x + ts.Width + 36, y + ts.Height + 5);
        using var dim = new Pen(BorderDim, 1f);
        g.DrawLine(dim, x + ts.Width + 44, y + ts.Height + 5, panel.Right - 28, y + ts.Height + 5);
    }

    public override Rectangle[] LayoutHeaderTabs(Rectangle header, int tabCount)
    {
        int w = 200, h = 56, gap = 10;
        int total = tabCount * w + (tabCount - 1) * gap;
        int x = header.X + (header.Width - total) / 2;
        int y = header.Y + (header.Height - h) / 2;
        var rects = new Rectangle[tabCount];
        for (int i = 0; i < tabCount; i++)
            rects[i] = new Rectangle(x + i * (w + gap), y, w, h);
        return rects;
    }

    public override void PaintHeader(Graphics g, HeaderInfo info)
    {
        var r = info.Bounds;
        g.Clear(Bg);
        var bar = new Rectangle(r.X + 16, r.Y + 8, r.Width - 32, r.Height - 16);
        using var path = Gfx.Chamfer(bar, 20);
        using var bg = new SolidBrush(Card);
        g.FillPath(bg, path);
        using var pen = new Pen(Border, 1.5f);
        g.DrawPath(pen, path);

        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var vc = flags | TextFormatFlags.VerticalCenter;
        // crosshair logo + brand
        float lcx = bar.X + 46, lcy = bar.Y + bar.Height / 2f;
        using var lp = new Pen(Accent, 1.5f);
        g.DrawEllipse(lp, lcx - 15, lcy - 15, 30, 30);
        g.DrawLine(lp, lcx - 21, lcy, lcx + 21, lcy);
        g.DrawLine(lp, lcx, lcy - 21, lcx, lcy + 21);
        TextRenderer.DrawText(g, "SYS MONITOR", DF(15, FontStyle.Bold),
            new Rectangle(bar.X + 74, bar.Y, 420, bar.Height), Accent, vc);

        // bracket tabs with / separators
        var tabs = LayoutHeaderTabs(r, info.Tabs.Length);
        var tabFont = DF(16, FontStyle.Bold);
        var tf = flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
        for (int i = 0; i < tabs.Length && i < info.Tabs.Length; i++)
        {
            bool active = i == info.ActiveTab;
            string label = active ? "[ " + info.Tabs[i] + " ]" : info.Tabs[i];
            TextRenderer.DrawText(g, label, tabFont, tabs[i], active ? AccentHi : Dim, tf);
            if (i < tabs.Length - 1)
            {
                var sep = new Rectangle(tabs[i].Right, tabs[i].Y,
                    tabs[i + 1].X - tabs[i].Right, tabs[i].Height);
                TextRenderer.DrawText(g, "/", tabFont, sep, Faint, tf);
            }
        }

        // clock + date, right-aligned: cannot clip
        var clockRect = new Rectangle(bar.X, bar.Y + 4, bar.Width - 30, 36);
        TextRenderer.DrawText(g, info.Clock, DF(22, FontStyle.Bold), clockRect, AccentHi,
            flags | TextFormatFlags.Right);
        var dateRect = new Rectangle(bar.X, bar.Y + 40, bar.Width - 30, 26);
        TextRenderer.DrawText(g, info.Date, DF(12, FontStyle.Regular), dateRect, Dim,
            flags | TextFormatFlags.Right);
    }

    public override void PaintGauge(Graphics g, Rectangle r, float frac,
        string number, string unit, string caption, Color valueColor)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Card);
        int size = Math.Min(r.Width, r.Height - 52) - 10;
        if (size < 60) return;
        float cx = r.X + r.Width / 2f;
        float cy = r.Y + 10 + size / 2f;
        float rO = size / 2f - 4;

        using (var op = new Pen(BorderDim, 1f))
            g.DrawEllipse(op, cx - rO, cy - rO, rO * 2, rO * 2);
        for (int i = 0; i < 72; i++)
        {
            double a = i * Math.PI / 36.0;
            bool major = i % 6 == 0;
            float r1 = rO - (major ? 10 : 5);
            using var pen = new Pen(major ? Accent : Faint, major ? 2f : 1f);
            g.DrawLine(pen,
                cx + (float)(Math.Cos(a) * r1), cy + (float)(Math.Sin(a) * r1),
                cx + (float)(Math.Cos(a) * rO), cy + (float)(Math.Sin(a) * rO));
        }

        // segmented HUD arc
        float rArc = rO - 22;
        var rect = new RectangleF(cx - rArc, cy - rArc, rArc * 2, rArc * 2);
        using (var tp = new Pen(Track, 8))
            g.DrawArc(tp, rect, 135, 270);
        int segs = 40, lit = (int)Math.Round(frac * segs);
        float span = 270f / segs;
        for (int s = 0; s < lit; s++)
        {
            using var pen = new Pen(s == lit - 1 ? AccentHi : valueColor, 8);
            g.DrawArc(pen, rect, 135 + s * span + 1, span - 2);
        }

        var f = DF(Math.Max(24, size / 4), FontStyle.Bold);
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var tsz = TextRenderer.MeasureText(g, number, f, new Size(int.MaxValue, int.MaxValue), flags);
        int ny = (int)(cy - tsz.Height / 2f) - 4;
        TextRenderer.DrawText(g, number, f, new Point((int)(cx - tsz.Width / 2f), ny), AccentHi, flags);
        var usz = TextRenderer.MeasureText(g, unit, DF(13, FontStyle.Regular));
        TextRenderer.DrawText(g, unit, DF(13, FontStyle.Regular),
            new Point((int)(cx - usz.Width / 2f), ny + tsz.Height - 2), Dim,
            TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        if (!string.IsNullOrEmpty(caption))
        {
            string cap = "// " + caption.ToUpperInvariant();
            var csz = TextRenderer.MeasureText(g, cap, DF(13, FontStyle.Regular));
            TextRenderer.DrawText(g, cap, DF(13, FontStyle.Regular),
                new Point((int)(cx - csz.Width / 2f), (int)(cy + rO + 4)), Dim,
                TextFormatFlags.NoPrefix);
        }
    }
}

/// <summary>Runestone: hammered dark metal, bronze frames, rivets, engraved titles.</summary>
public sealed class RunestoneTheme : ThemeStyle
{
    public RunestoneTheme()
    {
        Id = "runestone"; Name = "Runestone";
        Bg = Color.FromArgb(13, 11, 8); Card = Color.FromArgb(22, 19, 14);
        Border = Color.FromArgb(122, 98, 56); BorderDim = Color.FromArgb(66, 53, 30);
        Accent = Color.FromArgb(232, 179, 74); AccentHi = Color.FromArgb(246, 217, 138); AccentDeep = Color.FromArgb(154, 116, 42);
        Text = Color.FromArgb(232, 220, 192); Dim = Color.FromArgb(154, 138, 104); Faint = Color.FromArgb(94, 83, 64);
        Data = Color.FromArgb(245, 166, 35); DataDeep = Color.FromArgb(179, 106, 0);
        Good = Color.FromArgb(125, 216, 125); Warn = Color.FromArgb(255, 140, 66); Danger = Color.FromArgb(255, 82, 82);
        Ink = Color.FromArgb(36, 26, 8); Purple = Color.FromArgb(186, 142, 255); Track = Color.FromArgb(40, 32, 18);
        FontFiles = new[] { "metamorphous-latin-400-normal.ttf" };
        DisplayFamily = "Metamorphous";
    }

    public override int FrameCut => 10;
    public override int TitleReserve => 62;

    public override void PaintFrame(Graphics g, Rectangle r)
    {
        using var outer = Gfx.Chamfer(r, FrameCut);
        using var pen = new Pen(Border, 2f);
        g.DrawPath(pen, outer);
        var inner = r;
        inner.Inflate(-5, -5);
        if (inner.Width > 10 && inner.Height > 10)
        {
            using var ip = Gfx.Chamfer(inner, 6);
            using var ipen = new Pen(BorderDim, 1f);
            g.DrawPath(ipen, ip);
        }
        var disc = Color.FromArgb(42, 32, 18);
        for (int x = r.X + 36; x < r.Right - 24; x += 92)
        {
            Gfx.Rivet(g, x, r.Y + 10, 4, disc, AccentHi);
            Gfx.Rivet(g, x, r.Bottom - 10, 4, disc, AccentHi);
        }
        using var db = new SolidBrush(Accent);
        Gfx.Diamond(g, r.X + 7, r.Y + 7, 4, db);
        Gfx.Diamond(g, r.Right - 7, r.Y + 7, 4, db);
        Gfx.Diamond(g, r.X + 7, r.Bottom - 7, 4, db);
        Gfx.Diamond(g, r.Right - 7, r.Bottom - 7, 4, db);
    }

    public override void PaintTitle(Graphics g, string text, Rectangle panel)
    {
        var font = DF(15, FontStyle.Bold);
        var ts = TextRenderer.MeasureText(g, text, font,
            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        int cx = panel.X + panel.Width / 2;
        var rect = new Rectangle(cx - ts.Width / 2 - 10, panel.Y + 10, ts.Width + 20, ts.Height + 10);
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        var shadow = rect; shadow.Offset(1, 2);
        TextRenderer.DrawText(g, text, font, shadow, Color.FromArgb(20, 12, 4), flags);
        TextRenderer.DrawText(g, text, font, rect, Accent, flags);
        using var db = new SolidBrush(Accent);
        Gfx.Diamond(g, rect.X - 22, rect.Y + rect.Height / 2f, 4, db);
        Gfx.Diamond(g, rect.Right + 22, rect.Y + rect.Height / 2f, 4, db);
    }

    public override void PaintDivider(Graphics g, int x, int y, int width)
    {
        using var pen = new Pen(BorderDim, 1f);
        int mid = x + width / 2;
        g.DrawLine(pen, x, y, mid - 34, y);
        g.DrawLine(pen, mid + 34, y, x + width, y);
        using var b = new SolidBrush(Accent);
        Gfx.Diamond(g, mid, y, 5, b);
        using var tick = new Pen(Border, 1.5f);
        g.DrawLine(tick, mid - 24, y - 5, mid - 24, y + 5);
        g.DrawLine(tick, mid + 24, y - 5, mid + 24, y + 5);
    }

    public override void PaintNav(Graphics g, Rectangle r, string text, bool active)
    {
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        var font = DF(15, FontStyle.Bold);
        if (active)
        {
            using var path = Gfx.Chamfer(r, 10);
            using var bg = new SolidBrush(Gfx.Lighten(Card, 0.06f));
            g.FillPath(bg, path);
            using var pen = new Pen(Accent, 2f);
            g.DrawPath(pen, path);
            using var db = new SolidBrush(Accent);
            Gfx.Diamond(g, r.X + 15, r.Y + r.Height / 2f, 3.5f, db);
            Gfx.Diamond(g, r.Right - 15, r.Y + r.Height / 2f, 3.5f, db);
            TextRenderer.DrawText(g, text, font, r, Accent, flags);
        }
        else
        {
            TextRenderer.DrawText(g, text, font, r, Dim, flags);
        }
    }

    public override NavPlace NavLocation => NavPlace.InHeader;

    public override void PaintBackground(Graphics g, Rectangle r, Point phase)
    {
        g.Clear(Bg);
        using var path = new GraphicsPath();
        path.AddRectangle(r);
        using var brush = new PathGradientBrush(path);
        brush.CenterColor = Color.FromArgb(0, 0, 0, 0);
        brush.SurroundColors = new[] { Color.FromArgb(72, 0, 0, 0) };
        brush.CenterPoint = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
        g.FillRectangle(brush, r);
    }

    public override Rectangle[] LayoutHeaderTabs(Rectangle header, int tabCount)
    {
        int w = 208, h = 58, gap = 34;
        int total = tabCount * w + (tabCount - 1) * gap;
        int x = header.X + (header.Width - total) / 2;
        int y = header.Y + (header.Height - h) / 2;
        var rects = new Rectangle[tabCount];
        for (int i = 0; i < tabCount; i++)
            rects[i] = new Rectangle(x + i * (w + gap), y, w, h);
        return rects;
    }

    public override void PaintHeader(Graphics g, HeaderInfo info)
    {
        var r = info.Bounds;
        g.Clear(Bg);
        var bar = new Rectangle(r.X + 24, r.Y + 8, r.Width - 48, r.Height - 16);
        using var path = Gfx.Chamfer(bar, 12);
        using var bg = new LinearGradientBrush(bar, Gfx.Lighten(Card, 0.05f), Card, 90f);
        g.FillPath(bg, path);
        using var pen = new Pen(Border, 2f);
        g.DrawPath(pen, path);
        // rivets along the plate
        var disc = Color.FromArgb(30, 20, 10);
        for (int x = bar.X + 44; x < bar.Right - 34; x += 110)
        {
            Gfx.Rivet(g, x, bar.Y + 9, 4, disc, AccentHi);
            Gfx.Rivet(g, x, bar.Bottom - 9, 4, disc, AccentHi);
        }

        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var vc = flags | TextFormatFlags.VerticalCenter;
        // left: clock + date, bronze
        TextRenderer.DrawText(g, info.Clock + "   •   " + info.Date, DF(17, FontStyle.Bold),
            new Rectangle(bar.X + 30, bar.Y, 700, bar.Height), Accent, vc);

        // bronze plate tabs with rune separators
        var tabs = LayoutHeaderTabs(r, info.Tabs.Length);
        var tabFont = DF(16, FontStyle.Bold);
        var tf = flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
        for (int i = 0; i < tabs.Length && i < info.Tabs.Length; i++)
        {
            bool active = i == info.ActiveTab;
            var tr = tabs[i];
            using var tp = Gfx.Chamfer(tr, 10);
            using var tbg = new SolidBrush(active ? Gfx.Lighten(Card, 0.10f) : Card);
            g.FillPath(tbg, tp);
            using var tpen = new Pen(active ? Accent : BorderDim, active ? 2.5f : 1.5f);
            g.DrawPath(tpen, tp);
            if (active)
            {
                using var glow = new Pen(Color.FromArgb(90, Accent), 6f);
                g.DrawPath(glow, tp);
            }
            using var db = new SolidBrush(active ? Accent : BorderDim);
            Gfx.Diamond(g, tr.X + 17, tr.Y + tr.Height / 2f, 3.5f, db);
            Gfx.Diamond(g, tr.Right - 17, tr.Y + tr.Height / 2f, 3.5f, db);
            TextRenderer.DrawText(g, info.Tabs[i], tabFont, tr, active ? AccentHi : Dim, tf);
            if (i < tabs.Length - 1)
            {
                using var rb = new SolidBrush(Faint);
                Gfx.Diamond(g, tr.Right + 17, tr.Y + tr.Height / 2f, 4, rb);
            }
        }

        // right: machine name, right-aligned
        var right = new Rectangle(bar.X, bar.Y, bar.Width - 30, bar.Height);
        TextRenderer.DrawText(g, info.Machine, DF(14, FontStyle.Regular), right, Dim,
            flags | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }

    public override void PaintGauge(Graphics g, Rectangle r, float frac,
        string number, string unit, string caption, Color valueColor)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Card);
        int size = Math.Min(r.Width, r.Height - 52) - 10;
        if (size < 60) return;
        float cx = r.X + r.Width / 2f;
        float cy = r.Y + 10 + size / 2f;
        float rTick = size / 2f - 4;

        // rune band: 24 angular glyphs around the ring
        for (int i = 0; i < 24; i++)
        {
            double a = i * Math.PI / 12.0;
            float bx = cx + (float)Math.Cos(a) * (rTick - 8);
            float by = cy + (float)Math.Sin(a) * (rTick - 8);
            var st = g.Save();
            g.TranslateTransform(bx, by);
            g.RotateTransform((float)(a * 180.0 / Math.PI + 90));
            using var pen = new Pen(i % 2 == 0 ? Accent : Border, 1.6f);
            g.DrawLine(pen, 0, -6, 0, 6);
            if ((i & 1) == 0) g.DrawLine(pen, 0, -6, 5, 0);
            if ((i & 2) == 0) g.DrawLine(pen, 0, 0, 5, 6);
            g.Restore(st);
        }
        // bronze inner ring
        using (var ring = new Pen(Border, 2f))
            g.DrawEllipse(ring, cx - rTick + 20, cy - rTick + 20, (rTick - 20) * 2, (rTick - 20) * 2);

        // gradient progress arc
        float rArc = rTick - 36;
        var rect = new RectangleF(cx - rArc, cy - rArc, rArc * 2, rArc * 2);
        using (var tp2 = new Pen(Track, 13))
            g.DrawArc(tp2, rect, 135, 270);
        if (frac > 0.005f)
        {
            using var lg = new LinearGradientBrush(rect, Data, DataDeep, 45f);
            using var pen = new Pen(lg, 13);
            g.DrawArc(pen, rect, 135, 270 * frac);
        }

        // engraved number
        var f = DF(Math.Max(24, size / 4), FontStyle.Bold);
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var tsz = TextRenderer.MeasureText(g, number, f, new Size(int.MaxValue, int.MaxValue), flags);
        int ny = (int)(cy - tsz.Height / 2f) - 4;
        var shadow = new Point((int)(cx - tsz.Width / 2f) + 1, ny + 2);
        TextRenderer.DrawText(g, number, f, shadow, Color.FromArgb(20, 12, 4), flags);
        TextRenderer.DrawText(g, number, f, new Point((int)(cx - tsz.Width / 2f), ny), AccentHi, flags);
        var usz = TextRenderer.MeasureText(g, unit, DF(13, FontStyle.Regular));
        TextRenderer.DrawText(g, unit, DF(13, FontStyle.Regular),
            new Point((int)(cx - usz.Width / 2f), ny + tsz.Height - 2), Dim,
            TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        if (!string.IsNullOrEmpty(caption))
        {
            var csz = TextRenderer.MeasureText(g, caption, DF(13, FontStyle.Regular));
            TextRenderer.DrawText(g, caption, DF(13, FontStyle.Regular),
                new Point((int)(cx - csz.Width / 2f), (int)(cy + rTick + 6)), Dim,
                TextFormatFlags.NoPrefix);
        }
    }
}

/// <summary>Emberfall: cracked obsidian, molten lava-crack edges, glowing ember titles.</summary>
public sealed class EmberfallTheme : ThemeStyle
{
    public EmberfallTheme()
    {
        Id = "emberfall"; Name = "Emberfall";
        Bg = Color.FromArgb(10, 8, 8); Card = Color.FromArgb(18, 13, 13);
        Border = Color.FromArgb(122, 58, 30); BorderDim = Color.FromArgb(64, 34, 18);
        Accent = Color.FromArgb(255, 122, 26); AccentHi = Color.FromArgb(255, 179, 92); AccentDeep = Color.FromArgb(179, 74, 0);
        Text = Color.FromArgb(240, 216, 200); Dim = Color.FromArgb(160, 128, 104); Faint = Color.FromArgb(94, 74, 56);
        Data = Color.FromArgb(255, 140, 26); DataDeep = Color.FromArgb(204, 85, 0);
        Good = Color.FromArgb(125, 216, 125); Warn = Color.FromArgb(255, 179, 64); Danger = Color.FromArgb(255, 59, 48);
        Ink = Color.FromArgb(31, 14, 4); Purple = Color.FromArgb(200, 140, 255); Track = Color.FromArgb(46, 26, 16);
        FontFiles = new[] { "pirata-one-latin-400-normal.ttf" };
        DisplayFamily = "Pirata One";
    }

    public override int FrameCut => 26;
    public override int TitleReserve => 60;

    public override NavPlace NavLocation => NavPlace.InHeader;

    public override GraphicsPath CardPath(Rectangle r) => Gfx.ShardPath(r, 46);

    public override void PaintBackground(Graphics g, Rectangle r, Point phase)
    {
        g.Clear(Bg);
        // faint ember glows smoldering behind the shards
        foreach (var (fx, fy) in new[] { (0.15f, 0.82f), (0.85f, 0.18f), (0.55f, 0.55f) })
        {
            float gx = r.X + r.Width * fx, gy = r.Y + r.Height * fy, rad = 200;
            using var path = new GraphicsPath();
            path.AddEllipse(gx - rad, gy - rad, rad * 2, rad * 2);
            using var brush = new PathGradientBrush(path);
            brush.CenterColor = Color.FromArgb(24, Accent);
            brush.SurroundColors = new[] { Color.FromArgb(0, Accent) };
            g.FillEllipse(brush, gx - rad, gy - rad, rad * 2, rad * 2);
        }
    }

    public override void PaintFrame(Graphics g, Rectangle r)
    {
        var pts = Gfx.ShardCorners(r, 46);
        using var path = new GraphicsPath();
        path.AddPolygon(pts);
        using var pen = new Pen(BorderDim, 1.5f);
        g.DrawPath(pen, path);
        Gfx.CrackEdge(g, pts, Accent, 7, r);
    }

    public override Rectangle[] LayoutHeaderTabs(Rectangle header, int tabCount)
    {
        int w = 200, h = 56, gap = 34;
        int total = tabCount * w + (tabCount - 1) * gap;
        int x = header.X + (header.Width - total) / 2;
        int y = header.Y + (header.Height - h) / 2;
        var rects = new Rectangle[tabCount];
        for (int i = 0; i < tabCount; i++)
            rects[i] = new Rectangle(x + i * (w + gap), y, w, h);
        return rects;
    }

    public override void PaintHeader(Graphics g, HeaderInfo info)
    {
        var r = info.Bounds;
        g.Clear(Bg);
        // angular obsidian bar with a lava seam along its lower edge
        var bar = new Rectangle(r.X + 30, r.Y + 10, r.Width - 60, r.Height - 20);
        var pts = new[]
        {
            new PointF(bar.X + 60, bar.Y), new PointF(bar.Right - 30, bar.Y),
            new PointF(bar.Right - 90, bar.Bottom), new PointF(bar.X, bar.Bottom),
        };
        using var path = new GraphicsPath();
        path.AddPolygon(pts);
        using var bg = new SolidBrush(Card);
        g.FillPath(bg, path);
        Gfx.CrackEdge(g, pts, Accent, 21, bar);

        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var vc = flags | TextFormatFlags.VerticalCenter;
        TextRenderer.DrawText(g, "OBSIDIAN // MONITOR", DF(17, FontStyle.Bold),
            new Rectangle(bar.X + 96, bar.Y, 560, bar.Height), AccentHi, vc);

        // tabs, active one underlined in molten orange
        var tabs = LayoutHeaderTabs(r, info.Tabs.Length);
        var tabFont = DF(17, FontStyle.Bold);
        var tf = flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
        for (int i = 0; i < tabs.Length && i < info.Tabs.Length; i++)
        {
            bool active = i == info.ActiveTab;
            TextRenderer.DrawText(g, info.Tabs[i], tabFont, tabs[i],
                active ? AccentHi : Dim, tf);
            if (active)
            {
                using var up = new Pen(Accent, 3f);
                g.DrawLine(up, tabs[i].X + 34, tabs[i].Bottom - 6, tabs[i].Right - 34, tabs[i].Bottom - 6);
            }
            if (i < tabs.Length - 1)
            {
                var sep = new Rectangle(tabs[i].Right, tabs[i].Y,
                    tabs[i + 1].X - tabs[i].Right, tabs[i].Height);
                TextRenderer.DrawText(g, "/", tabFont, sep, Faint, tf);
            }
        }

        // clock, big ember numerals, right-aligned: cannot clip
        var clockRect = new Rectangle(bar.X, bar.Y + 2, bar.Width - 40, 40);
        TextRenderer.DrawText(g, info.Clock, DF(26, FontStyle.Bold), clockRect, AccentHi,
            flags | TextFormatFlags.Right);
        var dateRect = new Rectangle(bar.X, bar.Y + 42, bar.Width - 40, 26);
        TextRenderer.DrawText(g, info.Date, DF(13, FontStyle.Regular), dateRect, Dim,
            flags | TextFormatFlags.Right);
    }

    public override void PaintGauge(Graphics g, Rectangle r, float frac,
        string number, string unit, string caption, Color valueColor)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Card);
        int size = Math.Min(r.Width, r.Height - 52) - 10;
        if (size < 60) return;
        float cx = r.X + r.Width / 2f;
        float cy = r.Y + 10 + size / 2f;
        float rArc = size / 2f - 14;
        var rect = new RectangleF(cx - rArc, cy - rArc, rArc * 2, rArc * 2);

        // cracked ember ring: dark segments, molten where lit
        int segs = 26, lit = (int)Math.Round(frac * segs);
        float span = 270f / segs;
        for (int s = 0; s < segs; s++)
        {
            bool on = s < lit;
            if (on)
            {
                using var glow = new Pen(Color.FromArgb(70, Accent), 24);
                g.DrawArc(glow, rect, 135 + s * span + 1.5f, span - 3);
            }
            using var pen = new Pen(on ? (s >= lit - 2 ? AccentHi : Accent) : Track, 16);
            g.DrawArc(pen, rect, 135 + s * span + 1.5f, span - 3);
        }

        var f = DF(Math.Max(28, size / 3), FontStyle.Bold);
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var tsz = TextRenderer.MeasureText(g, number, f, new Size(int.MaxValue, int.MaxValue), flags);
        int ny = (int)(cy - tsz.Height / 2f) - 4;
        var glowRect = new Rectangle((int)(cx - tsz.Width / 2f), ny + 2, tsz.Width, tsz.Height);
        TextRenderer.DrawText(g, number, f, glowRect, AccentDeep, flags);
        TextRenderer.DrawText(g, number, f, new Point((int)(cx - tsz.Width / 2f), ny), AccentHi, flags);
        var usz = TextRenderer.MeasureText(g, unit, DF(13, FontStyle.Regular));
        TextRenderer.DrawText(g, unit, DF(13, FontStyle.Regular),
            new Point((int)(cx - usz.Width / 2f), ny + tsz.Height - 2), Dim,
            TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        if (!string.IsNullOrEmpty(caption))
        {
            var csz = TextRenderer.MeasureText(g, caption, DF(13, FontStyle.Regular));
            TextRenderer.DrawText(g, caption, DF(13, FontStyle.Regular),
                new Point((int)(cx - csz.Width / 2f), (int)(cy + rArc + 8)), Dim,
                TextFormatFlags.NoPrefix);
        }
    }

    public override void PaintTitle(Graphics g, string text, Rectangle panel)
    {
        var font = DF(16, FontStyle.Bold);
        var ts = TextRenderer.MeasureText(g, text, font,
            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        int cx = panel.X + panel.Width / 2;
        var rect = new Rectangle(cx - ts.Width / 2 - 10, panel.Y + 10, ts.Width + 20, ts.Height + 10);
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        var glow = rect; glow.Offset(0, 2);
        TextRenderer.DrawText(g, text, font, glow, AccentDeep, flags);
        TextRenderer.DrawText(g, text, font, rect, AccentHi, flags);
        using var gb = new SolidBrush(Color.FromArgb(90, Accent));
        Gfx.Diamond(g, rect.X - 24, rect.Y + rect.Height / 2f, 6, gb);
        Gfx.Diamond(g, rect.Right + 24, rect.Y + rect.Height / 2f, 6, gb);
        using var db = new SolidBrush(Accent);
        Gfx.Diamond(g, rect.X - 24, rect.Y + rect.Height / 2f, 3, db);
        Gfx.Diamond(g, rect.Right + 24, rect.Y + rect.Height / 2f, 3, db);
    }

    public override void PaintDivider(Graphics g, int x, int y, int width)
    {
        using var pen = new Pen(BorderDim, 1f);
        int mid = x + width / 2;
        g.DrawLine(pen, x, y, mid - 26, y);
        g.DrawLine(pen, mid + 26, y, x + width, y);
        using var glow = new SolidBrush(Color.FromArgb(80, Accent));
        Gfx.Diamond(g, mid, y, 8, glow);
        using var b = new SolidBrush(Accent);
        Gfx.Diamond(g, mid, y, 4.5f, b);
    }

    public override void PaintNav(Graphics g, Rectangle r, string text, bool active)
    {
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        var font = DF(15, FontStyle.Bold);
        if (active)
        {
            using var path = Gfx.Chamfer(r, 16);
            using var bg = new SolidBrush(Color.FromArgb(34, 18, 12));
            g.FillPath(bg, path);
            using var pen = new Pen(Accent, 2f);
            g.DrawPath(pen, path);
            TextRenderer.DrawText(g, text, font, r, AccentHi, flags);
        }
        else
        {
            TextRenderer.DrawText(g, text, font, r, Dim, flags);
        }
    }
}

/// <summary>Azeroth: iron-and-gold WoW chrome — squared double frames with
/// corner rivets, gold plaque titles, metal tab nav, and a gauge that is
/// just one huge rarity-colored numeral (no dial, no liquid, no furniture).
/// The numeral's color carries the thermal state.</summary>
public sealed class AzerothTheme : ThemeStyle
{
    public AzerothTheme()
    {
        Id = "azeroth"; Name = "Azeroth";
        Bg = Color.FromArgb(10, 7, 3); Card = Color.FromArgb(21, 14, 6);
        Border = Color.FromArgb(138, 106, 42); BorderDim = Color.FromArgb(84, 64, 30);
        Accent = Color.FromArgb(201, 150, 46); AccentHi = Color.FromArgb(242, 223, 154); AccentDeep = Color.FromArgb(138, 95, 20);
        Text = Color.FromArgb(232, 220, 192); Dim = Color.FromArgb(154, 124, 70); Faint = Color.FromArgb(96, 74, 38);
        Data = Color.FromArgb(61, 220, 88); DataDeep = Color.FromArgb(23, 165, 58);
        Good = Color.FromArgb(61, 220, 88); Warn = Color.FromArgb(255, 140, 40); Danger = Color.FromArgb(229, 72, 77);
        Ink = Color.FromArgb(42, 28, 7); Purple = Color.FromArgb(163, 53, 238); Track = Color.FromArgb(16, 11, 5);
        FontFiles = new[] { "metamorphous-latin-400-normal.ttf" };
        DisplayFamily = "Metamorphous";
    }

    public override int FrameCut => 4;
    public override int TitleReserve => 60;

    // Tabs stay at the bottom (default NavPlace.Bottom) as metal plates.

    /// <summary>Thermal state in WoW rarity colors. Blue through the 60s —
    /// the range the approved mock sits in — then green in the 70s, yellow,
    /// legendary orange, and red at the edge.</summary>
    public override Color TempColor(float? c)
    {
        if (!c.HasValue) return Dim;
        if (c.Value >= 90) return Color.FromArgb(240, 80, 80);
        if (c.Value >= 85) return Color.FromArgb(255, 128, 0);
        if (c.Value >= 78) return Color.FromArgb(255, 210, 60);
        if (c.Value >= 70) return Color.FromArgb(61, 220, 88);
        return Color.FromArgb(121, 199, 242);
    }

    public override void PaintBackground(Graphics g, Rectangle r, Point phase)
    {
        g.Clear(Bg);
        using var path = new GraphicsPath();
        path.AddRectangle(r);
        using var brush = new PathGradientBrush(path);
        brush.CenterColor = Color.FromArgb(33, 22, 10);
        brush.SurroundColors = new[] { Bg };
        brush.CenterPoint = new PointF(r.X + r.Width / 2f, r.Y + r.Height * 0.42f);
        g.FillRectangle(brush, r);
    }

    /// <summary>Cards are squared iron plates, not chamfered leather.</summary>
    public override GraphicsPath CardPath(Rectangle r)
    {
        var p = new GraphicsPath();
        p.AddRectangle(r);
        return p;
    }

    public override void PaintFrame(Graphics g, Rectangle r)
    {
        // faint warm sheen across the top of the plate
        int sheenH = Math.Min(72, r.Height / 3);
        if (r.Width > 24 && sheenH > 8)
        {
            var sg = new Rectangle(r.X + 3, r.Y + 3, r.Width - 6, sheenH);
            using var sheen = new LinearGradientBrush(sg,
                Color.FromArgb(15, AccentHi), Color.FromArgb(0, AccentHi), LinearGradientMode.Vertical);
            g.FillRectangle(sheen, sg);
        }
        using (var pen = new Pen(Border, 2f))
            g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
        var inner = r;
        inner.Inflate(-6, -6);
        if (inner.Width > 12 && inner.Height > 12)
        {
            using var ip = new Pen(BorderDim, 1f);
            g.DrawRectangle(ip, inner.X, inner.Y, inner.Width - 1, inner.Height - 1);
        }
        Gfx.Rivet(g, r.X + 13, r.Y + 13, 4.5f, Color.FromArgb(58, 42, 16), Color.FromArgb(216, 178, 94));
        Gfx.Rivet(g, r.Right - 14, r.Y + 13, 4.5f, Color.FromArgb(58, 42, 16), Color.FromArgb(216, 178, 94));
        Gfx.Rivet(g, r.X + 13, r.Bottom - 14, 4.5f, Color.FromArgb(58, 42, 16), Color.FromArgb(216, 178, 94));
        Gfx.Rivet(g, r.Right - 14, r.Bottom - 14, 4.5f, Color.FromArgb(58, 42, 16), Color.FromArgb(216, 178, 94));
    }

    /// <summary>Card titles sit on a gold plaque with pointed ends.</summary>
    public override void PaintTitle(Graphics g, string text, Rectangle panel)
    {
        var font = DF(13, FontStyle.Bold);
        var ts = TextRenderer.MeasureText(g, text, font,
            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        int w = Math.Max(200, ts.Width + 84);
        int h = 34;
        int x = panel.X + panel.Width / 2 - w / 2;
        int y = panel.Y + 10;
        int pt = 20;
        var pts = new[]
        {
            new Point(x, y + h / 2),
            new Point(x + pt, y), new Point(x + w - pt, y),
            new Point(x + w, y + h / 2),
            new Point(x + w - pt, y + h), new Point(x + pt, y + h),
        };
        using var path = new GraphicsPath();
        path.AddLines(pts);
        path.CloseFigure();
        using var brush = new LinearGradientBrush(new Rectangle(x, y, w, h),
            AccentHi, AccentDeep, LinearGradientMode.Vertical);
        g.FillPath(brush, path);
        using var edge = new Pen(Color.FromArgb(140, Ink), 1.5f);
        g.DrawPath(edge, path);
        TextRenderer.DrawText(g, text, font, new Rectangle(x + pt, y, w - pt * 2, h), Ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    public override void PaintDivider(Graphics g, int x, int y, int width)
    {
        using var pen = new Pen(BorderDim, 1f);
        int mid = x + width / 2;
        g.DrawLine(pen, x, y, mid - 26, y);
        g.DrawLine(pen, mid + 26, y, x + width, y);
        using var b = new SolidBrush(Accent);
        Gfx.Diamond(g, mid, y, 4.5f, b);
        using var b2 = new SolidBrush(Border);
        Gfx.Diamond(g, mid - 38, y, 2.5f, b2);
        Gfx.Diamond(g, mid + 38, y, 2.5f, b2);
    }

    /// <summary>Nav tabs are riveted metal plates; the active one glows gold.</summary>
    public override void PaintNav(Graphics g, Rectangle r, string text, bool active)
    {
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        var font = DF(15, FontStyle.Bold);
        if (active)
        {
            using var glow = new SolidBrush(Color.FromArgb(36, Accent));
            g.FillRectangle(glow, r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6);
        }
        using (var plate = new LinearGradientBrush(r,
            active ? Color.FromArgb(77, 56, 20) : Color.FromArgb(40, 29, 13),
            active ? Color.FromArgb(42, 29, 9) : Color.FromArgb(22, 16, 7),
            LinearGradientMode.Vertical))
            g.FillRectangle(plate, r);
        using (var pen = new Pen(active ? Accent : BorderDim, active ? 2f : 1f))
            g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
        if (active)
        {
            var inner = r;
            inner.Inflate(-5, -5);
            using var ip = new Pen(Color.FromArgb(120, AccentDeep), 1f);
            g.DrawRectangle(ip, inner.X, inner.Y, inner.Width - 1, inner.Height - 1);
        }
        TextRenderer.DrawText(g, text, font, r, active ? AccentHi : Dim, flags);
    }

    /// <summary>Squared inset track, sheen-graded fill, XP-style segment
    /// notches, bright leading edge. Fill color is the caller's.</summary>
    public override void PaintBar(Graphics g, Rectangle track, float frac, Color fill)
    {
        using (var tb = new SolidBrush(Track))
            g.FillRectangle(tb, track);
        using (var tp = new Pen(BorderDim, 1f))
            g.DrawRectangle(tp, track.X, track.Y, track.Width - 1, track.Height - 1);
        int fw = (int)((track.Width - 2) * Math.Clamp(frac, 0, 1));
        if (fw >= 2)
        {
            var fr = new Rectangle(track.X + 1, track.Y + 1, fw, track.Height - 2);
            using (var fb = new LinearGradientBrush(fr,
                Gfx.Lighten(fill, 0.35f), fill, LinearGradientMode.Vertical))
                g.FillRectangle(fb, fr);
            using (var notch = new Pen(Color.FromArgb(70, Color.Black), 1f))
                for (int x = fr.X + 22; x < fr.Right - 2; x += 22)
                    g.DrawLine(notch, x, fr.Y + 1, x, fr.Bottom - 2);
            using var edge = new Pen(Gfx.Lighten(fill, 0.55f), 1f);
            g.DrawLine(edge, fr.Right - 1, fr.Y, fr.Right - 1, fr.Bottom - 1);
        }
    }

    /// <summary>A real instrument: double gold ring, tick band, and a
    /// rarity-colored arc sweeping 270 degrees as temp climbs, number in
    /// the center in the same rarity color. Replaces the hero-numeral
    /// readout Alex rejected on the real panel (all scale, no shouting).</summary>
    public override void PaintGauge(Graphics g, Rectangle r, float frac,
        string number, string unit, string caption, Color valueColor)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Card);
        if (r.Width < 40 || r.Height < 40) return;

        const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        int capH = string.IsNullOrEmpty(caption) ? 4 : 30;
        int size = Math.Min(r.Width - 16, r.Height - capH - 12);
        if (size < 50) return;
        float cx = r.X + r.Width / 2f;
        float cy = r.Y + 8 + size / 2f;
        float rOut = size / 2f;

        // double gold ring
        using (var pen = new Pen(Border, 2.5f))
            g.DrawEllipse(pen, cx - rOut, cy - rOut, rOut * 2, rOut * 2);
        float rTick = rOut - 7;
        using (var pen = new Pen(BorderDim, 1f))
            g.DrawEllipse(pen, cx - rTick, cy - rTick, rTick * 2, rTick * 2);

        // tick band
        for (int i = 0; i < 60; i++)
        {
            double a = i * Math.PI / 30.0;
            bool major = i % 5 == 0;
            float r1 = rTick - (major ? 12 : 6);
            using var pen = new Pen(major ? Border : Faint, major ? 2f : 1f);
            g.DrawLine(pen,
                cx + (float)(Math.Cos(a) * r1), cy + (float)(Math.Sin(a) * r1),
                cx + (float)(Math.Cos(a) * rTick), cy + (float)(Math.Sin(a) * rTick));
        }
        using (var db = new SolidBrush(Accent))
            foreach (double a in new[] { 0.0, Math.PI / 2, Math.PI, 3 * Math.PI / 2 })
                Gfx.Diamond(g, cx + (float)(Math.Cos(a) * (rTick - 20)),
                    cy + (float)(Math.Sin(a) * (rTick - 20)), 3.5f, db);

        // rarity arc over its dark track
        float rArc = rTick - 26;
        var rect = new RectangleF(cx - rArc, cy - rArc, rArc * 2, rArc * 2);
        using (var track = new Pen(Track, 12))
            g.DrawArc(track, rect, 135, 270);
        if (frac > 0.005f)
        {
            using var glow = new Pen(Color.FromArgb(70, valueColor), 20);
            g.DrawArc(glow, rect, 135, 270 * frac);
            using var pen = new Pen(valueColor, 12);
            g.DrawArc(pen, rect, 135, 270 * frac);
        }

        // center number in the rarity color, unit at its foot
        float npx = Math.Max(20, size / 4f);
        var f = DF(npx, FontStyle.Regular);
        var nsz = TextRenderer.MeasureText(g, number, f, new Size(int.MaxValue, int.MaxValue), flags);
        if (nsz.Width > size - 30)
        {
            f = DF(Math.Max(14, npx * (size - 30f) / nsz.Width), FontStyle.Regular);
            nsz = TextRenderer.MeasureText(g, number, f, new Size(int.MaxValue, int.MaxValue), flags);
        }
        int nx = (int)(cx - nsz.Width / 2f);
        int ny = (int)(cy - nsz.Height / 2f) - 4;
        TextRenderer.DrawText(g, number, f, new Point(nx + 1, ny + 2), Color.Black, flags);
        TextRenderer.DrawText(g, number, f, new Point(nx, ny), valueColor, flags);
        var uf = DF(12, FontStyle.Regular);
        var usz = TextRenderer.MeasureText(g, unit, uf, new Size(int.MaxValue, int.MaxValue), flags);
        TextRenderer.DrawText(g, unit, uf,
            new Point((int)(cx - usz.Width / 2f), ny + nsz.Height - 1), Dim, flags);

        if (!string.IsNullOrEmpty(caption))
        {
            var cf = DF(12, FontStyle.Regular);
            var csz = TextRenderer.MeasureText(g, caption, cf, new Size(int.MaxValue, int.MaxValue), flags);
            int cxp = r.X + (r.Width - csz.Width) / 2;
            int cyp = r.Bottom - capH + 5;
            TextRenderer.DrawText(g, caption, cf, new Point(cxp, cyp), Dim, flags);
            using var db = new SolidBrush(Border);
            Gfx.Diamond(g, cxp - 16, cyp + csz.Height / 2f, 3, db);
            Gfx.Diamond(g, cxp + csz.Width + 16, cyp + csz.Height / 2f, 3, db);
        }
    }
}

#endregion

#region Theme — static facade over the active ThemeStyle

/// <summary>Active theme + scaled display fonts. Set once at startup from settings.</summary>
internal static class Theme
{
    public static readonly ThemeStyle[] All = new ThemeStyle[]
    {
        new GrimoireTheme(), new NebulaTheme(), new RunestoneTheme(), new EmberfallTheme(), new AzerothTheme(),
    };

    private static ThemeStyle _current = All[0];
    public static ThemeStyle Current => _current;

    public static void SetCurrent(string? id)
    {
        _current = All.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
    }

    // ---- palette passthroughs (existing page/control code compiles unchanged) ----
    public static Color Bg => _current.Bg;
    public static Color Card => _current.Card;
    public static Color Border => _current.Border;
    public static Color BorderDim => _current.BorderDim;
    public static Color Text => _current.Text;
    public static Color Dim => _current.Dim;

    /// <summary>Dim lifted ~55% toward Text. For small print (min/max/avg lines,
    /// footers): Dim alone is too faint to scan at arm's length on a dim strip.</summary>
    public static Color DimText
    {
        get
        {
            var d = _current.Dim;
            var t = _current.Text;
            return Color.FromArgb(
                d.R + (int)((t.R - d.R) * 0.55f),
                d.G + (int)((t.G - d.G) * 0.55f),
                d.B + (int)((t.B - d.B) * 0.55f));
        }
    }
    public static Color Faint => _current.Faint;
    public static Color Ink => _current.Ink;
    public static Color Purple => _current.Purple;
    public static Color Track => _current.Track;
    public static Color Accent => _current.Accent;

    // legacy names from the Grimoire-only era
    public static Color Gold => _current.Accent;
    public static Color GoldHi => _current.AccentHi;
    public static Color GoldDeep => _current.AccentDeep;
    public static Color Parchment => _current.Text;
    public static Color Arcane => _current.Data;
    public static Color ArcaneDeep => _current.DataDeep;
    public static Color Ember => _current.Warn;
    public static Color Cyan => _current.Data;
    public static Color Orange => _current.Warn;
    public static Color Green => _current.Good;
    public static Color Red => _current.Danger;

    // ---- paint delegation ----
    public static void PaintFrame(Graphics g, Rectangle r) => _current.PaintFrame(g, r);
    public static void PaintTitle(Graphics g, string text, Rectangle panel) => _current.PaintTitle(g, text, panel);
    public static void PaintDivider(Graphics g, int x, int y, int width) => _current.PaintDivider(g, x, y, width);
    public static void PaintNav(Graphics g, Rectangle r, string text, bool active) => _current.PaintNav(g, r, text, active);
    public static void PaintBar(Graphics g, Rectangle track, float frac, Color fill) => _current.PaintBar(g, track, frac, fill);
    public static void PaintBackground(Graphics g, Rectangle r, Point phase) => _current.PaintBackground(g, r, phase);
    public static void PaintHeader(Graphics g, HeaderInfo info) => _current.PaintHeader(g, info);
    public static Rectangle[] LayoutHeaderTabs(Rectangle header, int tabCount) => _current.LayoutHeaderTabs(header, tabCount);
    public static void PaintGauge(Graphics g, Rectangle r, float frac, string number, string unit, string caption, Color valueColor)
        => _current.PaintGauge(g, r, frac, number, unit, caption, valueColor);
    public static GraphicsPath CardPath(Rectangle r) => _current.CardPath(r);
    public static Color TempColor(float? c) => _current.TempColor(c);

    // ---- fonts ----
    private static readonly PrivateFontCollection _pfc = new();
    private static readonly Dictionary<string, Font> _fontCache = new();

    /// <summary>Global text-size multiplier (1.0 = 100%). Applied at startup from settings.</summary>
    public static float FontScale { get; set; } = 1f;

    /// <summary>Global layout scale: the UI is composed for a 2560x720
    /// design surface; this multiplies every layout dimension so the same
    /// composition fits displays of any size proportionally. Set once at
    /// startup from the target screen (see MainForm).</summary>
    public static float LayoutScale { get; set; } = 1f;

    /// <summary>Scale a design-pixel dimension by the layout scale (any
    /// screen) — spacing, paddings, chrome sizes.</summary>
    public static int Scale(int px) => Math.Max(1, (int)Math.Round(px * LayoutScale));

    /// <summary>Scale a design-pixel control height by layout scale AND the
    /// user's text-size setting.</summary>
    public static int ScaleH(int px) => Math.Max(1, (int)Math.Round(px * LayoutScale * FontScale));

    public static void InitFonts()
    {
        try
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Fonts");
            foreach (var theme in All)
            {
                foreach (string file in theme.FontFiles)
                {
                    string p = Path.Combine(dir, file);
                    if (File.Exists(p)) _pfc.AddFontFile(p);
                }
                theme.ResolvedFamily =
                    _pfc.Families.FirstOrDefault(f => f.Name.Equals(theme.DisplayFamily, StringComparison.OrdinalIgnoreCase));
            }
        }
        catch { /* fall back to system fonts */ }
    }

    internal static Font FontFor(ThemeStyle style, float basePx, FontStyle fstyle)
    {
        // Fonts follow the layout scale only up to 1.25x — beyond that (4K)
        // the fixed-size Settings dialog would overflow its rows, and dash
        // text reads fine slightly under its slots.
        float px = Math.Max(6, basePx * FontScale * Math.Min(LayoutScale, 1.25f));
        string key = $"{style.Id}|{px:0.0}|{(int)fstyle}";
        if (!_fontCache.TryGetValue(key, out var font))
        {
            try
            {
                font = style.ResolvedFamily != null
                    ? new Font(style.ResolvedFamily, px, fstyle, GraphicsUnit.Point)
                    : new Font(style.FallbackFamily, px, fstyle, GraphicsUnit.Point);
            }
            catch
            {
                font = new Font(style.FallbackFamily, px, fstyle, GraphicsUnit.Point);
            }
            _fontCache[key] = font;
        }
        return font;
    }

    private static Font Display(float basePx, FontStyle style) => FontFor(_current, basePx, style);

    public static Font TitleFont => Display(21, FontStyle.Bold);
    public static Font HeaderFont => Display(14, FontStyle.Bold);
    public static Font LabelFont => Display(12, FontStyle.Regular);
    public static Font SmallFont => FontFor(_current, 11, FontStyle.Regular); // NOTE: uses display family now
    public static Font MedNumberFont => Display(25, FontStyle.Bold);
    public static Font ClockFont => Display(30, FontStyle.Bold);
    public static Font RibbonFont => Display(13, FontStyle.Bold);
    public static Font NavFont => Display(15, FontStyle.Bold);

    /// <summary>Big engraved numeral for gauges (size scales with the gauge).</summary>
    public static Font GaugeNumberFont(int px) => Display(px, FontStyle.Bold);
}

#endregion
