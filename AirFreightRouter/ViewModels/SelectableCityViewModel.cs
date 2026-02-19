using CommunityToolkit.Mvvm.ComponentModel;
using AirFreightRouter.Models;

namespace AirFreightRouter.ViewModels;

/// <summary>
/// Wraps a <see cref="City"/> with a mutable <see cref="IsSelected"/> flag
/// so that the city-selection <c>ListBox</c> can bind checkboxes to it
/// without polluting the model class.
/// </summary>
public partial class SelectableCityViewModel : ObservableObject
{
    /// <summary>Gets the underlying city this wrapper represents.</summary>
    public City City { get; }

    /// <summary>
    /// Gets or sets whether this city is currently checked for inclusion
    /// in the next route computation.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Initialises a new wrapper around <paramref name="city"/> with
    /// <see cref="IsSelected"/> defaulting to <see langword="false"/>.
    /// </summary>
    public SelectableCityViewModel(City city) => City = city;

    /// <summary>
    /// Gets a display string in the form returned by <see cref="City.ToString()"/>
    /// (e.g. <c>"Boston, MA"</c>).
    /// </summary>
    public string DisplayName => City.ToString();
}
