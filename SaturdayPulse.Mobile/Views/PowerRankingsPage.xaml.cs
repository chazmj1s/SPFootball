using SaturdayPulse.ViewModels;

namespace SaturdayPulse.Views
{
    public partial class PowerRankingsPage : ContentPage
    {
        public PowerRankingsPage(PowerRankingsViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }
    }
}
