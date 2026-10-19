using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Erya.Controls;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// 拖拽排序的指针级验收：越过行中点实时重排（松手落位）、阈值内不算拖拽、
/// 按在行内容上不触发、Esc 放回起始位置。
/// </summary>
public class ReorderableItemsControlTests
{
    private static (Window Window, ReorderableItemsControl List, ObservableCollection<string> Items) CreateList()
    {
        var items = new ObservableCollection<string> { "A", "B", "C", "D" };
        var list = new ReorderableItemsControl
        {
            Width = 360,
            ItemsSource = items,
            ItemTemplate = new FuncDataTemplate<string>((item, _) => new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 6),
                Children =
                {
                    new DragHandle { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                    new TextBlock { Text = item, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                },
            }),
        };
        var window = new Window { Width = 480, Height = 420, Content = list };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, list, items);
    }

    private static Point CenterIn(Control control, Window window)
        => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    /// <summary>列表里第 <paramref name="index"/> 行的把手（视觉顺序 = 集合顺序）。</summary>
    private static DragHandle Handle(ReorderableItemsControl list, int index)
        => list.GetVisualDescendants().OfType<DragHandle>().ToList()[index];

    [AvaloniaFact]
    public void Dragging_past_midpoints_reorders_live_and_commits_on_release()
    {
        var (window, list, items) = CreateList();
        var press = CenterIn(Handle(list, 0), window);
        var bottom = CenterIn(Handle(list, 3), window) + new Vector(0, 2);

        window.MouseDown(press, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseMove(bottom);
        Dispatcher.UIThread.RunJobs();

        // 越过各行中点即实时腾位（拖拽中集合已重排，未松手）。
        Assert.Equal(["B", "C", "D", "A"], items);
        var dragging = list.GetVisualDescendants().OfType<ReorderableItem>().Single(r => r.Classes.Contains(":dragging"));
        Assert.Equal("A", dragging.Content);

        window.MouseUp(bottom, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["B", "C", "D", "A"], items);
        Assert.DoesNotContain(list.GetVisualDescendants().OfType<ReorderableItem>(),
            r => r.Classes.Contains(":dragging"));
    }

    [AvaloniaFact]
    public void Dragging_one_row_down_swaps_with_the_next()
    {
        var (window, list, items) = CreateList();
        var press = CenterIn(Handle(list, 1), window);
        var below = CenterIn(Handle(list, 2), window) + new Vector(0, 2);

        window.MouseDown(press, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseMove(below);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(below, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["A", "C", "B", "D"], items);
    }

    [AvaloniaFact]
    public void Dragging_the_last_row_to_the_top()
    {
        var (window, list, items) = CreateList();
        var press = CenterIn(Handle(list, 3), window);
        var above = CenterIn(Handle(list, 0), window) - new Vector(0, 2);

        window.MouseDown(press, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseMove(above);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(above, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["D", "A", "B", "C"], items);
    }

    [AvaloniaFact]
    public void Movement_below_the_threshold_is_not_a_drag()
    {
        var (window, list, items) = CreateList();
        var press = CenterIn(Handle(list, 0), window);
        var jiggle = press + new Vector(1, 2);

        window.MouseDown(press, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseMove(jiggle);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(jiggle, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["A", "B", "C", "D"], items);
        Assert.DoesNotContain(list.GetVisualDescendants().OfType<ReorderableItem>(),
            r => r.Classes.Contains(":dragging"));
    }

    [AvaloniaFact]
    public void Pressing_on_row_content_does_not_start_a_drag()
    {
        var (window, list, items) = CreateList();
        var label = list.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "A");
        var press = CenterIn(label, window);
        var bottom = CenterIn(Handle(list, 3), window) + new Vector(0, 2);

        window.MouseDown(press, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseMove(bottom);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(bottom, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["A", "B", "C", "D"], items);
    }

    [AvaloniaFact]
    public void Row_shows_pointerover_on_hover()
    {
        var (window, list, items) = CreateList();
        var handle = Handle(list, 1);

        window.MouseMove(CenterIn(handle, window));
        Dispatcher.UIThread.RunJobs();

        Assert.True(list.ContainerFromIndex(1)!.Classes.Contains(":pointerover"));
        Assert.True(handle.Classes.Contains(":pointerover"));
        Assert.False(list.ContainerFromIndex(0)!.Classes.Contains(":pointerover"));
    }

    [AvaloniaFact]
    public void Handle_and_row_show_pressed_while_the_handle_is_held()
    {
        var (window, list, items) = CreateList();
        var handle = Handle(list, 0);
        var press = CenterIn(handle, window);

        window.MouseDown(press, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(handle.Classes.Contains(":pressed"));
        Assert.True(list.ContainerFromIndex(0)!.Classes.Contains(":pressed"));

        window.MouseUp(press, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(handle.Classes.Contains(":pressed"));
        Assert.False(list.ContainerFromIndex(0)!.Classes.Contains(":pressed"));
        Assert.Equal(["A", "B", "C", "D"], items);
    }

    [AvaloniaFact]
    public void Escape_puts_the_row_back()
    {
        var (window, list, items) = CreateList();
        var press = CenterIn(Handle(list, 0), window);
        var bottom = CenterIn(Handle(list, 3), window) + new Vector(0, 2);

        window.MouseDown(press, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseMove(bottom);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["B", "C", "D", "A"], items);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "Escape");
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(bottom, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["A", "B", "C", "D"], items);
    }
}
