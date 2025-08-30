using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ConsoleGame.Renderer
{
    public class TerminalInput : IDisposable
    {
        private Queue<ConsoleKeyInfo> inputQueue;
        private Queue<MouseEvent> mouseQueue;
        private int[] monitoredVKeys;

        private IntPtr targetWindow;
        private IntPtr hookHandle;
        private LowLevelMouseProc hookProc;
        private Thread hookThread;
        private uint hookThreadId;
        private AutoResetEvent hookReady = new AutoResetEvent(false);

        private volatile bool capturing = false;
        private volatile MouseButtons heldButtons = MouseButtons.None;

        private readonly object inputLock = new object();
        private readonly object mouseLock = new object();

        public bool RequireFocus { get; set; } = true;
        public bool MouseEnabled { get; private set; }

        public TerminalInput()
        {
            inputQueue = new Queue<ConsoleKeyInfo>();
            mouseQueue = new Queue<MouseEvent>();
            monitoredVKeys = BuildDefaultMonitoredKeys();
            targetWindow = GetConsoleWindow();
            if (targetWindow == IntPtr.Zero)
            {
                targetWindow = GetForegroundWindow();
            }
            //StartHookThread();
        }

        public void Dispose()
        {
            StopHookThread();
            hookReady.Dispose();
        }

        public void SetTargetWindow(IntPtr hwnd)
        {
            targetWindow = hwnd;
        }

        public void Update()
        {
            if (RequireFocus && !HasConsoleFocus())
            {
                lock (inputLock) { inputQueue.Clear(); }
                lock (mouseLock) { mouseQueue.Clear(); }
                return;
            }

            bool shift = IsDown(0x10);
            bool alt = IsDown(0x12);
            bool ctrl = IsDown(0x11);

            for (int i = 0; i < monitoredVKeys.Length; i++)
            {
                int vk = monitoredVKeys[i];
                if (IsDown(vk))
                {
                    ConsoleKey key = (ConsoleKey)vk;
                    ConsoleKeyInfo keyInfo = new ConsoleKeyInfo('\0', key, shift, alt, ctrl);
                    lock (inputLock) { inputQueue.Enqueue(keyInfo); }
                }
            }
        }

        public bool TryGetKey(out ConsoleKeyInfo keyInfo)
        {
            lock (inputLock)
            {
                if (inputQueue.Count > 0)
                {
                    keyInfo = inputQueue.Dequeue();
                    return true;
                }
            }

            keyInfo = default;
            return false;
        }

        public bool TryGetMouseEvent(out MouseEvent mouseEvent)
        {
            lock (mouseLock)
            {
                if (mouseQueue.Count > 0)
                {
                    mouseEvent = mouseQueue.Dequeue();
                    return true;
                }
            }

            mouseEvent = default;
            return false;
        }

        public void Clear()
        {
            lock (inputLock) { inputQueue.Clear(); }
            lock (mouseLock) { mouseQueue.Clear(); }
        }

        public void SetMonitoredKeys(IEnumerable<ConsoleKey> keys)
        {
            if (keys == null)
            {
                monitoredVKeys = BuildDefaultMonitoredKeys();
                return;
            }

            List<int> list = new List<int>();
            foreach (var k in keys)
            {
                list.Add((int)k);
            }
            monitoredVKeys = list.ToArray();
        }

        public bool IsKeyDown(ConsoleKey key)
        {
            return IsDown((int)key);
        }

        public bool HasConsoleFocus()
        {
            IntPtr fg = GetForegroundWindow();
            if (targetWindow == IntPtr.Zero || fg == IntPtr.Zero)
            {
                return true;
            }
            if (fg == targetWindow)
            {
                return true;
            }
            if (IsWindowRelated(fg, targetWindow))
            {
                return true;
            }
            return false;
        }

        private void StartHookThread()
        {
            if (hookThread != null)
            {
                return;
            }

            hookThread = new Thread(HookThreadProc);
            hookThread.IsBackground = true;
            hookThread.Name = "TerminalInput.MouseHook";
            hookThread.Start();
            hookReady.WaitOne();
            MouseEnabled = hookHandle != IntPtr.Zero;
        }

        private void StopHookThread()
        {
            try
            {
                if (hookThreadId != 0)
                {
                    PostThreadMessage(hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch { }
            try
            {
                if (hookHandle != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(hookHandle);
                    hookHandle = IntPtr.Zero;
                }
            }
            catch { }
            try
            {
                if (hookThread != null && hookThread.IsAlive)
                {
                    hookThread.Join(250);
                }
            }
            catch { }
            hookThread = null;
            hookThreadId = 0;
            MouseEnabled = false;
        }

        private void HookThreadProc()
        {
            hookThreadId = GetCurrentThreadId();
            hookProc = new LowLevelMouseProc(MouseHookCallback);
            IntPtr hModule = GetModuleHandle(null);
            hookHandle = SetWindowsHookEx(WH_MOUSE_LL, hookProc, hModule, 0);
            hookReady.Set();

            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                MSLLHOOKSTRUCT data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                POINT screen = data.pt;

                IntPtr hover = WindowFromPoint(screen);
                bool overTarget = IsWindowRelated(hover, targetWindow);

                if (RequireFocus)
                {
                    IntPtr fg = GetForegroundWindow();
                    if (!IsWindowRelated(fg, targetWindow))
                    {
                        overTarget = false;
                    }
                }

                bool handled = false;

                if (overTarget || capturing)
                {
                    POINT client = screen;
                    if (targetWindow != IntPtr.Zero)
                    {
                        ScreenToClient(targetWindow, ref client);
                    }

                    RECT rc;
                    bool inClient = targetWindow == IntPtr.Zero ? true : (GetClientRect(targetWindow, out rc) && client.X >= 0 && client.Y >= 0 && client.X < (rc.Right - rc.Left) && client.Y < (rc.Bottom - rc.Top));

                    bool shift = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
                    bool alt = (GetKeyState(VK_MENU) & 0x8000) != 0;
                    bool ctrl = (GetKeyState(VK_CONTROL) & 0x8000) != 0;

                    if (msg == WM_MOUSEMOVE)
                    {
                        if (inClient || capturing)
                        {
                            EnqueueMouseEvent(client.X, client.Y, heldButtons, 0, true, false, false, false, shift, alt, ctrl);
                            handled = true;
                        }
                    }
                    else if (msg == WM_LBUTTONDOWN || msg == WM_MBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_XBUTTONDOWN)
                    {
                        MouseButtons b = MapButtonFromMessage(msg, data.mouseData, true);
                        heldButtons |= b;
                        if (!capturing && targetWindow != IntPtr.Zero)
                        {
                            SetCapture(targetWindow);
                            capturing = true;
                        }
                        if (inClient || capturing)
                        {
                            EnqueueMouseEvent(client.X, client.Y, b, 0, false, false, true, false, shift, alt, ctrl);
                            handled = true;
                        }
                    }
                    else if (msg == WM_LBUTTONUP || msg == WM_MBUTTONUP || msg == WM_RBUTTONUP || msg == WM_XBUTTONUP)
                    {
                        MouseButtons b = MapButtonFromMessage(msg, data.mouseData, false);
                        heldButtons &= ~b;
                        if (capturing && heldButtons == MouseButtons.None)
                        {
                            ReleaseCapture();
                            capturing = false;
                        }
                        if (inClient || capturing)
                        {
                            EnqueueMouseEvent(client.X, client.Y, b, 0, false, false, false, true, shift, alt, ctrl);
                            handled = true;
                        }
                    }
                    else if (msg == WM_MOUSEWHEEL || msg == WM_MOUSEHWHEEL)
                    {
                        int delta = (short)((data.mouseData >> 16) & 0xFFFF);
                        if (inClient || capturing)
                        {
                            EnqueueMouseEvent(client.X, client.Y, MouseButtons.None, delta, false, false, false, false, shift, alt, ctrl);
                            handled = true;
                        }
                    }
                }

                if (handled)
                {
                    return new IntPtr(1);
                }
            }

            return CallNextHookEx(hookHandle, nCode, wParam, lParam);
        }

        private static MouseButtons MapButtonFromMessage(int msg, uint mouseData, bool down)
        {
            if (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP) return MouseButtons.Left;
            if (msg == WM_RBUTTONDOWN || msg == WM_RBUTTONUP) return MouseButtons.Right;
            if (msg == WM_MBUTTONDOWN || msg == WM_MBUTTONUP) return MouseButtons.Middle;
            if (msg == WM_XBUTTONDOWN || msg == WM_XBUTTONUP)
            {
                int xbtn = (int)((mouseData >> 16) & 0xFFFF);
                if (xbtn == XBUTTON1) return MouseButtons.X1;
                if (xbtn == XBUTTON2) return MouseButtons.X2;
            }
            return MouseButtons.None;
        }

        private void EnqueueMouseEvent(int x, int y, MouseButtons buttons, int wheelDelta, bool moved, bool doubleClick, bool down, bool up, bool shift, bool alt, bool ctrl)
        {
            MouseEvent e = new MouseEvent
            {
                X = x,
                Y = y,
                Buttons = buttons,
                WheelDelta = wheelDelta,
                Moved = moved,
                DoubleClick = doubleClick,
                Down = down,
                Up = up,
                Shift = shift,
                Alt = alt,
                Ctrl = ctrl
            };
            lock (mouseLock) { mouseQueue.Enqueue(e); }
        }

        private static bool IsDown(int vKey)
        {
            return (GetAsyncKeyState(vKey) & 0x8000) != 0;
        }

        private static bool IsWindowRelated(IntPtr a, IntPtr b)
        {
            if (a == IntPtr.Zero || b == IntPtr.Zero) return false;
            IntPtr aRoot = GetAncestor(a, GA_ROOTOWNER);
            IntPtr bRoot = GetAncestor(b, GA_ROOTOWNER);
            if (a == b) return true;
            if (aRoot == b || bRoot == a) return true;
            if (aRoot == bRoot && aRoot != IntPtr.Zero) return true;
            return false;
        }

        private static int[] BuildDefaultMonitoredKeys()
        {
            List<int> keys = new List<int>();
            for (int vk = 0x41; vk <= 0x5A; vk++) keys.Add(vk);
            for (int vk = 0x30; vk <= 0x39; vk++) keys.Add(vk);
            keys.Add(0x25);
            keys.Add(0x26);
            keys.Add(0x27);
            keys.Add(0x28);
            keys.Add(0x20);
            keys.Add(0x1B);
            keys.Add(0x0D);
            keys.Add(0x10);
            keys.Add(0x11);
            keys.Add(0x12);
            for (int vk = 0xBA; vk <= 0xC0; vk++) keys.Add(vk);
            for (int vk = 0xDB; vk <= 0xDF; vk++) keys.Add(vk);
            keys.Add(0xE2);
            return keys.ToArray();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
            public uint lPrivate;
        }

        [Flags]
        public enum MouseButtons
        {
            None = 0,
            Left = 1,
            Right = 2,
            Middle = 4,
            X1 = 8,
            X2 = 16
        }

        public struct MouseEvent
        {
            public int X;
            public int Y;
            public MouseButtons Buttons;
            public int WheelDelta;
            public bool Moved;
            public bool DoubleClick;
            public bool Down;
            public bool Up;
            public bool Shift;
            public bool Alt;
            public bool Ctrl;
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr SetCapture(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private const int WH_MOUSE_LL = 14;

        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_XBUTTONUP = 0x020C;
        private const int WM_MOUSEHWHEEL = 0x020E;
        private const uint GA_ROOTOWNER = 3;
        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12;
        private const int XBUTTON1 = 1;
        private const int XBUTTON2 = 2;
        private const uint WM_QUIT = 0x0012;
    }
}
