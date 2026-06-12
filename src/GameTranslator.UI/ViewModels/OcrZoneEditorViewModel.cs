using GameTranslator.Domain.Profiles;

namespace GameTranslator.UI.ViewModels;

public sealed class OcrZoneEditorViewModel : ObservableObject
{
    private string id = Guid.NewGuid().ToString("N");
    private string name = string.Empty;
    private int absoluteX;
    private int absoluteY;
    private int absoluteWidth = 100;
    private int absoluteHeight = 40;
    private double relativeX;
    private double relativeY;
    private double relativeWidth = 0.25;
    private double relativeHeight = 0.05;

    public string Id
    {
        get => id;
        set => SetProperty(ref id, value);
    }

    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public int AbsoluteX
    {
        get => absoluteX;
        set => SetProperty(ref absoluteX, value);
    }

    public int AbsoluteY
    {
        get => absoluteY;
        set => SetProperty(ref absoluteY, value);
    }

    public int AbsoluteWidth
    {
        get => absoluteWidth;
        set => SetProperty(ref absoluteWidth, value);
    }

    public int AbsoluteHeight
    {
        get => absoluteHeight;
        set => SetProperty(ref absoluteHeight, value);
    }

    public double RelativeX
    {
        get => relativeX;
        set => SetProperty(ref relativeX, value);
    }

    public double RelativeY
    {
        get => relativeY;
        set => SetProperty(ref relativeY, value);
    }

    public double RelativeWidth
    {
        get => relativeWidth;
        set => SetProperty(ref relativeWidth, value);
    }

    public double RelativeHeight
    {
        get => relativeHeight;
        set => SetProperty(ref relativeHeight, value);
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Unnamed zone" : Name;

    public static OcrZoneEditorViewModel CreateDefault(int index)
    {
        return new OcrZoneEditorViewModel
        {
            Name = $"Zone {index}",
        };
    }

    public static OcrZoneEditorViewModel FromModel(OcrZone zone)
    {
        return new OcrZoneEditorViewModel
        {
            Id = zone.Id,
            Name = zone.Name,
            AbsoluteX = zone.AbsoluteBounds.X,
            AbsoluteY = zone.AbsoluteBounds.Y,
            AbsoluteWidth = zone.AbsoluteBounds.Width,
            AbsoluteHeight = zone.AbsoluteBounds.Height,
            RelativeX = zone.RelativeBounds.X,
            RelativeY = zone.RelativeBounds.Y,
            RelativeWidth = zone.RelativeBounds.Width,
            RelativeHeight = zone.RelativeBounds.Height,
        };
    }

    public OcrZone ToModel()
    {
        return new OcrZone
        {
            Id = Id,
            Name = Name.Trim(),
            AbsoluteBounds = new AbsoluteRectangle(AbsoluteX, AbsoluteY, AbsoluteWidth, AbsoluteHeight),
            RelativeBounds = new RelativeRectangle(RelativeX, RelativeY, RelativeWidth, RelativeHeight),
        };
    }
}
