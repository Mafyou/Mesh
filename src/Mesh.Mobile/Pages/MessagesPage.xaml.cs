namespace Mesh.Mobile.Pages;

public partial class MessagesPage : ContentPage
{
    private readonly MainPageViewModel _viewModel;

    public MessagesPage(MainPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
        _ = _viewModel.InitializeAsync();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.InitializeAsync();
    }
}
