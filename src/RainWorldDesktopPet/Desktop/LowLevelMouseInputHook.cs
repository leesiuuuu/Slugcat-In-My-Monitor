using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace RainWorldDesktopPet.Desktop
{
    // WH_MOUSE_LL callbacks are dispatched through the message queue of the
    // installing thread. Keeping that hook on the render/UI thread makes the
    // physical cursor wait whenever a complex multi-pet frame takes too long.
    // This owner gives the hook a small, dedicated message pump instead.
    internal sealed class LowLevelMouseInputHook : IDisposable
    {
        internal delegate bool ButtonHandler(int message, NativeMethods.Point point);

        private readonly ButtonHandler buttonHandler;
        private readonly NativeMethods.LowLevelMouseProc callback;
        private readonly ManualResetEvent started = new ManualResetEvent(false);
        private readonly object stateLock = new object();
        private Thread thread;
        private IntPtr hook;
        private uint threadId;
        private Exception startupError;
        private bool disposed;

        internal LowLevelMouseInputHook(ButtonHandler buttonHandler)
        {
            if (buttonHandler == null) throw new ArgumentNullException("buttonHandler");
            this.buttonHandler = buttonHandler;
            callback = HookCallback;
        }

        internal void Start()
        {
            lock (stateLock)
            {
                if (disposed) throw new ObjectDisposedException(GetType().Name);
                if (thread != null) return;
                thread = new Thread(ThreadMain);
                thread.IsBackground = true;
                thread.Name = "Slugcat mouse input hook";
                thread.Start();
            }

            if (!started.WaitOne(5000))
                throw new TimeoutException("Timed out while starting the desktop pet mouse input hook.");
            if (startupError != null)
                throw new InvalidOperationException(
                    "Unable to start the desktop pet mouse input hook.", startupError);
        }

        private void ThreadMain()
        {
            try
            {
                threadId = NativeMethods.GetCurrentThreadId();
                NativeMethods.Message unused;
                // Force creation of this thread's message queue before Start
                // returns, so shutdown can always post WM_QUIT safely.
                NativeMethods.PeekMessage(out unused, IntPtr.Zero, 0, 0,
                    NativeMethods.PM_NOREMOVE);
                hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL,
                    callback, NativeMethods.GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "Unable to install the desktop pet mouse input hook.");
                started.Set();

                NativeMethods.Message message;
                int result;
                while ((result = NativeMethods.GetMessage(out message, IntPtr.Zero, 0, 0)) > 0)
                {
                    NativeMethods.TranslateMessage(ref message);
                    NativeMethods.DispatchMessage(ref message);
                }
                if (result < 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "The desktop pet mouse input pump failed.");
            }
            catch (Exception exception)
            {
                startupError = exception;
                started.Set();
                Program.LogException(exception);
            }
            finally
            {
                IntPtr installed = hook;
                hook = IntPtr.Zero;
                if (installed != IntPtr.Zero)
                    NativeMethods.UnhookWindowsHookEx(installed);
                threadId = 0;
            }
        }

        private IntPtr HookCallback(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                int mouseMessage = unchecked((int)message.ToInt64());
                if (mouseMessage == NativeMethods.WM_LBUTTONDOWN ||
                    mouseMessage == NativeMethods.WM_LBUTTONDBLCLK ||
                    mouseMessage == NativeMethods.WM_LBUTTONUP)
                {
                    try
                    {
                        NativeMethods.LowLevelMouseHookData hookData =
                            (NativeMethods.LowLevelMouseHookData)Marshal.PtrToStructure(data,
                                typeof(NativeMethods.LowLevelMouseHookData));
                        if (buttonHandler(mouseMessage, hookData.Point))
                            return new IntPtr(1);
                    }
                    catch (Exception exception)
                    {
                        Program.LogException(exception);
                    }
                }
            }
            return NativeMethods.CallNextHookEx(hook, code, message, data);
        }

        public void Dispose()
        {
            Thread ownedThread;
            uint ownedThreadId;
            lock (stateLock)
            {
                if (disposed) return;
                disposed = true;
                ownedThread = thread;
                ownedThreadId = threadId;
                thread = null;
            }

            if (ownedThreadId != 0)
                NativeMethods.PostThreadMessage(ownedThreadId,
                    NativeMethods.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            if (ownedThread != null && ownedThread.IsAlive &&
                !ownedThread.Join(2000))
            {
                Program.LogException(new TimeoutException(
                    "Timed out while stopping the desktop pet mouse input hook."));
            }
            started.Dispose();
        }
    }
}
