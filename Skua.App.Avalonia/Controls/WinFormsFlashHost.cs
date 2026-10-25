using Avalonia.Controls;
using Avalonia.Platform;
using System;

#if IS_WINDOWS
namespace Skua.App.Avalonia.Controls
{
using WinForms = System.Windows.Forms;
public class WinFormsFlashHost : NativeControlHost
{
    private WinForms.Panel? _panel;
    private WinForms.Control? _child;

    public void SetChild(WinForms.Control child)
    {
        _child = child;
        if (_panel is null)
            return;

        AttachChildToPanel();
    }

    public void SetScaleMode(bool isExactFit = false)
    {
        ApplyScaleMode();
    }

    private void ApplyScaleMode()
    {
        if (_child is AxShockwaveFlashObjects.AxShockwaveFlash flash)
        {
            try
            {
                flash.ScaleMode = 0;
                flash.CtlScale = "ShowAll";
            }
            catch { }
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _panel = new WinForms.Panel
        {
            Dock = WinForms.DockStyle.Fill,
            BackColor = System.Drawing.Color.FromArgb(20, 20, 20)
        };

        _panel.Resize += (s, e) => ApplyScaleMode();

        if (_child is not null)
            AttachChildToPanel();

        return new PlatformHandle(_panel.Handle, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (_panel is not null)
        {
            _panel.Controls.Clear();
            _panel.Dispose();
            _panel = null;
        }
    }

    private void AttachChildToPanel()
    {
        if (_panel is null || _child is null)
            return;

        if (_child.Parent is not null && _child.Parent != _panel)
            _child.Parent.Controls.Remove(_child);

        _child.Dock = WinForms.DockStyle.Fill;
        _panel.Controls.Clear();
        _panel.Controls.Add(_child);
        ApplyScaleMode();
    }
}
}
#else
using System.Runtime.InteropServices;

namespace Skua.App.Avalonia.Controls
{
public class WinFormsFlashHost : NativeControlHost
{
    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XReparentWindow(IntPtr display, IntPtr w, IntPtr parent, int x, int y);

    [DllImport("libX11.so.6")]
    private static extern int XMapWindow(IntPtr display, IntPtr w);

    [DllImport("libX11.so.6")]
    private static extern int XMoveResizeWindow(IntPtr display, IntPtr w, int x, int y, uint width, uint height);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);

    private static IntPtr _xDisplay;
    private static readonly object _xLock = new();

    public static ulong HostXid { get; private set; }
    public static event Action<ulong>? HostXidReady;
    public static event Action<int, int>? HostResized;
    public static event Action<bool, string, string, bool, bool, bool>? KeyForwarded;
    public static event Action<string>? TextForwarded;
    public static event Action? GameClicked;
    private static WinFormsFlashHost? _activeInstance;

    public static void ForwardKey(bool down, string physical, string logical, bool ctrl, bool shift, bool alt)
    {
        KeyForwarded?.Invoke(down, physical, logical, ctrl, shift, alt);
    }

    public static void ForwardText(string text)
    {
        TextForwarded?.Invoke(text);
    }

    public static void NotifyGameClicked()
    {
        GameClicked?.Invoke();
    }

    private ulong _hostXid;
    private ulong _childXid;

    public WinFormsFlashHost()
    {
        Focusable = true;
    }

    private static IntPtr GetDisplay()
    {
        lock (_xLock)
        {
            if (_xDisplay == IntPtr.Zero)
            {
                try
                {
                    _xDisplay = XOpenDisplay(IntPtr.Zero);
                }
                catch { }
            }
            return _xDisplay;
        }
    }

    public void SetScaleMode(bool isExactFit = false)
    {
        ResizeChild();
    }

    public static void NotifyChildXidReady(ulong childXid)
    {
        _activeInstance?.AttachChildWindow(childXid);
    }

    public void AttachChildWindow(ulong childXid)
    {
        _childXid = childXid;
        EmbedAndResizeChild();
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _activeInstance = this;
        IPlatformHandle control = base.CreateNativeControlCore(parent);
        _hostXid = (ulong)control.Handle;
        HostXid = _hostXid;
        HostXidReady?.Invoke(_hostXid);

        if (_childXid != 0)
            EmbedAndResizeChild();

        return control;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (HostXid == (ulong)control.Handle)
            HostXid = 0;
        _hostXid = 0;
        base.DestroyNativeControlCore(control);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ResizeChild();
    }

    private void EmbedAndResizeChild()
    {
        if (_hostXid == 0 || _childXid == 0)
            return;

        try
        {
            IntPtr display = GetDisplay();
            if (display != IntPtr.Zero)
            {
                lock (_xLock)
                {
                    XReparentWindow(display, (IntPtr)_childXid, (IntPtr)_hostXid, 0, 0);
                    XMapWindow(display, (IntPtr)_childXid);
                    XFlush(display);
                }
            }
        }
        catch { }

        ResizeChild();
    }

    private void ResizeChild()
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int width = Math.Max(1, (int)Math.Round(Bounds.Width * scaling));
        int height = Math.Max(1, (int)Math.Round(Bounds.Height * scaling));

        if (_hostXid != 0 && _childXid != 0)
        {
            try
            {
                IntPtr display = GetDisplay();
                if (display != IntPtr.Zero)
                {
                    lock (_xLock)
                    {
                        XMoveResizeWindow(display, (IntPtr)_childXid, 0, 0, (uint)width, (uint)height);
                        XFlush(display);
                    }
                }
            }
            catch { }
        }

        HostResized?.Invoke(width, height);
    }
}
}

namespace System.Windows.Forms
{
    public delegate void MethodInvoker();

    public enum FormStartPosition
    {
        Manual,
        CenterScreen,
        WindowsDefaultLocation,
        WindowsDefaultBounds,
        CenterParent
    }

    public enum FormBorderStyle
    {
        None,
        FixedSingle,
        Fixed3D,
        FixedDialog,
        Sizable,
        FixedToolWindow,
        SizableToolWindow
    }

    public enum DockStyle
    {
        None,
        Top,
        Bottom,
        Left,
        Right,
        Fill
    }

    public enum ProgressBarStyle
    {
        Blocks,
        Continuous,
        Marquee
    }

    public enum DialogResult
    {
        None,
        OK,
        Cancel,
        Abort,
        Retry,
        Ignore,
        Yes,
        No
    }

    public class PaintEventArgs : System.EventArgs
    {
        public object? Graphics { get; }
    }

    public delegate void PaintEventHandler(object? sender, PaintEventArgs e);

    public class Control : System.IDisposable
    {
        public string Text { get; set; } = string.Empty;
        public System.Drawing.Size Size { get; set; }
        public int Width => Size.Width;
        public int Height => Size.Height;
        public DockStyle Dock { get; set; }
        public System.Drawing.Color ForeColor { get; set; }
        public System.Drawing.Color BackColor { get; set; }
        public event PaintEventHandler? Paint;
        public virtual void Dispose() { }
    }

    public class ProgressBar : Control
    {
        public int Minimum { get; set; }
        public int Maximum { get; set; } = 100;
        public int Value { get; set; }
        public ProgressBarStyle Style { get; set; }
    }

    public class Form : Control
    {
        public FormStartPosition StartPosition { get; set; }
        public FormBorderStyle FormBorderStyle { get; set; }
        public bool MaximizeBox { get; set; }
        public bool MinimizeBox { get; set; }
        public System.Collections.Generic.List<Control> Controls { get; } = new();
        public event System.EventHandler? Shown;
        public object? Invoke(System.Delegate method, params object?[]? args) => method.DynamicInvoke(args);
        public void Show() => Shown?.Invoke(this, System.EventArgs.Empty);
        public DialogResult ShowDialog()
        {
            Shown?.Invoke(this, System.EventArgs.Empty);
            return DialogResult.OK;
        }
        public void Close() { }
    }

    public static class Application
    {
        public static void Run(Form? mainForm = null) => mainForm?.Show();
        public static void DoEvents() { }
    }

    public static class MessageBox
    {
        public static DialogResult Show(string? text, string? caption = null) => DialogResult.OK;
    }
}
#endif
