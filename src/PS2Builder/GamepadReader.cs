using System.Runtime.InteropServices;

namespace PS2Builder;

internal readonly record struct GamepadSnapshot(
    bool Left,
    bool Right,
    bool Confirm,
    bool Cancel);

/// <summary>
/// Reads a standard gamepad without requiring any extra installation.
/// The preferred backend is the SDL3.dll bundled with the PCSX2 runtime, so
/// the exit overlay sees the same class of controllers that PCSX2 itself sees.
/// XInput is retained as a fallback for Xbox-compatible controllers.
/// </summary>
internal sealed class GamepadReader : IDisposable
{
    readonly SdlBackend? sdl;
    readonly XInputBackend? xinput;

    public GamepadReader(string pcsx2Directory)
    {
        try
        {
            var sdlPath = Path.Combine(pcsx2Directory, "SDL3.dll");
            if (File.Exists(sdlPath))
                sdl = new SdlBackend(sdlPath);
        }
        catch
        {
            sdl = null;
        }

        if (sdl is null)
        {
            try { xinput = new XInputBackend(); }
            catch { xinput = null; }
        }
    }

    public GamepadSnapshot Read()
    {
        if (sdl is not null && sdl.TryRead(out var state))
            return state;

        if (xinput is not null && xinput.TryRead(out state))
            return state;

        return default;
    }

    public void Dispose()
    {
        sdl?.Dispose();
        xinput?.Dispose();
    }

    sealed class SdlBackend : IDisposable
    {
        const uint SDL_INIT_GAMEPAD = 0x00002000u;
        const int SDL_GAMEPAD_AXIS_LEFTX = 0;
        const int SDL_GAMEPAD_BUTTON_SOUTH = 0;
        const int SDL_GAMEPAD_BUTTON_EAST = 1;
        const int SDL_GAMEPAD_BUTTON_DPAD_LEFT = 13;
        const int SDL_GAMEPAD_BUTTON_DPAD_RIGHT = 14;
        const short AxisThreshold = 16000;

        readonly IntPtr library;
        readonly SDL_InitDelegate init;
        readonly SDL_QuitSubSystemDelegate quitSubSystem;
        readonly SDL_UpdateGamepadsDelegate updateGamepads;
        readonly SDL_GetGamepadsDelegate getGamepads;
        readonly SDL_OpenGamepadDelegate openGamepad;
        readonly SDL_CloseGamepadDelegate closeGamepad;
        readonly SDL_GamepadConnectedDelegate gamepadConnected;
        readonly SDL_GetGamepadButtonDelegate getGamepadButton;
        readonly SDL_GetGamepadAxisDelegate getGamepadAxis;
        readonly SDL_FreeDelegate free;

        IntPtr gamepad;
        bool initialized;

        public SdlBackend(string dllPath)
        {
            library = NativeLibrary.Load(dllPath);
            try
            {
                init = Load<SDL_InitDelegate>("SDL_Init");
                quitSubSystem = Load<SDL_QuitSubSystemDelegate>("SDL_QuitSubSystem");
                updateGamepads = Load<SDL_UpdateGamepadsDelegate>("SDL_UpdateGamepads");
                getGamepads = Load<SDL_GetGamepadsDelegate>("SDL_GetGamepads");
                openGamepad = Load<SDL_OpenGamepadDelegate>("SDL_OpenGamepad");
                closeGamepad = Load<SDL_CloseGamepadDelegate>("SDL_CloseGamepad");
                gamepadConnected = Load<SDL_GamepadConnectedDelegate>("SDL_GamepadConnected");
                getGamepadButton = Load<SDL_GetGamepadButtonDelegate>("SDL_GetGamepadButton");
                getGamepadAxis = Load<SDL_GetGamepadAxisDelegate>("SDL_GetGamepadAxis");
                free = Load<SDL_FreeDelegate>("SDL_free");

                initialized = init(SDL_INIT_GAMEPAD);
                if (!initialized)
                    throw new InvalidOperationException("SDL gamepad initialization failed.");
            }
            catch
            {
                NativeLibrary.Free(library);
                throw;
            }
        }

        T Load<T>(string name) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

        public bool TryRead(out GamepadSnapshot state)
        {
            state = default;
            if (!initialized)
                return false;

            updateGamepads();

            if (gamepad == IntPtr.Zero || !gamepadConnected(gamepad))
            {
                CloseCurrentGamepad();
                gamepad = OpenFirstGamepad();
            }

            if (gamepad == IntPtr.Zero)
                return false;

            var leftX = getGamepadAxis(gamepad, SDL_GAMEPAD_AXIS_LEFTX);
            state = new GamepadSnapshot(
                Left: getGamepadButton(gamepad, SDL_GAMEPAD_BUTTON_DPAD_LEFT) || leftX <= -AxisThreshold,
                Right: getGamepadButton(gamepad, SDL_GAMEPAD_BUTTON_DPAD_RIGHT) || leftX >= AxisThreshold,
                Confirm: getGamepadButton(gamepad, SDL_GAMEPAD_BUTTON_SOUTH),
                Cancel: getGamepadButton(gamepad, SDL_GAMEPAD_BUTTON_EAST));
            return true;
        }

        IntPtr OpenFirstGamepad()
        {
            var ids = getGamepads(out var count);
            if (ids == IntPtr.Zero)
                return IntPtr.Zero;

            try
            {
                if (count <= 0)
                    return IntPtr.Zero;

                var instanceId = unchecked((uint)Marshal.ReadInt32(ids));
                return instanceId == 0 ? IntPtr.Zero : openGamepad(instanceId);
            }
            finally
            {
                free(ids);
            }
        }

        void CloseCurrentGamepad()
        {
            if (gamepad == IntPtr.Zero)
                return;
            closeGamepad(gamepad);
            gamepad = IntPtr.Zero;
        }

        public void Dispose()
        {
            if (library == IntPtr.Zero)
                return;

            CloseCurrentGamepad();
            if (initialized)
            {
                quitSubSystem(SDL_INIT_GAMEPAD);
                initialized = false;
            }
            NativeLibrary.Free(library);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        delegate bool SDL_InitDelegate(uint flags);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void SDL_QuitSubSystemDelegate(uint flags);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void SDL_UpdateGamepadsDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate IntPtr SDL_GetGamepadsDelegate(out int count);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate IntPtr SDL_OpenGamepadDelegate(uint instanceId);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void SDL_CloseGamepadDelegate(IntPtr gamepad);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        delegate bool SDL_GamepadConnectedDelegate(IntPtr gamepad);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        delegate bool SDL_GetGamepadButtonDelegate(IntPtr gamepad, int button);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate short SDL_GetGamepadAxisDelegate(IntPtr gamepad, int axis);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void SDL_FreeDelegate(IntPtr memory);
    }

    sealed class XInputBackend : IDisposable
    {
        const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
        const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
        const ushort XINPUT_GAMEPAD_A = 0x1000;
        const ushort XINPUT_GAMEPAD_B = 0x2000;
        const short AxisThreshold = 16000;

        readonly IntPtr library;
        readonly XInputGetStateDelegate getState;

        public XInputBackend()
        {
            foreach (var dll in new[] { "xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll" })
            {
                if (!NativeLibrary.TryLoad(dll, out library))
                    continue;

                if (NativeLibrary.TryGetExport(library, "XInputGetState", out var proc))
                {
                    getState = Marshal.GetDelegateForFunctionPointer<XInputGetStateDelegate>(proc);
                    return;
                }

                NativeLibrary.Free(library);
            }

            throw new DllNotFoundException("No compatible XInput runtime was found.");
        }

        public bool TryRead(out GamepadSnapshot state)
        {
            state = default;
            for (uint i = 0; i < 4; i++)
            {
                if (getState(i, out var xstate) != 0)
                    continue;

                var buttons = xstate.Gamepad.wButtons;
                state = new GamepadSnapshot(
                    Left: (buttons & XINPUT_GAMEPAD_DPAD_LEFT) != 0 || xstate.Gamepad.sThumbLX <= -AxisThreshold,
                    Right: (buttons & XINPUT_GAMEPAD_DPAD_RIGHT) != 0 || xstate.Gamepad.sThumbLX >= AxisThreshold,
                    Confirm: (buttons & XINPUT_GAMEPAD_A) != 0,
                    Cancel: (buttons & XINPUT_GAMEPAD_B) != 0);
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            if (library != IntPtr.Zero)
                NativeLibrary.Free(library);
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate uint XInputGetStateDelegate(uint userIndex, out XINPUT_STATE state);

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
    }
}
