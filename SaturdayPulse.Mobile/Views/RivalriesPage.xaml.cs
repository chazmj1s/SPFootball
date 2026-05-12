using SaturdayPulse.ViewModels;

namespace SaturdayPulse.Views
{
    public partial class RivalriesPage : ContentPage
    {
        private readonly RivalriesViewModel _viewModel;

        public RivalriesPage(RivalriesViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (_viewModel.Rivalries.Count == 0)
                await _viewModel.LoadDataAsync();
        }
    }
}
