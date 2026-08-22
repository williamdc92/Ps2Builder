using System.Runtime.InteropServices;

namespace PS2Builder;

internal sealed class ExitOverlayForm : Form
{
    const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
    const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
    const ushort XINPUT_GAMEPAD_A = 0x1000;
    const ushort XINPUT_GAMEPAD_B = 0x2000;

    readonly Button continueButton;
    readonly Button exitButton;
    readonly System.Windows.Forms.Timer controllerTimer;
    ushort previousControllerButtons;

    public event Action? ContinueRequested;
    public event Action? ExitRequested;

    public ExitOverlayForm(string gameTitle, Rectangle screenBounds)
    {
        Text = "PS2 Builder";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = screenBounds;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Opacity = 0.92;
        KeyPreview = true;

        var center = new Panel
        {
            Size = new Size(540, 230),
            BackColor = Color.FromArgb(32, 32, 32)
        };
        Controls.Add(center);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(34, 26, 34, 26),
            ColumnCount = 2,
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        center.Controls.Add(layout);

        var headingFontFamily = SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;
        var heading = new Label
        {
            Text = "Exit game?",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(headingFontFamily, 20, FontStyle.Bold),
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
            ForeColor = Color.Gainsboro
        };
        layout.Controls.Add(subtitle, 0, 1);
        layout.SetColumnSpan(subtitle, 2);

        continueButton = new Button
        {
            Text = "Continue",
            Dock = DockStyle.Fill,
            Margin = new Padding(8),
            TabIndex = 0
        };
        exitButton = new Button
        {
            Text = "Exit",
            Dock = DockStyle.Fill,
            Margin = new Padding(8),
            TabIndex = 1
        };
        continueButton.Click += (_, _) => ContinueRequested?.Invoke();
        exitButton.Click += (_, _) => ExitRequested?.Invoke();
        layout.Controls.Add(continueButton, 0, 2);
        layout.Controls.Add(exitButton, 1, 2);

        var help = new Label
        {
            Text = "Esc / B: continue    •    Enter / A: confirm",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.Silver
        };
        layout.Controls.Add(help, 0, 3);
        layout.SetColumnSpan(help, 2);

        Resize += (_, _) => CenterPanel(center);
        Shown += (_, _) =>
        {
            CenterPanel(center);
            continueButton.Select();
            Activate();
        };

        controllerTimer = new System.Windows.Forms.Timer { Interval = 80 };
        controllerTimer.Tick += (_, _) => PollXInput();
        controllerTimer.Start();
    }

    void CenterPanel(Control panel)
    {
        panel.Left = Math.Max(0, (ClientSize.Width - panel.Width) / 2);
        panel.Top = Math.Max(0, (ClientSize.Height - panel.Height) / 2);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Escape:
                ContinueRequested?.Invoke();
                return true;
            case Keys.Left:
            case Keys.Right:
                ToggleSelection();
                return true;
            case Keys.Enter:
                if (exitButton.Focused)
                    ExitRequested?.Invoke();
                else
                    ContinueRequested?.Invoke();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void PollXInput()
    {
        if (!TryGetFirstControllerButtons(out var buttons))
        {
            previousControllerButtons = 0;
            return;
        }

        var pressed = (ushort)(buttons & ~previousControllerButtons);
        previousControllerButtons = buttons;

        if ((pressed & (XINPUT_GAMEPAD_DPAD_LEFT | XINPUT_GAMEPAD_DPAD_RIGHT)) != 0)
            ToggleSelection();
        if ((pressed & XINPUT_GAMEPAD_B) != 0)
            ContinueRequested?.Invoke();
        if ((pressed & XINPUT_GAMEPAD_A) != 0)
        {
            if (exitButton.Focused)
                ExitRequested?.Invoke();
            else
                ContinueRequested?.Invoke();
        }
    }

    void ToggleSelection()
    {
        if (exitButton.Focused)
            continueButton.Select();
        else
            exitButton.Select();
    }

    static bool TryGetFirstControllerButtons(out ushort buttons)
    {
        buttons = 0;
        try
        {
            for (uint i = 0; i < 4; i++)
            {
                if (XInputGetState(i, out var state) == 0)
                {
                    buttons = state.Gamepad.wButtons;
                    return true;
                }
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            controllerTimer.Stop();
            controllerTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [DllImport("xinput1_4.dll")]
    static extern uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);
}
