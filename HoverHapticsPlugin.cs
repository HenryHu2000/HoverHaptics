using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using Loupedeck;

namespace HoverHaptics;

public class HoverHapticsPlugin : Plugin
{
    private const string EdgeEvent = "edge";
    private const string ButtonEvent = "button";
    private const string MainTray = "Shell_TrayWnd";
    private const string SideTray = "Shell_SecondaryTrayWnd";
    private const int DwellMs = 50;
    private const int StillMs = 16;
    private const int Slack = 4;
    private const int We = 1, Ns = 2, Nwse = 3, Nesw = 4, Button = 5;
    public override bool UsesApplicationApiOnly => true;
    public override bool HasNoApplication => true;
    private CancellationTokenSource stop;
    private static volatile Rect[] icons = Array.Empty<Rect>();
    private static bool busy;
    private static IntPtr sizeWe, sizeNs, sizeNwse, sizeNesw;

    public override void Load()
    {
        PluginEvents.AddEvent(EdgeEvent, "Hover", "Pointer rested on a window edge.");
        PluginEvents.AddEvent(ButtonEvent, "Hover", "Pointer rested on a taskbar control.");
        sizeWe = LoadCursor(IntPtr.Zero, (IntPtr)32644);
        sizeNs = LoadCursor(IntPtr.Zero, (IntPtr)32645);
        sizeNwse = LoadCursor(IntPtr.Zero, (IntPtr)32642);
        sizeNesw = LoadCursor(IntPtr.Zero, (IntPtr)32643);
        timeBeginPeriod(1);
        stop?.Cancel();
        stop = new CancellationTokenSource();
        var token = stop.Token;
        var thread = new Thread(() => Poll(token));
        thread.IsBackground = true;
        thread.Start();
    }

    public override void Unload()
    {
        stop?.Cancel();
        timeEndPeriod(1);
    }

    private void Poll(CancellationToken token)
    {
        Target pending = default;
        var since = 0;
        var armedX = 0;
        var armedY = 0;
        Target held = default;
        var drag = false;
        int? gap = null;
        var refreshed = Environment.TickCount;
        Refresh();
        while (!token.IsCancellationRequested)
        {
            try
            {
                var cursor = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
                if (GetCursorInfo(ref cursor) && cursor.Flags == 1)
                {
                    var next = Look(cursor, out var sizing);
                    if (drag && !sizing && !next.IsEmpty && !next.IsButton)
                    {
                        held = next;
                        pending = default;
                    }
                    else if (Advance(ref pending, ref since, ref armedX, ref armedY, ref held, ref gap, next, cursor.Position, Environment.TickCount, sizing) &&
                        !token.IsCancellationRequested)
                        PluginEvents.RaiseEvent(held.IsButton ? ButtonEvent : EdgeEvent);
                    else if (next.IsEmpty && !Near(held, cursor.Position) &&
                             unchecked(Environment.TickCount - refreshed) > 300)
                    {
                        refreshed = Environment.TickCount;
                        if (icons.Length == 0 || OnTaskbar(cursor.Position))
                            Refresh();
                    }
                    drag = sizing;
                }
            }
            catch (Exception) { }
            if (token.WaitHandle.WaitOne(4))
                break;
        }
    }

    private static Target Look(CursorInfo cursor, out bool sizing)
    {
        var edge = Edge(cursor.Cursor);
        sizing = edge != 0 && Moving();
        if (edge != 0 && !sizing)
            return new Target(edge, Rect.Empty);
        foreach (var spot in icons)
            if (spot.Contains(cursor.Position.X, cursor.Position.Y))
                return new Target(Button, spot);
        return default;
    }

    private static bool Advance(ref Target pending, ref int since, ref int armedX, ref int armedY, ref Target held, ref int? gap, Target next, NativePoint point, int now, bool sizing)
    {
        if (sizing && next.IsEmpty)
        {
            if (held.IsEmpty)
            {
                pending = default;
                gap = null;
            }
            return false;
        }
        if (next.IsEmpty && Near(held, point))
            return false;
        var prior = held.IsEmpty ? pending : held;
        if (next.IsEmpty && !prior.IsEmpty && !prior.IsButton)
        {
            gap ??= now;
            if (unchecked(now - gap.Value) < StillMs)
                return false;
        }
        gap = null;
        if (next.IsEmpty)
        {
            pending = default;
            held = default;
            return false;
        }
        if (held.Same(next))
        {
            pending = default;
            return false;
        }
        if (!pending.Same(next))
        {
            pending = next;
            since = now;
            armedX = point.X;
            armedY = point.Y;
            return false;
        }
        var still = point.X == armedX && point.Y == armedY;
        if (unchecked(now - since) < (still ? StillMs : DwellMs))
            return false;
        held = next;
        pending = default;
        return true;
    }

    private static int Edge(IntPtr cursor)
    {
        if (cursor == sizeWe) return We;
        if (cursor == sizeNs) return Ns;
        if (cursor == sizeNwse) return Nwse;
        if (cursor == sizeNesw) return Nesw;
        return 0;
    }

    private static bool Near(Target held, NativePoint point)
    {
        if (!held.IsButton) return false;
        var spot = held.Spot;
        return point.X >= spot.X - Slack && point.Y >= spot.Y - Slack &&
            point.X < spot.Right + Slack && point.Y < spot.Bottom + Slack;
    }

    private static void Refresh()
    {
        if (Interlocked.Exchange(ref busy, true)) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var found = new List<Rect>();
                CollectTray(FindWindow(MainTray, null), found);
                CollectTray(FindWindow(SideTray, null), found);
                if (found.Count > 0) icons = found.ToArray();
            }
            catch (Exception) { }
            Interlocked.Exchange(ref busy, false);
        });
    }

    private static void CollectTray(IntPtr window, List<Rect> found)
    {
        if (window == IntPtr.Zero) return;
        try { Walk(AutomationElement.FromHandle(window), found, 0); }
        catch (Exception) { }
    }

    private static void Walk(AutomationElement element, List<Rect> found, int depth)
    {
        if (element == null || depth > 6) return;
        AutomationElement.AutomationElementInformation state;
        try { state = element.Current; }
        catch (Exception) { return; }
        var spot = state.BoundingRectangle;
        if (state.AutomationId == "StartButton" || state.ClassName == "SystemTray.ShowDesktopButton")
        {
            if (spot.Width > 0 && spot.Height > 0 && spot.Width <= 480 && spot.Height <= 256)
                found.Add(spot);
            return;
        }
        var child = TreeWalker.ControlViewWalker.GetFirstChild(element);
        while (child != null)
        {
            Walk(child, found, depth + 1);
            child = TreeWalker.ControlViewWalker.GetNextSibling(child);
        }
    }

    private static IntPtr Root(NativePoint point) => GetAncestor(WindowFromPoint(point), 2);

    private static bool OnTaskbar(NativePoint point)
    {
        var name = new StringBuilder(32);
        GetClassName(Root(point), name, name.Capacity);
        var cls = name.ToString();
        return cls == MainTray || cls == SideTray;
    }

    private static bool Moving()
    {
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(0, ref info) && info.MoveSize != IntPtr.Zero;
    }

    private readonly struct Target
    {
        public readonly int Kind;
        public readonly Rect Spot;
        public Target(int kind, Rect spot) { Kind = kind; Spot = spot; }
        public bool IsEmpty => Kind == 0;
        public bool IsButton => Kind == Button;
        public bool Same(Target other) =>
            IsButton
                ? other.IsButton && (int)Spot.X == (int)other.Spot.X && (int)Spot.Y == (int)other.Spot.Y
                : Kind != 0 && other.Kind != 0 && !other.IsButton;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int Size, Flags;
        public IntPtr Cursor;
        public NativePoint Position;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size, Flags;
        public IntPtr Active, Focus, Capture, Menu, MoveSize, CaretWindow;
        public NativeRect CaretRect;
    }
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string cls, string name);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);
}
