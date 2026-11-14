using PortableDictionary.Core;

namespace PortableDictionary.Services;

// Reading the guide is a temporary view, not a new entry in lookup history.
public sealed class LookupNavigation
{
    private readonly NavigationHistory<LookupResult> _history = new();

    public bool IsGuideVisible { get; private set; } = true;
    public bool CanGoBack => IsGuideVisible ? _history.Current is not null : _history.CanGoBack;

    public void Navigate(LookupResult result)
    {
        _history.Navigate(result);
        IsGuideVisible = false;
    }

    public void ShowGuide() => IsGuideVisible = true;

    public LookupResult? GoBack()
    {
        if (!IsGuideVisible) return _history.GoBack();
        var current = _history.Current;
        if (current is not null) IsGuideVisible = false;
        return current;
    }
}
