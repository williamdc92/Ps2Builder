using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace PS2Builder;

/// <summary>
/// Owns the user-facing runtime session while PCSX2 is running. PCSX2 remains the
/// emulation engine, but normal users never need to interact with its desktop UI.
/// </summary>
internal sealed class PlaySessionContext : ApplicationContext
{
    const int WH_KEYBOARD_LL = 13;
    const int VK_ESCAPE = 0x1B;
    const uint WM_KEYDOWN = 0x0100;
    const uint WM_KEYUP = 0x0101;
    const uint WM_SYSKEYDOWN = 0x0104;
    const uint WM_SYSKEYUP = 0x0105;
    const uint WM_CLOSE = 0x0010;

    readonly Process process;
    readonly string title;
    readonly string expectedExecutable;
    readonly System.Windows.Forms.Timer timer;
    readonly NativeMethods.LowLevelKeyboardProc keyboardProc;
    IntPtr keyboardHook;
    int escapeRequested;
    int overlayContinueRequested;
    bool escapeKeyDown;
    bool fallbackEscapeWasDown;
    bool exitRequested;
    IntPtr lastPcsx2Window;
    ExitOverlayForm? overlay;

    public PlaySessionContext(Process process, string title, string expectedExecutable)
    {
        this.process = process;
        this.title = string.IsNullOrWhiteSpace(title) ? "PlayStation 2 Game" : title;
        this.expectedExecutable = Path.GetFullPath(expectedExecutable);

        keyboardProc = KeyboardHookCallback;
        keyboardHook = NativeMethods.SetWindowsHookEx(
            WH_KEYBOARD_LL,
            keyboardProc,
            NativeMethods.GetModuleHandle(null),
            0);

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
        if (BelongsToThisPcsx2(foreground))
            lastPcsx2Window = foreground;

        // The low-level hook is the primary path. Polling is only a fallback for
        // systems where the hook could not be installed.
        if (keyboardHook == IntPtr.Zero)
        {
            var escapeIsDown = (NativeMethods.GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0;
            if (escapeIsDown && !fallbackEscapeWasDown && BelongsToThisPcsx2(foreground))
                Interlocked.Exchange(ref escapeRequested, 1);
            fallbackEscapeWasDown = escapeIsDown;
        }

        if (Interlocked.Exchange(ref overlayContinueRequested, 0) != 0 && overlay is not null)
        {
            ContinueGame();
            return;
        }

        if (Interlocked.Exchange(ref escapeRequested, 0) != 0 && overlay is null)
        {
            var gameWindow = BelongsToThisPcsx2(foreground)
                ? foreground
                : lastPcsx2Window;

            if (gameWindow == IntPtr.Zero)
            {
                try { gameWindow = process.MainWindowHandle; } catch { }
            }

            if (gameWindow != IntPtr.Zero)
                ShowExitOverlay(gameWindow);
        }
    }

    IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = unchecked((uint)wParam.ToInt64());
            if (message is WM_KEYDOWN or WM_KEYUP or WM_SYSKEYDOWN or WM_SYSKEYUP)
            {
                var vkCode = Marshal.ReadInt32(lParam);
                if (vkCode == VK_ESCAPE)
                {
                    var isDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
                    var isUp = message is WM_KEYUP or WM_SYSKEYUP;
                    var foreground = NativeMethods.GetForegroundWindow();
                    var overlayForm = overlay;
                    var overlayOwnsInput = overlayForm is not null &&
                        !overlayForm.IsDisposed &&
                        overlayForm.IsHandleCreated &&
                        foreground == overlayForm.Handle;
                    var gameOwnsInput = BelongsToThisPcsx2(foreground);

                    if (overlayOwnsInput || gameOwnsInput)
                    {
                        if (isDown && !escapeKeyDown)
                        {
                            escapeKeyDown = true;
                            if (overlayOwnsInput)
                                Interlocked.Exchange(ref overlayContinueRequested, 1);
                            else
                                Interlocked.Exchange(ref escapeRequested, 1);
                        }
                        else if (isUp)
                        {
                            escapeKeyDown = false;
                        }

                        // PLAY.exe owns Escape for the entire game session. Swallow both
                        // key-down and key-up so the PCSX2 UI never receives the key.
                        return (IntPtr)1;
                    }

                    if (isUp)
                        escapeKeyDown = false;
                }
            }
        }

        return NativeMethods.CallNextHookEx(keyboardHook, nCode, wParam, lParam);
    }

    bool BelongsToThisPcsx2(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)process.Id)
            return true;

        // PCSX2 normally remains in the process we launched. This path also covers a
        // future runtime that hands its render window to another process from the same
        // executable without accidentally matching an unrelated PCSX2 installation.
        try
        {
            using var candidate = Process.GetProcessById((int)pid);
            var executable = candidate.MainModule?.FileName;
            return executable is not null &&
                string.Equals(Path.GetFullPath(executable), expectedExecutable, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    void ShowExitOverlay(IntPtr gameWindow)
    {
        lastPcsx2Window = gameWindow;
        var screen = Screen.FromHandle(gameWindow);
        var form = new ExitOverlayForm(title, screen.Bounds, Path.GetDirectoryName(expectedExecutable) ?? AppContext.BaseDirectory);
        overlay = form;

        form.ContinueRequested += ContinueGame;
        form.ExitRequested += ExitGame;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(overlay, form))
                overlay = null;
            if (!exitRequested && !process.HasExited)
                RestoreGameFocus();
        };

        form.Show();
        form.Activate();
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
            var closeRequested = lastPcsx2Window != IntPtr.Zero
                ? NativeMethods.PostMessage(lastPcsx2Window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero)
                : process.CloseMainWindow();
            if (!process.HasExited && closeRequested && process.WaitForExit(3000))
                return;
        }
        catch
        {
            // Fall through to final process termination below.
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

        if (keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(keyboardHook);
            keyboardHook = IntPtr.Zero;
        }

        if (overlay is not null && !overlay.IsDisposed)
            overlay.Close();

        base.ExitThreadCore();
    }

    static class NativeMethods
    {
        internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(
            int idHook,
            LowLevelKeyboardProc lpfn,
            IntPtr hMod,
            uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr GetModuleHandle(string? lpModuleName);

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
