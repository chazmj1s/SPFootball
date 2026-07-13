using SaturdayPulse.ViewModels;

namespace SaturdayPulse.Views
{
    public partial class MyTeamsPage : ContentPage
    {
        // NOTE: no OnAppearing here. MainPage.xaml.cs's AddPageToHost extracts
        // this page's .Content into a ContentView wrapper at startup and never
        // shows this ContentPage itself — only the wrapper's IsVisible toggles
        // on tab switch. OnAppearing/OnDisappearing never fire under that
        // architecture. Initial load is triggered from MainPage.xaml.cs's
        // SyncPage lazy-load switch instead, exactly like Schedule/Rankings/
        // Postseason/Settings (case N when ... !mvm.HasLoaded: _ = mvm.InitializeAsync()).

        public MyTeamsPage(MyTeamsViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }
    }
}
