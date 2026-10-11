namespace FocusLock;

/// <summary>Asks for the parent PIN. Stays open until <c>validate</c> accepts it (returns null) or the user cancels.</summary>
internal sealed class PinForm : Form
{
    private readonly TextBox pin = new() { UseSystemPasswordChar = true, Width = 220, Font = new Font(DefaultFont.FontFamily, 14f) };
    private readonly Label error = new() { AutoSize = true, ForeColor = Color.Firebrick };

    public PinForm(string prompt, Func<string, string?> validate)
    {
        Text = "FocusLock";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var ok = new Button { Text = "Unlock", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) =>
        {
            string? problem = validate(pin.Text);
            if (problem == null)
            {
                DialogResult = DialogResult.OK;
                return;
            }
            error.Text = problem;
            pin.SelectAll();
            pin.Focus();
        };
        AcceptButton = ok;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel { AutoSize = true, Controls = { ok, cancel } };
        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Controls = { new Label { Text = prompt, AutoSize = true }, pin, error, buttons },
        };
        Controls.Add(layout);
        Shown += (_, _) => WindowFinder.ForceForeground(Handle);
    }
}

/// <summary>Sets or changes the parent PIN.</summary>
internal sealed class SetPinForm : Form
{
    public SetPinForm(Settings settings)
    {
        Text = settings.HasPin ? "Change parent PIN" : "Set parent PIN";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        TextBox NewBox() => new() { UseSystemPasswordChar = true, Width = 220 };
        var current = NewBox();
        var first = NewBox();
        var second = NewBox();
        var error = new Label { AutoSize = true, ForeColor = Color.Firebrick };

        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
        if (settings.HasPin)
        {
            layout.Controls.Add(new Label { Text = "Current PIN:", AutoSize = true });
            layout.Controls.Add(current);
        }
        layout.Controls.Add(new Label { Text = "New PIN (at least 4 characters):", AutoSize = true });
        layout.Controls.Add(first);
        layout.Controls.Add(new Label { Text = "Repeat new PIN:", AutoSize = true });
        layout.Controls.Add(second);
        layout.Controls.Add(error);

        var save = new Button { Text = "Save", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) =>
        {
            if (settings.HasPin && !settings.CheckPin(current.Text))
            {
                error.Text = "The current PIN is wrong.";
            }
            else if (first.Text.Length < 4)
            {
                error.Text = "Use at least 4 characters.";
            }
            else if (first.Text != second.Text)
            {
                error.Text = "The two PINs do not match.";
            }
            else
            {
                settings.SetPin(first.Text);
                settings.Save();
                DialogResult = DialogResult.OK;
            }
        };
        layout.Controls.Add(new FlowLayoutPanel { AutoSize = true, Controls = { save, cancel } });
        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(layout);
    }
}
