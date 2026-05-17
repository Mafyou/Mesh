using Mesh.Mobile.ViewModels;
using Plugin.BLE.Abstractions.Contracts;

namespace Mesh.Mobile;

public partial class MainPage : ContentPage
{
    private readonly MainPageViewModel _viewModel;

    public MainPage(MainPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
        _ = _viewModel.InitializeAsync();
    }

    private async void OnNodeSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not CollectionView collectionView) return;
        if (e.CurrentSelection.FirstOrDefault() is not IDevice device) return;
        collectionView.SelectedItem = null;
        await _viewModel.ConnectSelectedNodeCommand.ExecuteAsync(device);
    }
}
