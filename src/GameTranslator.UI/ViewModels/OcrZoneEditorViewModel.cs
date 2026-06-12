using GameTranslator.Domain.Profiles;

namespace GameTranslator.UI.ViewModels;

public sealed class OcrZoneEditorViewModel : ValidatableObservableObject
{
    private readonly List<string> absoluteOverlapErrors = new();

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
                Validate();
            }
        }
    }

    public int AbsoluteX
    {
        get => absoluteX;
        set
        {
            if (SetProperty(ref absoluteX, value))
            {
                Validate();
            }
        }
    }

    public int AbsoluteY
    {
        get => absoluteY;
        set
        {
            if (SetProperty(ref absoluteY, value))
            {
                Validate();
            }
        }
    }

    public int AbsoluteWidth
    {
        get => absoluteWidth;
        set
        {
            if (SetProperty(ref absoluteWidth, value))
            {
                Validate();
            }
        }
    }

    public int AbsoluteHeight
    {
        get => absoluteHeight;
        set
        {
            if (SetProperty(ref absoluteHeight, value))
            {
                Validate();
            }
        }
    }

    public double RelativeX
    {
        get => relativeX;
        set
        {
            if (SetProperty(ref relativeX, value))
            {
                Validate();
            }
        }
    }

    public double RelativeY
    {
        get => relativeY;
        set
        {
            if (SetProperty(ref relativeY, value))
            {
                Validate();
            }
        }
    }

    public double RelativeWidth
    {
        get => relativeWidth;
        set
        {
            if (SetProperty(ref relativeWidth, value))
            {
                Validate();
            }
        }
    }

    public double RelativeHeight
    {
        get => relativeHeight;
        set
        {
            if (SetProperty(ref relativeHeight, value))
            {
                Validate();
            }
        }
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

    public void SetAbsoluteOverlapErrors(IEnumerable<string> errors)
    {
        absoluteOverlapErrors.Clear();
        absoluteOverlapErrors.AddRange(
            errors
                .Where(error => !string.IsNullOrWhiteSpace(error))
                .Select(error => error.Trim())
                .Distinct(StringComparer.Ordinal));

        Validate();
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

    private void Validate()
    {
        SetErrors(
            nameof(Name),
            string.IsNullOrWhiteSpace(Name)
                ? new[] { "Zone name is required." }
                : Array.Empty<string>());

        var absoluteErrors = new List<string>();
        if (AbsoluteWidth <= 0 || AbsoluteHeight <= 0)
        {
            absoluteErrors.Add("Absolute width and height must be positive.");
        }

        absoluteErrors.AddRange(absoluteOverlapErrors);
        SetErrors(nameof(AbsoluteX), absoluteErrors);
        SetErrors(nameof(AbsoluteY), absoluteErrors);
        SetErrors(nameof(AbsoluteWidth), absoluteErrors);
        SetErrors(nameof(AbsoluteHeight), absoluteErrors);

        var relativeSizeErrors = new List<string>();
        if (RelativeWidth <= 0 || RelativeHeight <= 0)
        {
            relativeSizeErrors.Add("Relative width and height must be positive.");
        }

        var relativePositionErrors = new List<string>();
        if (RelativeX < 0 || RelativeY < 0 || RelativeX >= 1 || RelativeY >= 1)
        {
            relativePositionErrors.Add("Relative X and Y must stay within 0..1.");
        }

        if (RelativeX + RelativeWidth > 1 || RelativeY + RelativeHeight > 1)
        {
            relativePositionErrors.Add("Relative bounds must fit within 0..1.");
        }

        SetErrors(nameof(RelativeX), relativePositionErrors);
        SetErrors(nameof(RelativeY), relativePositionErrors);
        SetErrors(nameof(RelativeWidth), relativeSizeErrors.Concat(relativePositionErrors));
        SetErrors(nameof(RelativeHeight), relativeSizeErrors.Concat(relativePositionErrors));
    }
}
