using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BgClicker
{
    public sealed class ThemedComboBox : ComboBox
    {
        public ThemedComboBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            IntegralHeight = false;
        }

        public Color FieldColor { get; set; } = Color.White;
        public Color TextColor { get; set; } = Color.Black;
        public Color BorderColor { get; set; } = Color.Gray;
        public Color HighlightColor { get; set; } = Color.FromArgb(0, 120, 212);
        public Color HighlightTextColor { get; set; } = Color.White;

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x000F && DropDownStyle == ComboBoxStyle.DropDownList)
            {
                PaintClosedBox();
            }
        }

        private void PaintClosedBox()
        {
            try
            {
                using (Graphics graphics = Graphics.FromHwnd(Handle))
                {
                    using (var background = new SolidBrush(FieldColor))
                    {
                        graphics.FillRectangle(background, ClientRectangle);
                    }

                    using (var border = new Pen(BorderColor))
                    {
                        graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
                    }

                    string text = SelectedIndex >= 0 ? GetItemText(SelectedItem) : "";
                    var textBounds = new Rectangle(5, 0, Math.Max(0, Width - 26), Height);
                    TextRenderer.DrawText(
                        graphics,
                        text,
                        Font,
                        textBounds,
                        TextColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                    int centerX = Width - 13;
                    int centerY = Height / 2 - 1;
                    using (var arrow = new Pen(TextColor, 1.6F))
                    {
                        graphics.DrawLine(arrow, centerX, centerY, centerX + 4, centerY + 4);
                        graphics.DrawLine(arrow, centerX + 8, centerY, centerX + 4, centerY + 4);
                        graphics.DrawLine(arrow, centerX, centerY, centerX + 8, centerY);
                    }
                }
            }
            catch
            {
            }
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color background = selected ? HighlightColor : FieldColor;

            using (var brush = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            if (e.Index >= 0)
            {
                Color foreground = selected ? HighlightTextColor : TextColor;
                var textBounds = new Rectangle(
                    e.Bounds.X + 4,
                    e.Bounds.Y,
                    Math.Max(0, e.Bounds.Width - 6),
                    e.Bounds.Height);

                TextRenderer.DrawText(
                    e.Graphics,
                    GetItemText(Items[e.Index]),
                    Font,
                    textBounds,
                    foreground,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
        }
    }

    public sealed class ThemedGroupBox : GroupBox
    {
        public ThemedGroupBox()
        {
            DoubleBuffered = true;
        }

        public Color BorderColor { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var background = new SolidBrush(BackColor))
            {
                e.Graphics.FillRectangle(background, ClientRectangle);
            }

            const int top = 12;
            using (var pen = new Pen(BorderColor))
            {
                e.Graphics.DrawRectangle(pen, 0, top, Width - 1, Height - top - 1);
            }

            SizeF titleSize = e.Graphics.MeasureString(Text, Font);
            int titleHeight = (int)Math.Ceiling(titleSize.Height);
            using (var background = new SolidBrush(BackColor))
            {
                e.Graphics.FillRectangle(
                    background,
                    8,
                    0,
                    titleSize.Width + 8,
                    Math.Max(top + 2, titleHeight + 2));
            }

            using (var textBrush = new SolidBrush(ForeColor))
            {
                e.Graphics.DrawString(Text, Font, textBrush, 10, 0);
            }
        }
    }

    public sealed class ThemePalette
    {
        public bool Dark;
        public Color Window;
        public Color Surface;
        public Color SurfaceAlt;
        public Color Input;
        public Color Border;
        public Color Text;
        public Color Muted;
        public Color Accent;
        public Color AccentText;
        public Color Danger;
        public Color ButtonHover;
        public Color ButtonPressed;
    }

    public static class ThemeManager
    {
        public static bool IsSystemDark()
        {
            try
            {
                object value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme",
                    1);
                return Convert.ToInt32(value) == 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsDark(string mode)
        {
            if (mode == "dark")
            {
                return true;
            }

            if (mode == "light")
            {
                return false;
            }

            return IsSystemDark();
        }

        public static ThemePalette Palette(bool dark)
        {
            if (dark)
            {
                return new ThemePalette
                {
                    Dark = true,
                    Window = Color.FromArgb(24, 24, 28),
                    Surface = Color.FromArgb(32, 32, 38),
                    SurfaceAlt = Color.FromArgb(43, 43, 52),
                    Input = Color.FromArgb(38, 38, 46),
                    Border = Color.FromArgb(66, 66, 78),
                    Text = Color.FromArgb(232, 232, 237),
                    Muted = Color.FromArgb(156, 156, 168),
                    Accent = Color.FromArgb(59, 130, 246),
                    AccentText = Color.White,
                    Danger = Color.FromArgb(239, 100, 100),
                    ButtonHover = Color.FromArgb(54, 54, 65),
                    ButtonPressed = Color.FromArgb(64, 64, 76)
                };
            }

            return new ThemePalette
            {
                Dark = false,
                Window = Color.FromArgb(243, 243, 243),
                Surface = Color.White,
                SurfaceAlt = Color.FromArgb(250, 250, 250),
                Input = Color.White,
                Border = Color.FromArgb(198, 198, 198),
                Text = Color.FromArgb(26, 26, 26),
                Muted = Color.FromArgb(96, 96, 96),
                Accent = Color.FromArgb(0, 120, 212),
                AccentText = Color.White,
                Danger = Color.FromArgb(176, 0, 32),
                ButtonHover = Color.FromArgb(236, 236, 236),
                ButtonPressed = Color.FromArgb(222, 222, 222)
            };
        }

        public static void Apply(Control root, ThemePalette palette)
        {
            root.BackColor = palette.Window;
            root.ForeColor = palette.Text;
            ApplyChildren(root, palette);
        }

        private static void ApplyChildren(Control parent, ThemePalette palette)
        {
            foreach (Control control in parent.Controls)
            {
                if (control is Button)
                {
                    StyleButton((Button)control, palette);
                }
                else if (control is Label)
                {
                    control.BackColor = parent.BackColor;
                    control.ForeColor = palette.Text;
                }
                else if (control is CheckBox || control is RadioButton)
                {
                    control.BackColor = parent.BackColor;
                    control.ForeColor = palette.Text;
                }
                else if (control is ThemedComboBox)
                {
                    var combo = (ThemedComboBox)control;
                    combo.FieldColor = palette.Input;
                    combo.TextColor = palette.Text;
                    combo.BorderColor = palette.Border;
                    combo.HighlightColor = palette.Accent;
                    combo.HighlightTextColor = palette.AccentText;
                    combo.BackColor = palette.Input;
                    combo.ForeColor = palette.Text;
                    combo.FlatStyle = FlatStyle.Flat;
                }
                else if (control is ComboBox)
                {
                    var combo = (ComboBox)control;
                    combo.BackColor = palette.Input;
                    combo.ForeColor = palette.Text;
                    combo.FlatStyle = palette.Dark ? FlatStyle.Flat : FlatStyle.Standard;
                }
                else if (control is NumericUpDown)
                {
                    control.BackColor = palette.Input;
                    control.ForeColor = palette.Text;
                    ((NumericUpDown)control).BorderStyle = BorderStyle.FixedSingle;
                }
                else if (control is ThemedGroupBox)
                {
                    control.BackColor = palette.Surface;
                    control.ForeColor = palette.Text;
                    ((ThemedGroupBox)control).BorderColor = palette.Border;
                }
                else if (control is GroupBox)
                {
                    control.BackColor = palette.Surface;
                    control.ForeColor = palette.Text;
                }
                else
                {
                    control.BackColor = parent.BackColor;
                    control.ForeColor = palette.Text;
                }

                if (control.Tag != null && string.Equals(control.Tag.ToString(), "hint", StringComparison.Ordinal))
                {
                    control.ForeColor = palette.Muted;
                }

                ApplyChildren(control, palette);
            }
        }

        public static void StyleButton(Button button, ThemePalette palette)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = palette.Border;
            button.BackColor = palette.SurfaceAlt;
            button.ForeColor = palette.Text;
            button.FlatAppearance.MouseOverBackColor = palette.ButtonHover;
            button.FlatAppearance.MouseDownBackColor = palette.ButtonPressed;
            button.UseVisualStyleBackColor = false;
        }
    }
}
