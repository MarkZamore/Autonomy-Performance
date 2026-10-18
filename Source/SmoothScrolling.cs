using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

// Only the settings pane opts in. No timer remains active between wheel gestures.
public sealed class SmoothScrollingController : UIElement, IDisposable
{
    public const double WheelStep = 44;
    public static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(160);
    static readonly DependencyProperty AnimatedOffsetProperty = DependencyProperty.Register(
        "AnimatedOffset", typeof(double), typeof(SmoothScrollingController),
        new PropertyMetadata(0.0, OffsetChanged));
    readonly ScrollViewer viewer;
    readonly List<double> requestedOffsets = new List<double>();
    bool suppressOffset, disposed;
    int animationVersion;
    public double TargetOffset { get; private set; }
    public bool IsAnimating { get; private set; }

    public SmoothScrollingController(ScrollViewer scrollViewer)
    {
        if (scrollViewer == null) throw new ArgumentNullException("scrollViewer");
        viewer = scrollViewer;
        TargetOffset = viewer.VerticalOffset;
        viewer.PreviewMouseWheel += OnWheel;
        viewer.PreviewMouseDown += OnManualMouse;
        viewer.PreviewKeyDown += OnManualKey;
        viewer.PreviewTouchDown += OnManualTouch;
        viewer.ScrollChanged += OnScrollChanged;
        viewer.Unloaded += OnUnloaded;
    }

    public bool ScrollByWheelDelta(int delta)
    {
        if (disposed || delta == 0 || viewer.ScrollableHeight <= 0) return false;
        double current = viewer.VerticalOffset;
        double next = Clamp((IsAnimating ? TargetOffset : current) - delta * WheelStep / 120.0);
        StopAnimation(false);
        TargetOffset = next;
        if (Math.Abs(next - current) < 0.01) return true;
        if (!SystemParameters.ClientAreaAnimation) { RequestOffset(next); return true; }
        suppressOffset = true;
        SetValue(AnimatedOffsetProperty, current);
        suppressOffset = false;
        IsAnimating = true;
        int version = ++animationVersion;
        DoubleAnimation animation = new DoubleAnimation(current, next, new Duration(AnimationDuration));
        animation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
        animation.Completed += delegate {
            if (version != animationVersion || disposed) return;
            suppressOffset = true;
            SetValue(AnimatedOffsetProperty, TargetOffset);
            BeginAnimation(AnimatedOffsetProperty, null);
            suppressOffset = false;
            IsAnimating = false;
            RequestOffset(TargetOffset);
        };
        BeginAnimation(AnimatedOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
        return true;
    }

    public void Cancel()
    {
        StopAnimation(true);
    }

    void StopAnimation(bool clearRequests)
    {
        ++animationVersion;
        IsAnimating = false;
        suppressOffset = true;
        BeginAnimation(AnimatedOffsetProperty, null);
        SetValue(AnimatedOffsetProperty, viewer.VerticalOffset);
        suppressOffset = false;
        TargetOffset = viewer.VerticalOffset;
        if (clearRequests) requestedOffsets.Clear();
    }

    double Clamp(double offset) { return Math.Max(0, Math.Min(viewer.ScrollableHeight, offset)); }
    static void OffsetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        SmoothScrollingController controller = (SmoothScrollingController)sender;
        if (!controller.suppressOffset && !controller.disposed)
            controller.RequestOffset((double)e.NewValue);
    }
    void RequestOffset(double offset)
    {
        offset = Clamp(offset);
        // ScrollToVerticalOffset is queued by WPF. Remember requests so its later
        // ScrollChanged event is distinguishable from an external/manual scroll.
        requestedOffsets.Add(offset);
        if (requestedOffsets.Count > 64) requestedOffsets.RemoveAt(0);
        viewer.ScrollToVerticalOffset(offset);
    }
    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || IsComboBoxSource(e.OriginalSource as DependencyObject)) return;
        if (ScrollByWheelDelta(e.Delta)) e.Handled = true;
    }
    bool IsComboBoxSource(DependencyObject source)
    {
        while (source != null && source != viewer) {
            if (source is ComboBox || source is ComboBoxItem) return true;
            DependencyObject parent = null;
            if (source is Visual || source is System.Windows.Media.Media3D.Visual3D)
                parent = VisualTreeHelper.GetParent(source);
            if (parent == null) parent = LogicalTreeHelper.GetParent(source);
            source = parent;
        }
        return false;
    }
    void OnManualMouse(object sender, MouseButtonEventArgs e) { Cancel(); }
    void OnManualKey(object sender, KeyEventArgs e) { Cancel(); }
    void OnManualTouch(object sender, TouchEventArgs e) { Cancel(); }
    void OnUnloaded(object sender, RoutedEventArgs e) { Cancel(); }
    void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource != viewer) return;
        if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0) { Cancel(); return; }
        if (e.VerticalChange == 0) return;
        for (int i = requestedOffsets.Count - 1; i >= 0; --i) {
            if (Math.Abs(requestedOffsets[i] - viewer.VerticalOffset) < 1.0) {
                requestedOffsets.RemoveRange(0, i + 1);
                return;
            }
        }
        if (IsAnimating) Cancel();
        else TargetOffset = viewer.VerticalOffset;
    }
    public void Dispose()
    {
        if (disposed) return;
        Cancel();
        disposed = true;
        viewer.PreviewMouseWheel -= OnWheel;
        viewer.PreviewMouseDown -= OnManualMouse;
        viewer.PreviewKeyDown -= OnManualKey;
        viewer.PreviewTouchDown -= OnManualTouch;
        viewer.ScrollChanged -= OnScrollChanged;
        viewer.Unloaded -= OnUnloaded;
    }
}
