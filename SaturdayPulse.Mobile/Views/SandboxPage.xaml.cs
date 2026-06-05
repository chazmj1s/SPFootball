using SaturdayPulse.ViewModels;

namespace SaturdayPulse.Views
{
    public partial class SandboxPage : ContentPage
    {
        public SandboxPage(SandboxViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }
    }
}
