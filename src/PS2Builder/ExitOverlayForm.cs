namespace PS2Builder;

internal sealed class ExitOverlayForm : Form
{
    static readonly Color OverlayBackground = Color.FromArgb(10, 10, 13);
    static readonly Color CardBackground = Color.FromArgb(27, 29, 36);
    static readonly Color ButtonBackground = Color.FromArgb(42, 45, 55);
    static readonly Color Accent = Color.FromArgb(72, 112, 255);
    static readonly Color MutedText = Color.FromArgb(180, 184, 196);

    readonly Button continueButton;
    readonly Button exitButton;
    readonly System.Windows.Forms.Timer controllerTimer;
    readonly GamepadReader gamepadReader;
    readonly Panel card;
    GamepadSnapshot previousGamepadState;
    int selectedIndex;

    public event Action? ContinueRequested;
    public event Action? ExitRequested;

    public ExitOverlayForm(string gameTitle, Rectangle screenBounds, string pcsx2Directory)
    {
        Text = "PS2 Builder";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = screenBounds;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = OverlayBackground;
        Opacity = 0.96;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;

        card = new Panel
        {
            Size = new Size(560, 250),
            BackColor = CardBackground,
            Padding = new Padding(38, 28, 38, 24)
        };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(62, 66, 78));
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        Controls.Add(card);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        card.Controls.Add(layout);

        var fontFamily = SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;
        var heading = new Label
        {
            Text = "Exit game?",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(fontFamily, 21, FontStyle.Bold),
            ForeColor = Color.White
        };
        layout.Controls.Add(heading, 0, 0);
        layout.SetColumnSpan(heading, 2);

        var subtitle = new Label
        {
            Text = gameTitle,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopCenter,
            AutoEllipsis = true,
            Font = new Font(fontFamily, 10, FontStyle.Regular),
            ForeColor = MutedText
        };
        layout.Controls.Add(subtitle, 0, 1);
        layout.SetColumnSpan(subtitle, 2);

        continueButton = CreateChoiceButton("Continue", 0);
        exitButton = CreateChoiceButton("Exit game", 1);
        layout.Controls.Add(continueButton, 0, 2);
        layout.Controls.Add(exitButton, 1, 2);

        var help = new Label
        {
            Text = "D-pad / ← →   •   A / Enter: select   •   B / Esc: back",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(fontFamily, 9, FontStyle.Regular),
            ForeColor = MutedText
        };
        layout.Controls.Add(help, 0, 3);
        layout.SetColumnSpan(help, 2);

        Resize += (_, _) => CenterCard();

        gamepadReader = new GamepadReader(pcsx2Directory);
        controllerTimer = new System.Windows.Forms.Timer { Interval = 50 };
        controllerTimer.Tick += (_, _) => PollGamepad();

        PrepareForShow(screenBounds);
    }

    public void PrepareForShow(Rectangle screenBounds)
    {
        Bounds = screenBounds;
        CenterCard();
        SetSelection(0);

        // Baseline the current pad state before polling starts. A button still held from
        // dismissing the previous overlay must not immediately dismiss/reconfirm the next one.
        previousGamepadState = gamepadReader.Read();
        controllerTimer.Start();
    }

    public void HideOverlay()
    {
        controllerTimer.Stop();
        Hide();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        PrepareForShow(Bounds);
        Activate();
        BringToFront();
    }

    Button CreateChoiceButton(string text, int index)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = index == 0
                ? new Padding(0, 16, 8, 16)
                : new Padding(8, 16, 0, 16),
            TabStop = false,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            BackColor = ButtonBackground,
            ForeColor = Color.White,
            Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 11, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 56, 68);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(62, 66, 80);
        button.MouseEnter += (_, _) => SetSelection(index);
        button.Click += (_, _) => ActivateSelection(index);
        return button;
    }

    void CenterCard()
    {
        card.Left = Math.Max(0, (ClientSize.Width - card.Width) / 2);
        card.Top = Math.Max(0, (ClientSize.Height - card.Height) / 2);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Escape:
                ContinueRequested?.Invoke();
                return true;
            case Keys.Left:
                SetSelection(0);
                return true;
            case Keys.Right:
                SetSelection(1);
                return true;
            case Keys.Enter:
            case Keys.Space:
                ActivateSelection(selectedIndex);
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void PollGamepad()
    {
        if (!Visible)
            return;

        var state = gamepadReader.Read();

        if (state.Left && !previousGamepadState.Left)
            SetSelection(0);
        if (state.Right && !previousGamepadState.Right)
            SetSelection(1);
        if (state.Cancel && !previousGamepadState.Cancel)
            ContinueRequested?.Invoke();
        if (state.Confirm && !previousGamepadState.Confirm)
            ActivateSelection(selectedIndex);

        previousGamepadState = state;
    }

    void SetSelection(int index)
    {
        selectedIndex = index <= 0 ? 0 : 1;
        ApplyButtonStyle(continueButton, selectedIndex == 0);
        ApplyButtonStyle(exitButton, selectedIndex == 1);
    }

    static void ApplyButtonStyle(Button button, bool selected)
    {
        button.BackColor = selected ? Accent : ButtonBackground;
        button.FlatAppearance.BorderColor = selected
            ? Color.FromArgb(150, 177, 255)
            : Color.FromArgb(74, 78, 92);
    }

    void ActivateSelection(int index)
    {
        if (index == 1)
            ExitRequested?.Invoke();
        else
            ContinueRequested?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            controllerTimer.Stop();
            controllerTimer.Dispose();
            gamepadReader.Dispose();
        }
        base.Dispose(disposing);
    }
}
