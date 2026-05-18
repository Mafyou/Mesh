namespace Mesh.Mobile.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsPageViewModel _viewModel;

    public SettingsPage(SettingsPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Subscribe();
        _viewModel.Refresh();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Unsubscribe();
    }
}
