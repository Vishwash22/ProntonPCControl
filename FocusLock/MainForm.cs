namespace FocusLock;

/// <summary>The parent's setup screen: pick an app, a duration and what happens afterwards, then lock.</summary>
internal sealed class MainForm : Form
{
    private readonly Settings settings = Settings.Load();
    private readonly ListView windows = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
    };
    private readonly NumericUpDown minutes = new() { Minimum = 0, Maximum = 24 * 60, Width = 80 };
    private readonly ComboBox endAction = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
    private readonly CheckBox watchOnly = new()
    {
        Text = "Watch only: also block all typing and clicking in the app until a parent unlocks",
        AutoSize = true,
    };
    private readonly Label pinStatus = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button pinButton = new() { AutoSize = true };
    private readonly Button start = new() { Text = "Start lock", AutoSize = true, Font = new Font(DefaultFont.FontFamily, 11f, FontStyle.Bold) };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.DarkGreen, MaximumSize = new Size(500, 0), Margin = new Padding(8, 8, 0, 0) };
    private LockSession? session;

    public MainForm()
    {
        Text = "FocusLock – parental app lock";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(680, 660);
        MinimumSize = new Size(560, 480);
        Font = new Font(DefaultFont.FontFamily, 9.5f);
        Padding = new Padding(12);

        windows.Columns.Add("Window", 400);
        windows.Columns.Add("App", 160);
        windows.SelectedIndexChanged += (_, _) => UpdateStartButton();

        var refresh = new Button { Text = "Refresh list", AutoSize = true };
        refresh.Click += (_, _) => RefreshWindows();

        endAction.Items.AddRange(new object[]
        {
            "Unlock the computer",
            "Lock Windows (go to the sign-in screen)",
            "Keep the screen blocked until a parent unlocks",
        });
        endAction.SelectedIndex = (int)settings.EndAction;
        minutes.Value = Math.Clamp(settings.Minutes, 0, (int)minutes.Maximum);
        watchOnly.Checked = settings.WatchOnly;

        pinButton.Click += (_, _) =>
        {
            using var dialog = new SetPinForm(settings);
            dialog.ShowDialog(this);
            UpdatePinStatus();
        };
        start.Click += (_, _) => StartLock();

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        layout.Controls.Add(Heading("1. Choose the app to lock to"));
        layout.Controls.Add(Hint("Open it first (for example, join the Zoom meeting), then press Refresh list."));
        layout.Controls.Add(windows);
        layout.Controls.Add(refresh);
        layout.Controls.Add(Heading("2. For how long"));
        layout.Controls.Add(Row(minutes, Hint("minutes   (0 = until a parent unlocks)")));
        layout.Controls.Add(Heading("3. When the time is up"));
        layout.Controls.Add(endAction);
        layout.Controls.Add(Heading("4. What the child can do"));
        layout.Controls.Add(watchOnly);
        layout.Controls.Add(Heading("Parent PIN"));
        layout.Controls.Add(Row(pinStatus, pinButton));
        layout.Controls.Add(Hint("To unlock early, press Ctrl + Alt + Shift + U and enter the PIN."));
        layout.Controls.Add(Row(start, status));
        for (int i = 0; i < layout.Controls.Count; i++)
        {
            layout.RowStyles.Add(layout.Controls[i] == windows
                ? new RowStyle(SizeType.Percent, 100)
                : new RowStyle(SizeType.AutoSize));
        }
        Controls.Add(layout);

        UpdatePinStatus();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RefreshWindows();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        e.Cancel = session != null && e.CloseReason == CloseReason.UserClosing;
        base.OnFormClosing(e);
    }

    private void RefreshWindows()
    {
        windows.BeginUpdate();
        windows.Items.Clear();
        foreach (var window in WindowFinder.ListAppWindows(new ProcessNames()))
        {
            var item = new ListViewItem(new[] { window.Title, window.ProcessName }) { Tag = window };
            windows.Items.Add(item);
            if (windows.SelectedItems.Count == 0
                && string.Equals(window.ProcessName, settings.LastProcessName, StringComparison.OrdinalIgnoreCase))
            {
                item.Selected = true;
            }
        }
        windows.EndUpdate();
        UpdateStartButton();
    }

    private void UpdatePinStatus()
    {
        pinStatus.Text = settings.HasPin ? "A PIN is set." : "No PIN yet. Set one before locking.";
        pinButton.Text = settings.HasPin ? "Change PIN…" : "Set PIN…";
        UpdateStartButton();
    }

    private void UpdateStartButton() => start.Enabled = settings.HasPin && windows.SelectedItems.Count == 1;

    private void StartLock()
    {
        var window = (AppWindow)windows.SelectedItems[0].Tag!;
        settings.LastProcessName = window.ProcessName;
        settings.Minutes = (int)minutes.Value;
        settings.EndAction = (EndAction)endAction.SelectedIndex;
        settings.WatchOnly = watchOnly.Checked;
        settings.Save();

        var options = new LockOptions(
            window.Handle,
            window.ProcessName,
            settings.Minutes == 0 ? null : TimeSpan.FromMinutes(settings.Minutes),
            settings.EndAction,
            settings.WatchOnly);
        var newSession = new LockSession(options, settings);
        newSession.Ended += message =>
        {
            session = null;
            Show();
            WindowFinder.ForceForeground(Handle);
            status.Text = message;
            RefreshWindows();
        };

        Hide();
        try
        {
            session = newSession;
            List<string> warnings = newSession.Start();
            if (warnings.Count > 0)
            {
                status.Text = "Some restrictions could not be applied: " + string.Join(" ", warnings);
            }
        }
        catch (InvalidOperationException ex)
        {
            session = null;
            Show();
            MessageBox.Show(this, ex.Message, "FocusLock", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Label Heading(string text) =>
        new() { Text = text, AutoSize = true, Font = new Font(DefaultFont.FontFamily, 10.5f, FontStyle.Bold), Margin = new Padding(0, 10, 0, 2) };

    private static Label Hint(string text) =>
        new() { Text = text, AutoSize = true, ForeColor = Color.DimGray, Anchor = AnchorStyles.Left };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        row.Controls.AddRange(controls);
        return row;
    }
}
