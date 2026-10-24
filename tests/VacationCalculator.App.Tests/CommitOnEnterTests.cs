using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using VacationCalculator.App.Behaviors;

namespace VacationCalculator.App.Tests;

public class CommitOnEnterTests
{
    private sealed class Source : System.ComponentModel.INotifyPropertyChanged
    {
        public string Value { get; set; } = "old";
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    }

    private static void RunSta(Action a)
    {
        Exception? ex = null;
        var t = new Thread(() => { try { a(); } catch (Exception e) { ex = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (ex != null) throw new Exception("STA failure", ex);
    }

    private static KeyEventArgs Press(TextBox box, Key key)
    {
        var src = new HwndSource(new HwndSourceParameters("t") { Width = 10, Height = 10 });
        var e = new KeyEventArgs(Keyboard.PrimaryDevice, src, 0, key) { RoutedEvent = UIElement.PreviewKeyDownEvent };
        box.RaiseEvent(e);
        return e;
    }

    private static (TextBox box, Source src) Make(bool enabled)
    {
        var src = new Source();
        var box = new TextBox();
        box.SetBinding(TextBox.TextProperty, new Binding("Value")
        { Source = src, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.Explicit });
        if (enabled) CommitOnEnter.SetIsEnabled(box, true);
        box.Text = "new";
        return (box, src);
    }

    [Fact]
    public void Enter_pushes_text_to_source_and_marks_handled()
    {
        RunSta(() =>
        {
            var (box, src) = Make(true);
            var e = Press(box, Key.Enter);
            Assert.Equal("new", src.Value);
            Assert.True(e.Handled);
        });
    }

    [Fact]
    public void Other_keys_do_nothing()
    {
        RunSta(() =>
        {
            var (box, src) = Make(true);
            var e = Press(box, Key.A);
            Assert.Equal("old", src.Value);
            Assert.False(e.Handled);
        });
    }

    [Fact]
    public void Disabled_behavior_does_nothing()
    {
        RunSta(() =>
        {
            var (box, src) = Make(false);
            var e = Press(box, Key.Enter);
            Assert.Equal("old", src.Value);
            Assert.False(e.Handled);
        });
    }

    [Fact]
    public void Disabling_again_detaches()
    {
        RunSta(() =>
        {
            var (box, src) = Make(true);
            CommitOnEnter.SetIsEnabled(box, false);
            var e = Press(box, Key.Enter);
            Assert.Equal("old", src.Value);
            Assert.False(e.Handled);
        });
    }
}
