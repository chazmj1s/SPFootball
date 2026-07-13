using Microsoft.Maui.Layouts;
using SaturdayPulse.Services;
using SaturdayPulse.ViewModels;
using System.ComponentModel;

namespace SaturdayPulse.Views
{
    public partial class MainPage : ContentPage
    {
        private readonly MainViewModel                _vm;
        private readonly MyTeamsPage                  _myTeamsPage;
        private readonly SchedulePage                 _schedulePage;
        private readonly PowerRankingsPage            _rankingsPage;
        private readonly SettingsPage                 _SettingsPage;
        private readonly PostseasonPage               _postseasonPage;
        private readonly SandboxPage                  _sandboxPage;
        private readonly SharedNavigationStateService _navState;

        private CancellationTokenSource? _loadingAnimCts;

        public MainPage(
            SharedNavigationStateService navState,
            MainViewModel mainViewModel,
            MyTeamsPage myTeamsPage,
            SchedulePage schedulePage,
            PowerRankingsPage rankingsPage,
            SettingsPage SettingsPage,
            PostseasonPage postseasonPage,
            SandboxPage sandboxPage)
        {
            InitializeComponent();

            _navState        = navState;
            _vm              = mainViewModel;
            _myTeamsPage     = myTeamsPage;
            _schedulePage    = schedulePage;
            _rankingsPage    = rankingsPage;
            _SettingsPage    = SettingsPage;
            _postseasonPage  = postseasonPage;
            _sandboxPage     = sandboxPage;

            BindingContext = _vm;

            // Build tab items — My Teams is tab 0 (default landing page).
            // Every other index shifted +1 from before.
            _vm.TabItems.Clear();
            var labels = new[] { "My Teams", "Scores", "Rankings", "Postseason", "Sandbox", "Settings" };
            var initialIndex = GetInitialTabIndex();
            for (int i = 0; i < labels.Length; i++)
                _vm.TabItems.Add(new TabItem { Label = labels[i], Index = i, IsSelected = i == initialIndex });

            // Add pages to AbsoluteLayout — order matches labels[] above,
            // and MUST match tab index order (SyncPage indexes PageHost.Children
            // positionally).
            AddPageToHost(_myTeamsPage);     // 0 — My Teams
            AddPageToHost(_schedulePage);    // 1 — Scores
            AddPageToHost(_rankingsPage);    // 2 — Rankings
            AddPageToHost(_postseasonPage);  // 3 — Postseason
            AddPageToHost(_sandboxPage);     // 4 — Sandbox
            AddPageToHost(_SettingsPage);    // 5 — Settings

            // Wire loading state FIRST before any load fires
            WireLoadingState(_myTeamsPage);
            WireLoadingState(_schedulePage);
            WireLoadingState(_rankingsPage);
            WireLoadingState(_postseasonPage);

            // Forward nav state changes
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

            _navState.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SharedNavigationStateService.SelectedYear))
                {
                    ResetAllPages();
                    //SyncPage(_vm.SelectedIndex);
                }
            };

            // Show initial page — visibility only, no load
            for (int i = 0; i < PageHost.Count; i++)
                if (PageHost.Children[i] is VisualElement ve)
                    ve.IsVisible = i == initialIndex;

            // Keep SelectedIndex consistent with the visible tab (without
            // triggering the PropertyChanged-driven SyncPage above — that
            // path is for user-driven tab switches; at startup we drive
            // SyncTabItems/SyncPage explicitly, below, once).
            _vm.SetInitialTabIndex(initialIndex);
            SyncTabItems(initialIndex);
            SyncPage(initialIndex);

            // MainView owns year/week/conference setup. InitializeAsync warms the
            // cache, builds the week strip, resolves the default week + conference,
            // then fires FilterChanged so the currently-active page (whichever tab
            // GetInitialTabIndex resolved to — My Teams by default) renders.
            // Runs on the MAIN THREAD (no Task.Run) so the nav-state continuation —
            // including ApplyStartupDefaults' property notifications — stays on it.
            // The awaited async cache fetch frees the main thread during I/O.
            _ = _vm.InitializeAsync();
        }

        // ── Loading state wiring ──────────────────────────────────────────

        private readonly HashSet<INotifyPropertyChanged> _wiredViewModels = new();

        private void ResetAllPages()
        {
            if (_myTeamsPage.BindingContext  is MyTeamsViewModel      mvm) mvm.HasLoaded = false;
            if (_schedulePage.BindingContext   is ScheduleViewModel      svm) svm.HasLoaded = false;
            if (_rankingsPage.BindingContext    is PowerRankingsViewModel rvm) rvm.HasLoaded = false;
            if (_postseasonPage.BindingContext is PostseasonViewModel    pvm) pvm.HasLoaded = false;
        }

        private void WireLoadingState(ContentPage page)
        {
            if (page.BindingContext is not INotifyPropertyChanged npc) return;
            if (!_wiredViewModels.Add(npc)) return;

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
            if (_myTeamsPage.BindingContext  is MyTeamsViewModel      mvm && mvm.IsBusy)   return true;
            if (_schedulePage.BindingContext   is ScheduleViewModel      svm && svm.IsLoading) return true;
            if (_rankingsPage.BindingContext    is PowerRankingsViewModel rvm && rvm.IsBusy)    return true;
            if (_postseasonPage.BindingContext is PostseasonViewModel    pvm && pvm.IsLoading) return true;
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
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Microsoft.Maui.Controls.ViewExtensions.CancelAnimations(LoadingBar);
                LoadingBar.TranslationX = 0;
            });
        }

        // ── Page host helpers ─────────────────────────────────────────────

        private const string DefaultLandingPageKey = "DefaultLandingPage";

        /// <summary>
        /// Reads the "Default landing page" preference set from Settings.
        /// Defaults to My Teams (tab 0) if unset — matches My Teams being
        /// the new default landing page per the feature spec. Index mapping
        /// MUST match the labels[]/AddPageToHost order above.
        /// </summary>
        private int GetInitialTabIndex()
        {
            var stored = Preferences.Default.Get(DefaultLandingPageKey, "MyTeams");
            return stored switch
            {
                "MyTeams"    => 0,
                "Scores"     => 1,
                "Rankings"   => 2,
                "Postseason" => 3,
                "Sandbox"    => 4,
                "Settings"   => 5,
                _            => 0
            };
        }

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
                System.Diagnostics.Debug.WriteLine($"[SyncPage] index={index}");

                for (int i = 0; i < PageHost.Count; i++)
                    if (PageHost.Children[i] is VisualElement ve)
                        ve.IsVisible = i == index;

                // Mark which page is active so off-screen pages defer FilterChanged
                // work instead of loading/rendering behind another tab.
                if (_myTeamsPage.BindingContext is MyTeamsViewModel mActive)
                    mActive.IsActive = index == 0;
                if (_rankingsPage.BindingContext is PowerRankingsViewModel rActive)
                    rActive.IsActive = index == 2;
                if (_postseasonPage.BindingContext is PostseasonViewModel pActive)
                    pActive.IsActive = index == 3;

                // Lazy load on first visit.
                switch (index)
                {
                    // All content VMs are main-thread-safe (LoadDataAsync/InitializeAsync
                    // await async I/O, continuation stays on main). No Task.Run — that
                    // would push their UI mutations onto a background thread.
                    case 0 when _myTeamsPage.BindingContext is MyTeamsViewModel mvm && !mvm.HasLoaded:
                        _ = mvm.InitializeAsync(); break;
                    case 1 when _schedulePage.BindingContext is ScheduleViewModel svm && !svm.HasLoaded:
                        _ = svm.LoadDataAsync(); break;
                    case 2 when _rankingsPage.BindingContext is PowerRankingsViewModel rvm && !rvm.HasLoaded:
                        _ = rvm.LoadDataAsync(); break;
                    case 3 when _postseasonPage.BindingContext is PostseasonViewModel pvm && !pvm.HasLoaded:
                        _ = pvm.LoadDataAsync(); break;
                    case 5 when _SettingsPage.BindingContext is SettingsViewModel fvm && !fvm.HasLoaded:
                        _ = fvm.LoadDataAsync(); break;
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
