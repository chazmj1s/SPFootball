using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace SaturdayPulse.Helpers
{
    /// <summary>
    /// ObservableCollection that supports ReplaceRange — replaces all items
    /// and fires a single Reset notification instead of one per item.
    /// Prevents CollectionView from re-rendering on every individual Add/Remove.
    /// </summary>
    public class ObservableRangeCollection<T> : ObservableCollection<T>
    {
        public ObservableRangeCollection() { }
        public ObservableRangeCollection(IEnumerable<T> items) : base(items) { }

        public void ReplaceRange(IEnumerable<T> items)
        {
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Reset));
        }
    }
}
