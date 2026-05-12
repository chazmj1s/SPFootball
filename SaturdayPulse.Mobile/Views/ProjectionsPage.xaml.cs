using SaturdayPulse.ViewModels;

namespace SaturdayPulse.Views
{
    public partial class ProjectionsPage : ContentPage
    {
        public ProjectionsPage(ProjectionsViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }
    }
}