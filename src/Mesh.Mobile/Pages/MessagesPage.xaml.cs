using System.Collections.Specialized;
using Mesh.Mobile.Core.Models;

namespace Mesh.Mobile.Pages;

public partial class MessagesPage : ContentPage
{
    private readonly MainPageViewModel _viewModel;

    public MessagesPage(MainPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.InitializeAsync();
        _viewModel.Messages.CollectionChanged += OnMessagesChanged;
        ScrollToBottom();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Messages.CollectionChanged -= OnMessagesChanged;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        var last = _viewModel.Messages.LastOrDefault();
        if (last is not null)
            ChatCollectionView.ScrollTo(last, position: ScrollToPosition.End, animate: false);
    }
}
