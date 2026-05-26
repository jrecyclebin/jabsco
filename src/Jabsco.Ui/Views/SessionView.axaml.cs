using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using MouseButton = Jabsco.Common.Events.MouseButton;
using ScrollDirection = Jabsco.Common.Events.ScrollDirection;
using Jabsco.Ui.Controls;
using Jabsco.Ui.ViewModels;

namespace Jabsco.Ui.Views;

public partial class SessionView : UserControl
{
    private const double NearBottomThreshold = 60.0;

    private FramebufferControl? _framebuffer;
    private BackdropBlurControl? _chatBackdropBlur;
    private ScrollViewer? _chatScrollViewer;
    private ScrollViewer? _historyScrollViewer;
    private SessionViewModel? _vm;

    public SessionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.ChatItems.CollectionChanged    -= OnChatItemsChanged;
            _vm.HistoryItems.CollectionChanged -= OnHistoryItemsChanged;
        }

        _vm = DataContext as SessionViewModel;

        if (_vm != null)
        {
            _vm.ChatItems.CollectionChanged    += OnChatItemsChanged;
            _vm.HistoryItems.CollectionChanged += OnHistoryItemsChanged;
        }
    }

    private bool IsNearBottom(ScrollViewer? sv)
    {
        if (sv == null) return true;
        return sv.Extent.Height - sv.Offset.Y - sv.Viewport.Height <= NearBottomThreshold;
    }

    private void OnChatItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || !IsNearBottom(_chatScrollViewer)) return;
        Dispatcher.UIThread.Post(() => _chatScrollViewer?.ScrollToEnd(), DispatcherPriority.Loaded);
    }

    private void OnHistoryItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || !IsNearBottom(_historyScrollViewer)) return;
        Dispatcher.UIThread.Post(() => _historyScrollViewer?.ScrollToEnd(), DispatcherPriority.Loaded);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _framebuffer        = this.FindControl<FramebufferControl>("FramebufferCtrl");
        _chatBackdropBlur   = this.FindControl<BackdropBlurControl>("ChatBackdropBlur");
        _chatScrollViewer   = this.FindControl<ScrollViewer>("ChatScrollViewer");
        _historyScrollViewer = this.FindControl<ScrollViewer>("HistoryScrollViewer");

        Dispatcher.UIThread.Post(() => _chatScrollViewer?.ScrollToEnd(), DispatcherPriority.Loaded);

        if (_chatBackdropBlur != null)
            _chatBackdropBlur.SourceControl = _framebuffer;

        if (_framebuffer == null) return;
        _framebuffer.PointerPressed      += OnPointerPressed;
        _framebuffer.PointerMoved        += OnPointerMoved;
        _framebuffer.PointerWheelChanged += OnPointerWheelChanged;
        _framebuffer.KeyDown             += OnKeyDown;
        _framebuffer.TextInput           += OnTextInput;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_framebuffer != null)
        {
            _framebuffer.PointerPressed      -= OnPointerPressed;
            _framebuffer.PointerMoved        -= OnPointerMoved;
            _framebuffer.PointerWheelChanged -= OnPointerWheelChanged;
            _framebuffer.KeyDown             -= OnKeyDown;
            _framebuffer.TextInput           -= OnTextInput;
        }

        if (_chatBackdropBlur != null)
            _chatBackdropBlur.SourceControl = null;

        base.OnDetachedFromVisualTree(e);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null || _framebuffer == null || !_vm.CanInteract) return;
        _framebuffer.Focus();
        var (x, y) = _framebuffer.ToRdpCoords(e.GetPosition(_framebuffer));
        var props = e.GetCurrentPoint(_framebuffer).Properties;
        var button = props.IsRightButtonPressed ? MouseButton.Right
            : props.IsMiddleButtonPressed ? MouseButton.Middle
            : MouseButton.Left;
        _ = _vm.SendMouseClickAsync(button, x, y);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_vm == null || _framebuffer == null || !_vm.CanInteract) return;
        var (x, y) = _framebuffer.ToRdpCoords(e.GetPosition(_framebuffer));
        _ = _vm.SendMouseMoveAsync(x, y);
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_vm == null || _framebuffer == null || !_vm.CanInteract) return;
        var (x, y) = _framebuffer.ToRdpCoords(e.GetPosition(_framebuffer));
        var direction = e.Delta.Y >= 0 ? ScrollDirection.Up : ScrollDirection.Down;
        _ = _vm.SendScrollAsync(x, y, direction, 3);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm == null || !_vm.CanInteract) return;
        var chord = KeyTranslator.ToChord(e);
        if (chord == null) return;
        e.Handled = true;
        _ = _vm.SendKeyPressAsync(chord);
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (_vm == null || !_vm.CanInteract || string.IsNullOrEmpty(e.Text)) return;
        e.Handled = true;
        _ = _vm.SendTextAsync(e.Text);
    }

    private void OnPromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (sender is TextBox tb)
            {
                var idx = tb.CaretIndex;
                tb.Text = (tb.Text ?? "").Insert(idx, "\n");
                tb.CaretIndex = idx + 1;
            }
            return;
        }
        _vm?.SendPromptCommand.Execute(null);
    }

    private void OnCommandSuggestionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is string name)
        {
            lb.SelectedItem = null;
            _vm?.SelectSuggestion(name);
        }
    }
}
