using System.Runtime.CompilerServices;

namespace SaturdayPulse.Controls
{
    /// <summary>
    /// A ContentView that defers instantiation of its content until
    /// IsExpanded becomes true for the first time.
    ///
    /// Usage in XAML:
    ///
    ///   &lt;controls:LazyContentView IsExpanded="{Binding IsTrendExpanded}"&gt;
    ///     &lt;controls:LazyContentView.LazyTemplate&gt;
    ///       &lt;DataTemplate&gt;
    ///         &lt;chart:SfCartesianChart ... /&gt;
    ///       &lt;/DataTemplate&gt;
    ///     &lt;/controls:LazyContentView.LazyTemplate&gt;
    ///   &lt;/controls:LazyContentView&gt;
    ///
    /// The DataTemplate content is only created once — on first expand.
    /// Subsequent collapse/expand cycles toggle IsVisible only.
    /// BindingContext is inherited from the parent automatically.
    /// </summary>
    public class LazyContentView : ContentView
    {
        private bool _inflated = false;

        // ── IsExpanded ────────────────────────────────────────────────────

        public static readonly BindableProperty IsExpandedProperty =
            BindableProperty.Create(
                nameof(IsExpanded),
                typeof(bool),
                typeof(LazyContentView),
                defaultValue: false,
                propertyChanged: OnIsExpandedChanged);

        public bool IsExpanded
        {
            get => (bool)GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        private static void OnIsExpandedChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is LazyContentView lv)
                lv.ApplyExpanded((bool)newValue);
        }

        // ── LazyTemplate ──────────────────────────────────────────────────

        public static readonly BindableProperty LazyTemplateProperty =
            BindableProperty.Create(
                nameof(LazyTemplate),
                typeof(DataTemplate),
                typeof(LazyContentView),
                defaultValue: null);

        public DataTemplate? LazyTemplate
        {
            get => (DataTemplate?)GetValue(LazyTemplateProperty);
            set => SetValue(LazyTemplateProperty, value);
        }

        // ── Core logic ────────────────────────────────────────────────────

        private void ApplyExpanded(bool expanded)
        {
            if (expanded && !_inflated)
            {
                // First expand — inflate the template
                if (LazyTemplate?.CreateContent() is View view)
                {
                    // Inherit binding context from parent row
                    view.BindingContext = BindingContext;
                    Content   = view;
                    _inflated = true;
                }
            }

            // Toggle visibility — never destroy once inflated
            IsVisible = expanded && _inflated;
        }

        // ── BindingContext propagation ─────────────────────────────────────
        // When CollectionView rebinds the row (e.g. after sort/filter),
        // push the new context into the already-inflated content.

        protected override void OnBindingContextChanged()
        {
            base.OnBindingContextChanged();
            if (_inflated && Content != null)
                Content.BindingContext = BindingContext;
        }
    }
}
