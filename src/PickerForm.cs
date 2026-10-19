using System;
using System.Drawing;
using System.Windows.Forms;

namespace BgClicker
{
    /// <summary>
    /// Full-screen translucent overlay used to pick a window and a point.
    /// The click is consumed, so the window underneath does not receive it.
    /// </summary>
    public sealed class PickerForm : Form
    {
        public Point? PickedPoint { get; private set; }

        public PickerForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 0.35;
            Cursor = Cursors.Cross;
            ShowInTaskbar = false;
            KeyPreview = true;
            DoubleBuffered = true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                PickedPoint = PointToScreen(e.Location);
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            base.OnMouseDown(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            const string text = "Кликните по нужному окну   ·   Esc — отмена";
            using (var font = new Font("Segoe UI", 14F, FontStyle.Bold))
            {
                SizeF size = e.Graphics.MeasureString(text, font);
                float left = (Width - size.Width) / 2F;
                var box = new RectangleF(left - 18F, 22F, size.Width + 36F, size.Height + 18F);

                using (var background = new SolidBrush(Color.FromArgb(235, 0, 0, 0)))
                {
                    e.Graphics.FillRectangle(background, box);
                }

                using (var border = new Pen(Color.Lime, 2F))
                {
                    e.Graphics.DrawRectangle(border, box.X, box.Y, box.Width, box.Height);
                }

                using (var brush = new SolidBrush(Color.Lime))
                {
                    e.Graphics.DrawString(text, font, brush, left, 31F);
                }
            }

            using (var border = new Pen(Color.FromArgb(220, 0, 180, 0), 3F))
            {
                e.Graphics.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
            }
        }
    }
}
