using System;
using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Erya.Controls;

/// <summary>
/// 可拖拽排序的 ItemsControl。行模板里放一个把手（<see cref="DragHandle"/>，或给
/// 任意元素设置 IsDragHandle="True"），按住把手移动超过阈值（4px）即开始拖拽：
/// 指针越过相邻行中点时集合实时 Move，其余行随之腾位；松手落位，Esc 放回起始位置。
/// 行高不要求一致（按各行中点判定），不依赖虚拟化面板（默认 StackPanel）。
/// ItemsSource 需为可变列表，ObservableCollection 走原生 Move（保留容器、不重建）。
/// </summary>
public class ReorderableItemsControl : ItemsControl
{
    /// <summary>把手标记：值为 True 的元素上按下即开始该行拖拽（DragHandle 已自带）。</summary>
    public static readonly AttachedProperty<bool> IsDragHandleProperty =
        AvaloniaProperty.RegisterAttached<ReorderableItemsControl, Control, bool>("IsDragHandle");

    public static void SetIsDragHandle(Control element, bool value)
        => element.SetValue(IsDragHandleProperty, value);

    public static bool GetIsDragHandle(Control element)
        => element.GetValue(IsDragHandleProperty);

    private const double DragThreshold = 4;
    private const double AutoScrollEdge = 40;
    private const double AutoScrollStep = 12;

    private object? _dragItem;
    private int _dragIndex;
    private int _dragStartIndex;
    private Point _pressPoint;
    private Point _lastPointerPosition;
    private bool _isDragging;
    private IPointer? _dragPointer;
    private Control? _pressHandle;
    private InputElement? _escapeRoot;
    private ScrollViewer? _scrollViewer;
    private DispatcherTimer? _autoScrollTimer;
    private MethodInfo? _moveMethod;

    public ReorderableItemsControl()
    {
        // 把手可能在行模板深处，且子元素可能已处理按压：接管所有冒泡按压。
        AddHandler(PointerPressedEvent, OnHandlePressed, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = null;
        return item is not Control;
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
        => new ReorderableItem();

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (container is ReorderableItem row)
        {
            row.Content = item;
            row.ContentTemplate = ItemTemplate;
        }
    }

    private void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_dragItem is not null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is not Visual source) return;

        var handle = source.GetSelfAndVisualAncestors()
            .TakeWhile(v => !ReferenceEquals(v, this))
            .OfType<Control>()
            .FirstOrDefault(GetIsDragHandle);
        if (handle is null) return;

        var container = handle.GetVisualAncestors()
            .TakeWhile(v => !ReferenceEquals(v, this))
            .OfType<Control>()
            .FirstOrDefault(c => IndexFromContainer(c) >= 0);
        if (container is null) return;

        _dragStartIndex = _dragIndex = IndexFromContainer(container);
        _dragItem = Items[_dragIndex];
        _pressPoint = _lastPointerPosition = e.GetPosition(this);
        _scrollViewer = this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();

        _dragPointer = e.Pointer;
        e.Pointer.Capture(this);
        // 把手上的按压不作为行内交互（勾选、按钮等）处理。
        e.Handled = true;

        // 按压反馈（把手 + 行）：指针已捕获，释放事件不再路由到行内元素，
        // 伪类若交给 Avalonia 自动维护可能卡住，这里手动置位/复位
        //（伪类只能由控件自己设置，走各自的 internal SetPressed）。
        _pressHandle = handle;
        if (handle is DragHandle dragHandle) dragHandle.SetPressed(true);
        if (container is ReorderableItem row) row.SetPressed(true);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragItem is null || !ReferenceEquals(e.Pointer, _dragPointer)) return;

        _lastPointerPosition = e.GetPosition(this);
        if (!_isDragging)
        {
            var delta = _lastPointerPosition - _pressPoint;
            if (Math.Abs(delta.X) <= DragThreshold && Math.Abs(delta.Y) <= DragThreshold) return;

            _isDragging = true;
            ApplyDragState();
            // 捕获期间光标跟随捕获元素（列表本身）：保持 ↑↓ 提示拖拽方向。
            Cursor = new Cursor(StandardCursorType.DragMove);
            HookEscape();
            StartAutoScroll();
        }
        UpdateTarget(_lastPointerPosition);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragItem is null || !ReferenceEquals(e.Pointer, _dragPointer)) return;
        e.Handled = true;
        EndDrag();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_dragItem is not null) EndDrag();
    }

    private void OnEscapeDuringDrag(object? sender, KeyEventArgs e)
    {
        if (!_isDragging || e.Key != Key.Escape) return;
        e.Handled = true;
        MoveItem(_dragIndex, _dragStartIndex);
        _dragIndex = _dragStartIndex;
        EndDrag();
    }

    /// <summary>
    /// 指针在“去掉被拖行”的序列里越过了多少行中点，即为落点槽位，也是 Move 的
    /// 目标下标（Move 语义 = 先移除再插入到目标位置）。布局是批处理的，Move 后
    /// 一帧内 Bounds 可能滞后——判定只在指针移动时进行且以中点为界，指针继续
    /// 移动会自我纠正。集合 Move 会重建行容器，故每次 Move 后按新下标重新施加
    /// 拖拽视觉。
    /// </summary>
    private void UpdateTarget(Point position)
    {
        if (_dragItem is null || ItemsPanelRoot is not { } panel) return;
        var dragContainer = ContainerFromIndex(_dragIndex);
        if (this.TranslatePoint(position, panel)?.Y is not { } pointerY) return;

        int slot = 0;
        foreach (var child in panel.Children)
        {
            if (child is Control row && !ReferenceEquals(row, dragContainer)
                && pointerY > row.Bounds.Top + row.Bounds.Height / 2)
            {
                slot++;
            }
        }

        var target = Math.Clamp(slot, 0, Math.Max(0, Items.Count - 1));
        if (target != _dragIndex)
        {
            MoveItem(_dragIndex, target);
            _dragIndex = target;
            ApplyDragState();
        }
    }

    /// <summary>
    /// ItemsSource 是 ObservableCollection 时走其原生 Move（容器随项保留）；
    /// 否则退化为 Remove+Insert。未绑定 ItemsSource 时调整控件自身的 Items。
    /// </summary>
    private void MoveItem(int from, int to)
    {
        if (from == to || from < 0 || from >= Items.Count) return;
        to = Math.Clamp(to, 0, Items.Count - 1);

        if (ItemsSource is IList source)
        {
            _moveMethod ??= source.GetType().GetMethod("Move", new[] { typeof(int), typeof(int) });
            if (_moveMethod is not null)
            {
                _moveMethod.Invoke(source, new object[] { from, to });
                return;
            }
            var item = source[from]!;
            source.RemoveAt(from);
            source.Insert(to, item);
        }
        else
        {
            var item = Items[from]!;
            Items.RemoveAt(from);
            Items.Insert(to, item);
        }
    }

    private void HookEscape()
    {
        // 拖拽期间在顶层隧道监听 Esc；不抢焦点，任何聚焦状态都能取消。
        if (VisualRoot is not InputElement root) return;
        _escapeRoot = root;
        root.AddHandler(KeyDownEvent, OnEscapeDuringDrag, RoutingStrategies.Tunnel);
    }

    private void StartAutoScroll()
    {
        if (_scrollViewer is null) return;
        _autoScrollTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(30), DispatcherPriority.Background, (_, _) => AutoScroll());
        _autoScrollTimer.Start();
    }

    private void AutoScroll()
    {
        if (_scrollViewer is not { } viewer || VisualRoot is null) return;
        if (this.TranslatePoint(_lastPointerPosition, viewer) is not { } pos) return;

        var viewport = viewer.Viewport.Height;
        var offset = viewer.Offset;
        if (pos.Y < AutoScrollEdge && offset.Y > 0)
            viewer.Offset = new Vector(offset.X, Math.Max(0, offset.Y - AutoScrollStep));
        else if (pos.Y > viewport - AutoScrollEdge && offset.Y < viewer.Extent.Height - viewport)
            viewer.Offset = new Vector(offset.X, Math.Min(
                viewer.Extent.Height - viewport, offset.Y + AutoScrollStep));
    }

    private void EndDrag()
    {
        _autoScrollTimer?.Stop();
        _autoScrollTimer = null;
        if (_escapeRoot is { } root)
        {
            root.RemoveHandler(KeyDownEvent, OnEscapeDuringDrag);
            _escapeRoot = null;
        }
        if (_isDragging && ContainerFromIndex(_dragIndex) is { } container)
        {
            SetDragging(container, false);
            container.ZIndex = 0;
        }
        // 行内容在集合 Move 时可能已重建，旧把手/容器的清理尽力而为即可。
        if (_pressHandle is DragHandle oldHandle) oldHandle.SetPressed(false);
        _pressHandle = null;
        if (ContainerFromIndex(_dragIndex) is ReorderableItem currentRow) currentRow.SetPressed(false);
        Cursor = null;
        _dragPointer?.Capture(null);
        _dragPointer = null;
        _dragItem = null;
        _isDragging = false;
    }

    /// <summary>拖拽视觉挂在当前下标的容器上（集合 Move 会重建容器，需重施加）。</summary>
    private void ApplyDragState()
    {
        if (ContainerFromIndex(_dragIndex) is not { } container) return;
        SetDragging(container, true);
        container.ZIndex = 100;
    }

    /// <summary>拖拽视觉只作用于自家行容器；直接子项为 Control 时无拖拽视觉（伪类不可外部设置）。</summary>
    private static void SetDragging(Control container, bool value)
    {
        if (container is ReorderableItem row) row.SetDragging(value);
    }
}
