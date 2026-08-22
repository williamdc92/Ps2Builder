using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PS2Builder;

/// <summary>
/// Owns the user-facing runtime session while PCSX2 is running. PCSX2 remains the
/// emulation engine, but normal users never need to interact with its desktop UI.
/// </summary>
internal sealed class PlaySessionContext : ApplicationContext
{
    const int VK_ESCAPE = 0x1B;
    const uint WM_CLOSE = 0x0010;

    readonly Process process;
    readonly string title;
    readonly System.Windows.Forms.Timer timer;
    bool escapeWasDown;
    bool exitRequested;
    IntPtr lastPcsx2Window;
    ExitOverlayForm? overlay;

    public PlaySessionContext(Process process, string title)
    {
        this.process = process;
        this.title = string.IsNullOrWhiteSpace(title) ? "PlayStation 2 Game" : title;

        timer = new System.Windows.Forms.Timer { Interval = 30 };
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    void Tick()
    {
        if (process.HasExited)
        {
            timer.Stop();
            overlay?.Close();
            ExitThread();
            return;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        if (BelongsToPcsx2(foreground))
            lastPcsx2Window = foreground;

        var escapeDown = (NativeMethods.GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0;
        if (escapeDown && !escapeWasDown && overlay is null && BelongsToPcsx2(foreground))
            ShowExitOverlay(foreground);
        escapeWasDown = escapeDown;
    }

    bool BelongsToPcsx2(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == (uint)process.Id;
    }

    void ShowExitOverlay(IntPtr gameWindow)
    {
        lastPcsx2Window = gameWindow;
        var screen = Screen.FromHandle(gameWindow);
        overlay = new ExitOverlayForm(title, screen.Bounds);
        overlay.ContinueRequested += ContinueGame;
        overlay.ExitRequested += ExitGame;
        overlay.FormClosed += (_, _) =>
        {
            overlay = null;
            if (!exitRequested && !process.HasExited)
                RestoreGameFocus();
        };
        overlay.Show();
        overlay.Activate();
    }

    void ContinueGame()
    {
        overlay?.Close();
        RestoreGameFocus();
    }

    void RestoreGameFocus()
    {
        if (lastPcsx2Window != IntPtr.Zero)
            NativeMethods.SetForegroundWindow(lastPcsx2Window);
    }

    void ExitGame()
    {
        if (exitRequested)
            return;
        exitRequested = true;
        overlay?.Close();

        try
        {
            // ConfirmShutdown is disabled in the generated PCSX2.ini, therefore the
            // normal window close path exits without surfacing a PCSX2 confirmation UI.
            // Prefer the actual game window because the Qt main window can be hidden.
            var closeRequested = lastPcsx2Window != IntPtr.Zero
                ? NativeMethods.PostMessage(lastPcsx2Window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero)
                : process.CloseMainWindow();
            if (!process.HasExited && closeRequested && process.WaitForExit(3000))
                return;
        }
        catch
        {
            // Fall through to the final process termination below.
        }

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    protected override void ExitThreadCore()
    {
        timer.Stop();
        timer.Dispose();
        if (overlay is not null && !overlay.IsDisposed)
            overlay.Close();
        base.ExitThreadCore();
    }

    static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
