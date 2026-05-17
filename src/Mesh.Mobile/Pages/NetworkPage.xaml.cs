namespace Mesh.Mobile.Pages;

public partial class NetworkPage : ContentPage
{
    private readonly NetworkPageViewModel _viewModel;

    public NetworkPage(NetworkPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Initialize();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Cleanup();
    }
}
