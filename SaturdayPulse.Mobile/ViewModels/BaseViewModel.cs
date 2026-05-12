using SaturdayPulse.Models;
using SaturdayPulse.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace SaturdayPulse.ViewModels
{
    public abstract class BaseViewModel : INotifyPropertyChanged
    {
        protected readonly FollowService _followService;

        public ICommand ToggleFollowCommand { get; }

        protected BaseViewModel(FollowService followService)
        {
            _followService = followService;
            ToggleFollowCommand = new Command<object>(param =>
            {
                var id = param switch
                {
                    int i => i,
                    string s when int.TryParse(s, out var parsed) => parsed,
                    TeamRanking tr => tr.TeamID,
                    TeamInfo ti => ti.TeamID,
                    _ => 0
                };
                if (id > 0) _followService.Toggle(id);
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}