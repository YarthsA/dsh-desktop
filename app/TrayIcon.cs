using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using Forms = System.Windows.Forms;

namespace DshDesktop;

public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public TrayIcon(Action showWindow, Action exitApp)
    {
        var menu = new Forms.ContextMenuStrip
        {
            BackColor = Color.FromArgb(28, 29, 34),
            ForeColor = Color.White,
            ShowImageMargin = false,
            DropShadowEnabled = false,
            // 必须关闭 AutoSize 并显式给定 Size：ContextMenuStrip 默认会
            // 按文本内容收缩，菜单项的固定宽度会被覆盖（表现为菜单没变大）
            AutoSize = false,
            Size = new Size(220, 112),
            Padding = new Forms.Padding(6),
            Renderer = new RoundedMenuRenderer(),
        };
        menu.Items.Add(MenuItem("显示", showWindow));
        menu.Items.Add(new Forms.ToolStripSeparator { Margin = new Forms.Padding(0) });
        menu.Items.Add(MenuItem("退出", exitApp));

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "DeepSeek Harness",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => showWindow();
    }

    private static Forms.ToolStripMenuItem MenuItem(string text, Action action)
        => new(text, null, (_, _) => action())
        {
            AutoSize = false,
            Width = 208,
            Height = 46,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = Forms.Padding.Empty,
            Margin = new Forms.Padding(0),
            Font = new Font("Microsoft YaHei UI", 12f),
        };

    private static Icon LoadIcon()
    {
        try
        {
            return new Icon(Path.Combine(AppContext.BaseDirectory, "dsh-icon.ico"));
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private sealed class DarkColorTable : Forms.ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(28, 29, 34);
        public override Color MenuItemSelected => Color.FromArgb(58, 58, 65);
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuBorder => Color.FromArgb(80, 82, 90);
        public override Color SeparatorDark => Color.FromArgb(58, 60, 68);
        public override Color SeparatorLight => Color.FromArgb(58, 60, 68);
        public override Color ImageMarginGradientBegin => Color.FromArgb(28, 29, 34);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(28, 29, 34);
        public override Color ImageMarginGradientEnd => Color.FromArgb(28, 29, 34);
    }

    private sealed class RoundedMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        private const int Radius = 10;

        public RoundedMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
        {
            var bounds = new Rectangle(Point.Empty, e.ToolStrip.Size);
            using var path = RoundedRect(bounds, Radius);
            using var brush = new SolidBrush(Color.FromArgb(245, 28, 29, 34));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using var path = RoundedRect(bounds, Radius);
            using var pen = new Pen(Color.FromArgb(96, 92, 94, 102), 1);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(pen, path);
        }

        // 悬停高亮：圆角灰色块，与主窗口标题栏按钮的悬停色（#3A3A41）保持一致
        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected)
            {
                base.OnRenderMenuItemBackground(e);
                return;
            }
            var r = e.Item.Bounds;
            var bounds = new Rectangle(r.X + 2, r.Y + 1, r.Width - 4, r.Height - 2);
            using var path = RoundedRect(bounds, 6);
            using var brush = new SolidBrush(Color.FromArgb(58, 58, 65));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
        }

        // 强制水平+垂直居中，规避 ToolStripMenuItem TextAlign 在部分布局下的偏差
        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextFormat |= Forms.TextFormatFlags.HorizontalCenter | Forms.TextFormatFlags.VerticalCenter;
            base.OnRenderItemText(e);
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
