using static FocusLock.NativeMethods;

namespace FocusLock;

/// <summary>A small always-on-top countdown at the top of the screen. Mouse clicks pass straight through it.</summary>
internal sealed class CountdownForm : Form
{
    private readonly Label label = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Color.White,
        Font = new Font(DefaultFont.FontFamily, 10f, FontStyle.Bold),
    };

    public CountdownForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(30, 30, 30);
        Opacity = 0.8;
        Size = new Size(320, 30);
        Controls.Add(label);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOPMOST;
            return cp;
        }
    }

    public void SetMessage(string text)
    {
        label.Text = text;
        Rectangle screen = Screen.PrimaryScreen!.Bounds;
        Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Top);
        WindowFinder.KeepTopMost(Handle);
    }
}

/// <summary>Covers every screen when the locked app is gone or time is up, until a parent unlocks.</summary>
internal sealed class BlockerForm : Form
{
    private readonly Label message = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Color.White,
        Font = new Font(DefaultFont.FontFamily, 28f),
    };

    private bool allowClose;

    public event EventHandler? UnlockClicked;

    public BlockerForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(20, 24, 40);
        Bounds = SystemInformation.VirtualScreen;

        var unlock = new Button
        {
            Text = "Parent unlock…",
            AutoSize = true,
            Padding = new Padding(16, 6, 16, 6),
            Font = new Font(DefaultFont.FontFamily, 12f),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.None,
        };
        unlock.Click += (_, _) => UnlockClicked?.Invoke(this, EventArgs.Empty);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        layout.Controls.Add(message, 0, 0);
        layout.Controls.Add(unlock, 0, 1);
        Controls.Add(layout);
    }

    public void SetMessage(string text)
    {
        if (message.Text != text)
        {
            message.Text = text;
        }
        Bounds = SystemInformation.VirtualScreen;
        WindowFinder.KeepTopMost(Handle);
    }

    public void CloseForGood()
    {
        allowClose = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        e.Cancel = !allowClose && e.CloseReason == CloseReason.UserClosing;
        base.OnFormClosing(e);
    }
}
