using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Skua.Shared.Avalonia.Services;

/// <summary>
/// Provides client-side resize borders and interactive resizing for custom chrome (undecorated)
/// windows on Linux desktop platforms where the window manager does not draw sizing frames.
/// </summary>
public static class WindowResizeHelper
{
    private const double BorderThickness = 7.0;
    private const double CornerThickness = 14.0;

    private static readonly ConditionalWeakTable<Window, object> AttachedWindows = new();
    private static readonly ConditionalWeakTable<Window, HorizontalResizeState> ActiveResizes = new();

    private static readonly Cursor CursorTopLeft = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor CursorTopRight = new(StandardCursorType.TopRightCorner);
    private static readonly Cursor CursorBottomLeft = new(StandardCursorType.BottomLeftCorner);
    private static readonly Cursor CursorBottomRight = new(StandardCursorType.BottomRightCorner);
    private static readonly Cursor CursorNorthSouth = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor CursorWestEast = new(StandardCursorType.SizeWestEast);

    public static void Attach(Window window)
    {
        if (window == null)
            return;

        lock (AttachedWindows)
        {
            if (AttachedWindows.TryGetValue(window, out _))
                return;
            AttachedWindows.Add(window, new object());
        }

        window.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerExitedEvent, OnPointerExited, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Window window)
            return;

        if (!window.CanResize || window.WindowState != WindowState.Normal)
        {
            if (window.Cursor != null)
                window.Cursor = null;
            return;
        }

        Point pos = e.GetPosition(window);
        WindowEdge? edge = GetEdge(pos, window.Bounds.Size);
        if (ActiveResizes.TryGetValue(window, out HorizontalResizeState? resize))
        {
            UpdateHorizontalResize(window, e, resize);
            return;
        }

        // If mouse button is held and we are not in resize mode, user is dragging content inside window
        var pointProps = e.GetCurrentPoint(window).Properties;
        if (pointProps.IsLeftButtonPressed && window.Cursor == null)
            return;
        if (edge.HasValue)
        {
            window.Cursor = GetCursorForEdge(edge.Value);
        }
        else if (window.Cursor != null)
        {
            window.Cursor = null;
        }
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Window window)
            return;

        if (!e.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
            return;

        if (!window.CanResize || window.WindowState != WindowState.Normal)
            return;

        Point pos = e.GetPosition(window);
        WindowEdge? edge = GetEdge(pos, window.Bounds.Size);
        if (!edge.HasValue)
            return;

        try
        {
            if (edge is WindowEdge.West or WindowEdge.East)
            {
                ActiveResizes.Remove(window);
                ActiveResizes.Add(window, new HorizontalResizeState(
                    edge.Value,
                    window.PointToScreen(e.GetPosition(window)),
                    window.Position,
                    window.Bounds.Width));
                e.Pointer.Capture(window);
                e.Handled = true;
                return;
            }

            // If window was using SizeToContent, fix the current dimensions to prevent layout collapse
            if (window.SizeToContent != SizeToContent.Manual)
            {
                if (double.IsNaN(window.Width) || window.Width <= 0)
                    window.Width = window.Bounds.Width;
                if (double.IsNaN(window.Height) || window.Height <= 0)
                    window.Height = window.Bounds.Height;
                window.SizeToContent = SizeToContent.Manual;
            }

            window.BeginResizeDrag(edge.Value, e);
            e.Handled = true;
        }
        catch { }
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Window window)
            return;

        Point pos = e.GetPosition(window);
        WindowEdge? edge = GetEdge(pos, window.Bounds.Size);
        ActiveResizes.Remove(window);
        e.Pointer.Capture(null);
        if (!edge.HasValue && window.Cursor != null)
            window.Cursor = null;
    }

    private static void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (sender is Window window)
        {
            ActiveResizes.Remove(window);
            window.Cursor = null;
        }
    }

    private static void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Window window && window.Cursor != null)
            window.Cursor = null;
    }

    private static WindowEdge? GetEdge(Point point, Size size)
    {
        double x = point.X;
        double y = point.Y;
        double w = size.Width;
        double h = size.Height;

        if (w <= CornerThickness * 2 || h <= CornerThickness * 2)
            return null;

        if (x < 0 || y < 0 || x > w || y > h)
            return null;

        bool top = y <= BorderThickness;
        bool bottom = y >= h - BorderThickness;
        bool left = x <= BorderThickness;
        bool right = x >= w - BorderThickness;

        bool topCorner = y <= CornerThickness;
        bool bottomCorner = y >= h - CornerThickness;
        bool leftCorner = x <= CornerThickness;
        bool rightCorner = x >= w - CornerThickness;

        if (topCorner && leftCorner) return WindowEdge.NorthWest;
        if (topCorner && rightCorner) return WindowEdge.NorthEast;
        if (bottomCorner && leftCorner) return WindowEdge.SouthWest;
        if (bottomCorner && rightCorner) return WindowEdge.SouthEast;

        if (top) return WindowEdge.North;
        if (bottom) return WindowEdge.South;
        if (left) return WindowEdge.West;
        if (right) return WindowEdge.East;

        return null;
    }

    private static Cursor GetCursorForEdge(WindowEdge edge) => edge switch
    {
        WindowEdge.NorthWest => CursorTopLeft,
        WindowEdge.NorthEast => CursorTopRight,
        WindowEdge.SouthWest => CursorBottomLeft,
        WindowEdge.SouthEast => CursorBottomRight,
        WindowEdge.North or WindowEdge.South => CursorNorthSouth,
        WindowEdge.West or WindowEdge.East => CursorWestEast,
        _ => Cursor.Default
    };

    private static void UpdateHorizontalResize(Window window, PointerEventArgs e, HorizontalResizeState resize)
    {
        PixelPoint current = window.PointToScreen(e.GetPosition(window));
        int delta = current.X - resize.StartPointer.X;
        double width = resize.StartWidth;
        int left = resize.StartPosition.X;

        if (resize.Edge == WindowEdge.East)
            width += delta;
        else
        {
            width -= delta;
            left += delta;
        }

        double minimum = Math.Max(window.MinWidth, 320);
        if (width < minimum)
        {
            if (resize.Edge == WindowEdge.West)
                left = resize.StartPosition.X + (int)(resize.StartWidth - minimum);
            width = minimum;
        }

        window.Width = width;
        if (resize.Edge == WindowEdge.West)
            window.Position = new PixelPoint(left, resize.StartPosition.Y);
    }

    private sealed record HorizontalResizeState(
        WindowEdge Edge,
        PixelPoint StartPointer,
        PixelPoint StartPosition,
        double StartWidth);
}
