namespace PortableDictionary.Core;

public sealed class NavigationHistory<T>
{
    private readonly List<T> _items = new();
    private int _currentIndex = -1;

    public bool CanGoBack => _currentIndex > 0;
    public T? Current => _currentIndex >= 0 ? _items[_currentIndex] : default;
    public int Count => _items.Count;

    public void Navigate(T view)
    {
        if (_currentIndex + 1 < _items.Count)
        {
            _items.RemoveRange(_currentIndex + 1, _items.Count - _currentIndex - 1);
        }
        _items.Add(view);
        _currentIndex = _items.Count - 1;
    }

    public T? GoBack()
    {
        if (!CanGoBack)
        {
            return default;
        }
        _currentIndex--;
        return _items[_currentIndex];
    }
}
