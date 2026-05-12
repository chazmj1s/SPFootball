using SaturdayPulse.ViewModels;

namespace SaturdayPulse.Views
{
    public partial class FollowingPage : ContentPage
    {
        public FollowingPage(FollowingViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }
    }
}
