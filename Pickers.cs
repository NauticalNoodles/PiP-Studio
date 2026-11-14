using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PipStudio
{
    // =================================================================================
    //  Click-a-window picker
    // =================================================================================
    class HighlightForm : Form
    {
        static readonly Color Key = Color.FromArgb(255, 1, 0, 1);
        string label = "";
        readonly float s;

        public HighlightForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Key;
            TransparencyKey = Key;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            s = U.ScaleAt(Cursor.Position);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
                return cp;
            }
        }

        public void ShowFor(Rectangle r, string text)
        {
            label = text;
            Bounds = r;
            if (!Visible) Show();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Key);
            int b = Math.Max(3, (int)(4 * s));
            using (var p = new Pen(Theme.Accent, b)) g.DrawRectangle(p, b / 2, b / 2, ClientSize.Width - b, ClientSize.Height - b);
            Font f = U.UIBold(13 * s);
            Size ts = LT.MeasureText(label, f);
            var lr = new Rectangle(b, b, Math.Min(ClientSize.Width - 2 * b, ts.Width + (int)(16 * s)), ts.Height + (int)(8 * s));
            using (var br = new SolidBrush(Theme.Accent)) g.FillRectangle(br, lr);
            LT.DrawText(g, label, f, lr, Theme.OnAccent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    class HintForm : Form
    {
        readonly string text;
        readonly float s;

        public HintForm(string t)
        {
            text = t;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(24, 24, 28);
            Opacity = 0.94;
            s = U.ScaleAt(Cursor.Position);
            Size ts = LT.MeasureText(text, U.UIBold(14 * s));
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size sz = new Size(ts.Width + (int)(40 * s), ts.Height + (int)(22 * s));
            Bounds = new Rectangle(wa.Left + (wa.Width - sz.Width) / 2, wa.Top + (int)(28 * s), sz.Width, sz.Height);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); U.RoundCorners(Handle, true); }

        protected override void OnPaint(PaintEventArgs e)
        {
            LT.DrawText(e.Graphics, text, U.UIBold(14 * s), ClientRectangle, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    class ScreenWindowPicker : Form
    {
        readonly HighlightForm hl = new HighlightForm();
        readonly HintForm hint = new HintForm("Click any window to open it in PiP  ·  Esc to cancel");
        readonly System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
        IntPtr cur = IntPtr.Zero;
        public IntPtr Selected = IntPtr.Zero;

        public ScreenWindowPicker()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            BackColor = Color.Black;
            Opacity = 0.01;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            t.Interval = 30;
            t.Tick += (o, e) => Track();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            hl.Owner = this;
            hint.Owner = this;
            hint.Show();
            t.Start();
            U.ForceForeground(Handle);
        }

        void Track()
        {
            IntPtr h = WinEnum.WindowAt(Cursor.Position);
            if (h == cur) return;
            cur = h;
            if (h == IntPtr.Zero) { hl.Hide(); return; }
            string proc = U.ProcessName(h);
            hl.ShowFor(U.FrameRect(h), U.Trunc(U.WindowTitle(h), 60) + (proc.Length > 0 ? "  ·  " + proc : ""));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                Selected = WinEnum.WindowAt(Cursor.Position);
                DialogResult = Selected != IntPtr.Zero ? DialogResult.OK : DialogResult.Cancel;
            }
            else DialogResult = DialogResult.Cancel;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            t.Stop();
            t.Dispose();
            hl.Close();
            hint.Close();
            base.OnFormClosed(e);
        }

        public static IntPtr Pick()
        {
            App.I.ModalOpen++;
            try
            {
                using (var p = new ScreenWindowPicker())
                    return p.ShowDialog() == DialogResult.OK ? p.Selected : IntPtr.Zero;
            }
            finally { App.I.ModalOpen--; }
        }
    }

    // =================================================================================
    //  Region selector (crop editor & screen-region picker)
    // =================================================================================
    class RegionSelector : Form
    {
        readonly Bitmap img;
        readonly Bitmap dimmed;
        readonly bool fullMode;
        readonly string hint;
        readonly float s;
        Rectangle imgRect;
        RectangleF sel = RectangleF.Empty;
        int drag = 0;        // 0 none, 1 new, 2 move, 3 resize
        int handle = -1;
        PointF anchor, downImg;
        RectangleF selStart;
        double aspect = 0;
        string aspectName = "Free";
        public RectangleF ResultFraction;

        public RegionSelector(Bitmap image, Rectangle bounds, RectangleF initFrac, bool fullscreenImage, string hintText)
        {
            img = image;
            fullMode = fullscreenImage;
            hint = hintText;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            BackColor = Color.FromArgb(14, 14, 17);
            Cursor = Cursors.Cross;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            s = U.ScaleAt(Cursor.Position);

            dimmed = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(dimmed))
            {
                g.DrawImage(img, 0, 0, img.Width, img.Height);
                using (var b = new SolidBrush(Color.FromArgb(fullMode ? 120 : 150, 0, 0, 0))) g.FillRectangle(b, 0, 0, img.Width, img.Height);
            }

            bool isFull = initFrac.IsEmpty || (initFrac.X <= 0.0005f && initFrac.Y <= 0.0005f && initFrac.Width >= 0.999f && initFrac.Height >= 0.999f);
            if (!isFull) sel = new RectangleF(initFrac.X * img.Width, initFrac.Y * img.Height, initFrac.Width * img.Width, initFrac.Height * img.Height);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ComputeImgRect();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            U.ForceForeground(Handle);
        }

        void ComputeImgRect()
        {
            var c = ClientRectangle;
            if (fullMode) { imgRect = new Rectangle(0, 0, img.Width, img.Height); return; }
            int padTop = (int)(96 * s);
            var area = new Rectangle(c.X + (int)(40 * s), c.Y + padTop, c.Width - (int)(80 * s), c.Height - padTop - (int)(50 * s));
            double k = Math.Min(area.Width / (double)img.Width, area.Height / (double)img.Height);
            if (k > 2) k = 2;
            int w = Math.Max(1, (int)(img.Width * k)), h = Math.Max(1, (int)(img.Height * k));
            imgRect = new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
        }

        float K { get { return imgRect.Width / (float)img.Width; } }

        PointF ToImg(Point p)
        {
            float x = (p.X - imgRect.X) / K, y = (p.Y - imgRect.Y) / K;
            return new PointF(U.Clamp(x, 0f, img.Width), U.Clamp(y, 0f, img.Height));
        }

        Rectangle ToView(RectangleF r)
        {
            return Rectangle.Round(new RectangleF(imgRect.X + r.X * K, imgRect.Y + r.Y * K, r.Width * K, r.Height * K));
        }

        Rectangle[] Handles(Rectangle v)
        {
            int hs = (int)(10 * s);
            int mx = v.Left + v.Width / 2, my = v.Top + v.Height / 2;
            var pts = new[] {
                new Point(v.Left, v.Top), new Point(mx, v.Top), new Point(v.Right, v.Top), new Point(v.Right, my),
                new Point(v.Right, v.Bottom), new Point(mx, v.Bottom), new Point(v.Left, v.Bottom), new Point(v.Left, my) };
            return pts.Select(p => new Rectangle(p.X - hs / 2, p.Y - hs / 2, hs, hs)).ToArray();
        }

        int HitHandle(Point p)
        {
            if (sel.IsEmpty) return -1;
            var hs = Handles(ToView(sel));
            for (int i = 0; i < hs.Length; i++)
            {
                var r = hs[i];
                r.Inflate((int)(4 * s), (int)(4 * s));
                if (r.Contains(p)) return i;
            }
            return -1;
        }

        RectangleF ClampRect(RectangleF r)
        {
            float l = Math.Max(0, r.Left), t = Math.Max(0, r.Top), rr = Math.Min(img.Width, r.Right), b = Math.Min(img.Height, r.Bottom);
            if (rr < l) rr = l;
            if (b < t) b = t;
            return RectangleF.FromLTRB(l, t, rr, b);
        }

        RectangleF FromAnchor(PointF a, PointF p)
        {
            float w = p.X - a.X, h = p.Y - a.Y;
            if (aspect > 0)
            {
                float sw = w < 0 ? -1 : 1, sh = h < 0 ? -1 : 1;
                float hw = (float)(Math.Abs(h) * aspect);
                if (hw > Math.Abs(w)) w = sw * hw;
                else h = sh * (float)(Math.Abs(w) / aspect);
            }
            var r = RectangleF.FromLTRB(Math.Min(a.X, a.X + w), Math.Min(a.Y, a.Y + h), Math.Max(a.X, a.X + w), Math.Max(a.Y, a.Y + h));
            return ClampRect(r);
        }

        RectangleF ResizeSel(RectangleF r0, int h, float dx, float dy)
        {
            float l = r0.Left, t = r0.Top, r = r0.Right, b = r0.Bottom;
            if (h == 0 || h == 6 || h == 7) l += dx;
            if (h == 2 || h == 3 || h == 4) r += dx;
            if (h == 0 || h == 1 || h == 2) t += dy;
            if (h == 4 || h == 5 || h == 6) b += dy;
            if (aspect > 0 && (h == 0 || h == 2 || h == 4 || h == 6))
            {
                float hh = (float)(Math.Abs(r - l) / aspect);
                if (h == 0 || h == 2) t = b - hh;
                else b = t + hh;
            }
            return ClampRect(RectangleF.FromLTRB(Math.Min(l, r), Math.Min(t, b), Math.Max(l, r), Math.Max(t, b)));
        }

        void InvalidateSel(RectangleF before)
        {
            int pad = (int)(70 * s);
            Rectangle a = before.IsEmpty ? Rectangle.Empty : ToView(before);
            Rectangle b = sel.IsEmpty ? Rectangle.Empty : ToView(sel);
            Rectangle u = a.IsEmpty ? b : (b.IsEmpty ? a : Rectangle.Union(a, b));
            if (u.IsEmpty) return;
            u.Inflate(pad, pad);
            Invalidate(u);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; return; }
            if (e.Button != MouseButtons.Left) return;
            RectangleF before = sel;
            downImg = ToImg(e.Location);
            selStart = sel;
            int h = HitHandle(e.Location);
            if (h >= 0) { drag = 3; handle = h; }
            else if (!sel.IsEmpty && ToView(sel).Contains(e.Location)) drag = 2;
            else { drag = 1; anchor = downImg; sel = new RectangleF(anchor, SizeF.Empty); }
            InvalidateSel(before);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (drag == 0) { UpdateCursor(e.Location); return; }
            RectangleF before = sel;
            PointF p = ToImg(e.Location);
            if (drag == 1) sel = FromAnchor(anchor, p);
            else if (drag == 2)
            {
                var r = selStart;
                r.X = U.Clamp(r.X + (p.X - downImg.X), 0f, img.Width - r.Width);
                r.Y = U.Clamp(r.Y + (p.Y - downImg.Y), 0f, img.Height - r.Height);
                sel = r;
            }
            else if (drag == 3) sel = ResizeSel(selStart, handle, p.X - downImg.X, p.Y - downImg.Y);
            InvalidateSel(before);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (drag == 1 && (sel.Width * K < 6 || sel.Height * K < 6))
            {
                RectangleF before = sel;
                sel = RectangleF.Empty;
                InvalidateSel(before);
            }
            drag = 0;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left) Confirm();
        }

        void UpdateCursor(Point p)
        {
            int h = HitHandle(p);
            Cursor c = Cursors.Cross;
            if (h == 0 || h == 4) c = Cursors.SizeNWSE;
            else if (h == 2 || h == 6) c = Cursors.SizeNESW;
            else if (h == 1 || h == 5) c = Cursors.SizeNS;
            else if (h == 3 || h == 7) c = Cursors.SizeWE;
            else if (!sel.IsEmpty && ToView(sel).Contains(p)) c = Cursors.SizeAll;
            if (Cursor != c) Cursor = c;
        }

        void SetAspect(double a, string name)
        {
            aspect = a;
            aspectName = name;
            if (a > 0 && !sel.IsEmpty)
            {
                float cx = sel.X + sel.Width / 2, cy = sel.Y + sel.Height / 2;
                float w = sel.Width, h = (float)(w / a);
                if (h > img.Height) { h = img.Height; w = (float)(h * a); }
                sel = ClampRect(new RectangleF(cx - w / 2, cy - h / 2, w, h));
            }
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Escape: DialogResult = DialogResult.Cancel; break;
                case Keys.Enter: Confirm(); break;
                case Keys.F: sel = new RectangleF(0, 0, img.Width, img.Height); Invalidate(); break;
                case Keys.D1: case Keys.NumPad1: SetAspect(0, "Free"); break;
                case Keys.D2: case Keys.NumPad2: SetAspect(16.0 / 9.0, "16:9"); break;
                case Keys.D3: case Keys.NumPad3: SetAspect(4.0 / 3.0, "4:3"); break;
                case Keys.D4: case Keys.NumPad4: SetAspect(1.0, "1:1"); break;
                case Keys.D5: case Keys.NumPad5: SetAspect(9.0 / 16.0, "9:16"); break;
            }
        }

        void Confirm()
        {
            if (sel.IsEmpty || sel.Width < 2 || sel.Height < 2)
            {
                if (fullMode) return;
                sel = new RectangleF(0, 0, img.Width, img.Height);
            }
            ResultFraction = new RectangleF(sel.X / img.Width, sel.Y / img.Height, sel.Width / img.Width, sel.Height / img.Height);
            DialogResult = DialogResult.OK;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.InterpolationMode = fullMode ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(dimmed, imgRect);
            if (!sel.IsEmpty)
            {
                Rectangle v = ToView(sel);
                g.DrawImage(img, v, sel, GraphicsUnit.Pixel);
                g.PixelOffsetMode = PixelOffsetMode.Default;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Theme.Accent, Math.Max(2f, 2f * s))) g.DrawRectangle(pen, v);
                using (var thirds = new Pen(Color.FromArgb(70, 255, 255, 255), 1f))
                {
                    for (int i = 1; i < 3; i++)
                    {
                        g.DrawLine(thirds, v.Left + v.Width * i / 3, v.Top, v.Left + v.Width * i / 3, v.Bottom);
                        g.DrawLine(thirds, v.Left, v.Top + v.Height * i / 3, v.Right, v.Top + v.Height * i / 3);
                    }
                }
                using (var hb = new SolidBrush(Color.White))
                using (var hp = new Pen(Theme.Accent, 1.5f))
                    foreach (var h in Handles(v)) { g.FillRectangle(hb, h); g.DrawRectangle(hp, h); }

                string sz = (int)Math.Round(sel.Width) + " × " + (int)Math.Round(sel.Height) + (aspect > 0 ? "  ·  " + aspectName : "");
                Font f = U.UIBold(12 * s);
                Size ts = LT.MeasureText(sz, f);
                var lr = new Rectangle(v.Left, v.Bottom + (int)(8 * s), ts.Width + (int)(16 * s), ts.Height + (int)(6 * s));
                if (lr.Bottom > ClientSize.Height - 4) lr.Y = v.Bottom - lr.Height - (int)(8 * s);
                using (var path = U.RoundRect(lr, 4 * s))
                using (var b = new SolidBrush(Color.FromArgb(230, 20, 20, 24)))
                    g.FillPath(b, path);
                LT.DrawText(g, sz, f, lr, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            DrawHint(g);
        }

        void DrawHint(Graphics g)
        {
            string line2 = "Enter or double-click to confirm  ·  Esc to cancel  ·  F = everything  ·  Shape: 1 Free  2 16:9  3 4:3  4 1:1  5 9:16  (now: " + aspectName + ")";
            Font f1 = U.UIBold(14 * s), f2 = U.UI(12 * s);
            Size t1 = LT.MeasureText(hint, f1), t2 = LT.MeasureText(line2, f2);
            int w = Math.Max(t1.Width, t2.Width) + (int)(36 * s), h = t1.Height + t2.Height + (int)(22 * s);
            Rectangle area = ClientRectangle;
            if (fullMode)
            {
                Rectangle pb = Screen.PrimaryScreen.Bounds;
                area = new Rectangle(pb.X - Bounds.X, pb.Y - Bounds.Y, pb.Width, pb.Height);
            }
            var r = new Rectangle(area.X + (area.Width - w) / 2, area.Y + (int)(24 * s), w, h);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = U.RoundRect(r, 10 * s))
            using (var b = new SolidBrush(Color.FromArgb(235, 22, 22, 26)))
                g.FillPath(b, path);
            LT.DrawText(g, hint, f1, new Rectangle(r.X, r.Y + (int)(10 * s), r.Width, t1.Height), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            LT.DrawText(g, line2, f2, new Rectangle(r.X, r.Y + (int)(12 * s) + t1.Height, r.Width, t2.Height), Color.FromArgb(175, 175, 188),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            dimmed.Dispose();
            base.OnFormClosed(e);
        }
    }

}
