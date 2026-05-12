using Microsoft.Maui.Layouts;
using SaturdayPulse.Services;
using SaturdayPulse.ViewModels;

namespace SaturdayPulse.Views
{
    public partial class MainPage : ContentPage
    {
        private readonly MainViewModel               _vm;
        private readonly SchedulePage                _schedulePage;
        private readonly PowerRankingsPage           _rankingsPage;
        private readonly FollowingPage               _followingPage;
        private readonly ProjectionsPage             _projectionsPage;
        private readonly ConfigPage                  _configPage;
        private readonly SharedNavigationStateService _navState;

        public MainPage(
            SharedNavigationStateService navState,
            MainViewModel mainViewModel,
            SchedulePage schedulePage,
            PowerRankingsPage rankingsPage,
            FollowingPage followingPage,
            ProjectionsPage projectionsPage,
            ConfigPage configPage)
        {
            InitializeComponent();

            _navState        = navState;
            _vm              = mainViewModel;
            _schedulePage    = schedulePage;
            _rankingsPage    = rankingsPage;
            _followingPage   = followingPage;
            _projectionsPage = projectionsPage;
            _configPage      = configPage;

            BindingContext = _vm;

            // Build tab items
            _vm.TabItems.Clear();
            var labels = new[] { "Scores", "Rankings", "Following", "Projections", "Config" };
            for (int i = 0; i < labels.Length; i++)
                _vm.TabItems.Add(new TabItem { Label = labels[i], Index = i, IsSelected = i == 0 });

            // Add pages to AbsoluteLayout — each fills the entire host
            AddPageToHost(_schedulePage);
            AddPageToHost(_rankingsPage);
            AddPageToHost(_followingPage);
            AddPageToHost(_projectionsPage);
            AddPageToHost(_configPage);

            // Sync tab underline + page visibility on index change
            _vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.SelectedIndex))
                {
                    SyncTabItems(_vm.SelectedIndex);
                    SyncPage(_vm.SelectedIndex);
                }
                if (e.PropertyName == nameof(MainViewModel.SelectedWeek))
                    ScrollToSelectedWeek();
            };

            // Show initial page
            SyncPage(0);

            // Trigger initial data load
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(200);
                if (_schedulePage.BindingContext is ScheduleViewModel svm && !svm.HasLoaded)
                    await svm.LoadDataAsync();
            });
        }

        // ── Page host helpers ─────────────────────────────────────────────

        private void AddPageToHost(ContentPage page)
        {
            var wrapper = new ContentView
            {
                Content        = page.Content,
                BindingContext = page.BindingContext,
                IsVisible      = false
            };
            AbsoluteLayout.SetLayoutBounds(wrapper, new Rect(0, 0, 1, 1));
            AbsoluteLayout.SetLayoutFlags(wrapper, AbsoluteLayoutFlags.All);
            PageHost.Add(wrapper);
        }

        // ── Tab sync ──────────────────────────────────────────────────────

        private void SyncTabItems(int index)
        {
            foreach (var tab in _vm.TabItems)
                tab.IsSelected = tab.Index == index;
        }

        private void SyncPage(int index)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                // Toggle visibility
                for (int i = 0; i < PageHost.Count; i++)
                    if (PageHost.Children[i] is VisualElement ve)
                        ve.IsVisible = i == index;

                // Lazy load on first visit
                switch (index)
                {
                    case 0 when _schedulePage.BindingContext is ScheduleViewModel svm && !svm.HasLoaded:
                        await svm.LoadDataAsync(); break;
                    case 1 when _rankingsPage.BindingContext is PowerRankingsViewModel rvm && !rvm.HasLoaded:
                        await rvm.LoadDataAsync(); break;
                    case 2 when _followingPage.BindingContext is FollowingViewModel fvm && !fvm.HasLoaded:
                        await fvm.LoadDataAsync(); break;
                    case 3 when _projectionsPage.BindingContext is ProjectionsViewModel pvm && !pvm.HasLoaded:
                        await pvm.LoadDataAsync(); break;
                }
            });
        }

        // ── Week scroll sync ──────────────────────────────────────────────

        private void ScrollToSelectedWeek()
        {
            var weeks    = _vm.Weeks;
            var selected = _vm.SelectedWeek;
            var index    = weeks.ToList().FindIndex(w => w.Week == selected);
            if (index < 0) return;

            const double itemWidth = 42.0;
            var scrollX = Math.Max(0, (index * itemWidth) - 150);

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(100);
                await WeekScrollView.ScrollToAsync(scrollX, 0, animated: true);
            });
        }
    }
}
