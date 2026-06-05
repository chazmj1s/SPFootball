using SaturdayPulse.ViewModels;

namespace SaturdayPulse.Views
{
    public partial class PostseasonPage : ContentPage
    {
        public PostseasonPage(PostseasonViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }
    }
}
