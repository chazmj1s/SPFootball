using Microsoft.Maui.Layouts;
using SaturdayPulse.Services;
using SaturdayPulse.ViewModels;
using System.ComponentModel;

namespace SaturdayPulse.Views
{
    public partial class MainPage : ContentPage
    {
        private readonly MainViewModel                _vm;
        private readonly SchedulePage                 _schedulePage;
        private readonly PowerRankingsPage            _rankingsPage;
        private readonly SettingsPage                 _SettingsPage;
        private readonly ProjectionsPage              _projectionsPage;
        private readonly SharedNavigationStateService _navState;

        private CancellationTokenSource? _loadingAnimCts;

        public MainPage(
            SharedNavigationStateService navState,
            MainViewModel mainViewModel,
            SchedulePage schedulePage,
            PowerRankingsPage rankingsPage,
            SettingsPage SettingsPage,
            ProjectionsPage projectionsPage)
        {
            InitializeComponent();

            _navState        = navState;
            _vm              = mainViewModel;
            _schedulePage    = schedulePage;
            _rankingsPage    = rankingsPage;
            _SettingsPage    = SettingsPage;
            _projectionsPage = projectionsPage;

            BindingContext = _vm;

            // Build tab items
            _vm.TabItems.Clear();
            var labels = new[] { "Scores", "Rankings", "Projections", "Settings" };
            for (int i = 0; i < labels.Length; i++)
                _vm.TabItems.Add(new TabItem { Label = labels[i], Index = i, IsSelected = i == 0 });

            // Add pages to AbsoluteLayout — each fills the entire host
            AddPageToHost(_schedulePage);
            AddPageToHost(_rankingsPage);
            AddPageToHost(_projectionsPage);
            AddPageToHost(_SettingsPage);

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
                if (e.PropertyName == nameof(MainViewModel.IsLoading))
                    UpdateLoadingAnimation(_vm.IsLoading);
            };

            // Year change — reset all pages so they reload fresh on next visit
            _navState.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SharedNavigationStateService.SelectedYear))
                {
                    ResetAllPages();
                    SyncPage(_vm.SelectedIndex);
                }
            };

            // Show initial page
            SyncPage(0);

            // Wire loading state after pages are initialized
            WireLoadingState(_schedulePage);
            WireLoadingState(_rankingsPage);
            WireLoadingState(_projectionsPage);

            // Trigger initial data load
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(200);
                if (_schedulePage.BindingContext is ScheduleViewModel svm && !svm.HasLoaded)
                    await svm.LoadDataAsync();
            });
        }

        // ── Loading state wiring ──────────────────────────────────────────

        private readonly HashSet<INotifyPropertyChanged> _wiredViewModels = new();

        private void ResetAllPages()
        {
            if (_schedulePage.BindingContext   is ScheduleViewModel      svm) svm.HasLoaded = false;
            if (_rankingsPage.BindingContext    is PowerRankingsViewModel rvm) rvm.HasLoaded = false;
            if (_projectionsPage.BindingContext is ProjectionsViewModel   pvm) pvm.HasLoaded = false;
        }

        private void WireLoadingState(ContentPage page)
        {
            if (page.BindingContext is not INotifyPropertyChanged npc) return;
            if (!_wiredViewModels.Add(npc)) return; // already subscribed

            npc.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != "IsLoading" && e.PropertyName != "IsBusy") return;

                var anyLoading = IsAnyPageLoading();
                if (!anyLoading && !_vm.IsLoading) return;

                System.Diagnostics.Debug.WriteLine(
                    $"[Loading] {s.GetType().Name}.{e.PropertyName} fired — anyLoading={anyLoading}");

                if (anyLoading)
                {
                    _vm.IsLoading = true;
                    if (_loadingAnimCts == null)
                        MainThread.BeginInvokeOnMainThread(() => StartLoadingAnimation());
                }
                else
                {
                    _vm.IsLoading = false;
                    StopLoadingAnimation();
                }
            };
        }

        private bool IsAnyPageLoading()
        {
            if (_schedulePage.BindingContext   is ScheduleViewModel      svm && svm.IsLoading) return true;
            if (_rankingsPage.BindingContext    is PowerRankingsViewModel rvm && rvm.IsBusy)    return true;
            if (_projectionsPage.BindingContext is ProjectionsViewModel   pvm && pvm.IsLoading) return true;
            return false;
        }

        // ── Loading animation ─────────────────────────────────────────────

        private void UpdateLoadingAnimation(bool isLoading)
        {
            if (isLoading)
                StartLoadingAnimation();
            else
                StopLoadingAnimation();
        }

        private void StartLoadingAnimation()
        {
            if (_loadingAnimCts != null) return;
            _loadingAnimCts = new CancellationTokenSource();
            var token = _loadingAnimCts.Token;

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    // Wait for layout if needed
                    if (LoadingBarHost.Width <= 0)
                        await Task.Delay(100);

                    while (!token.IsCancellationRequested)
                    {
                        var hostWidth = LoadingBarHost.Width;
                        var barWidth  = LoadingBar.Width > 0 ? LoadingBar.Width : 80;
                        var travel    = Math.Max(50, hostWidth - barWidth);

                        await LoadingBar.TranslateTo(travel, 0, 600, Easing.CubicInOut);
                        if (token.IsCancellationRequested) break;
                        await LoadingBar.TranslateTo(0, 0, 600, Easing.CubicInOut);
                    }
                }
                finally
                {
                    LoadingBar.TranslationX = 0;
                }
            });
        }

        private void StopLoadingAnimation()
        {
            _loadingAnimCts?.Cancel();
            _loadingAnimCts?.Dispose();
            _loadingAnimCts = null;
            // Abort any in-progress animation immediately
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Microsoft.Maui.Controls.ViewExtensions.CancelAnimations(LoadingBar);
                LoadingBar.TranslationX = 0;
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

                Console.WriteLine($"[SyncPage] index={index}, scheduleBC={_schedulePage.BindingContext?.GetType().Name}");

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
                    case 2 when _projectionsPage.BindingContext is ProjectionsViewModel pvm && !pvm.HasLoaded:
                        await pvm.LoadDataAsync(); break;
                    case 3 when _SettingsPage.BindingContext is SettingsViewModel fvm && !fvm.HasLoaded:
                        await fvm.LoadDataAsync(); break;
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
