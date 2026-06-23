using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MDRedactor.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private string _currentFileTitle = string.Empty;
    private bool _hasUnsavedChanges;
    private string _statusText = string.Empty;

    public MainWindowViewModel(Func<Task> openAsync, Func<Task> saveAsync)
    {
        OpenCommand = new AsyncRelayCommand(openAsync);
        SaveCommand = new AsyncRelayCommand(saveAsync);
    }

    public IAsyncRelayCommand OpenCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public string CurrentFileTitle
    {
        get => _currentFileTitle;
        set => SetProperty(ref _currentFileTitle, value);
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        set => SetProperty(ref _hasUnsavedChanges, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }
}
