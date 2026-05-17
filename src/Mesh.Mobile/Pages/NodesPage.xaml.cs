namespace Mesh.Mobile.Pages;

public partial class NodesPage : ContentPage
{
    private readonly NodesPageViewModel _viewModel;

    public NodesPage(NodesPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Subscribe();
        if (!_viewModel.IsScanning && !_viewModel.IsConnected)
            _ = _viewModel.ScanCommand.ExecuteAsync(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Unsubscribe();
    }

    private void OnDeviceSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not CollectionView cv) return;
        if (e.CurrentSelection.FirstOrDefault() is not IDevice device) return;
        cv.SelectedItem = null;
        _ = _viewModel.ConnectDeviceCommand.ExecuteAsync(device);
    }
}
