using GameTranslator.Domain.Profiles;

namespace GameTranslator.UI.ViewModels;

public sealed class OcrZoneEditorViewModel : ValidatableObservableObject
{
    public const int ReferenceSurfaceWidth = 1920;
    public const int ReferenceSurfaceHeight = 1080;
    public const double PreviewSurfaceWidth = 640;
    public const double PreviewSurfaceHeight = 360;
    private const double RelativeBoundsTolerance = 0.000001;

    private readonly List<string> absoluteOverlapErrors = new();

    private string id = Guid.NewGuid().ToString("N");
    private string name = string.Empty;
    private bool isSelected;
    private int absoluteX;
    private int absoluteY;
    private int absoluteWidth = 100;
    private int absoluteHeight = 40;
    private double relativeX;
    private double relativeY;
    private double relativeWidth = 0.25;
    private double relativeHeight = 0.05;
    private string overlayFontFamily = OcrZoneTextStyle.DefaultFontFamily;
    private double overlayFontSize = OcrZoneTextStyle.DefaultFontSize;
    private bool overlayIsBold = true;
    private bool overlayIsItalic;
    private bool overlayCanExpandBeyondSource;

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
                NotifyDerivedPropertiesChanged();
                Validate();
            }
        }
    }

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    public int AbsoluteX
    {
        get => absoluteX;
        set
        {
            if (SetProperty(ref absoluteX, value))
            {
                NotifyDerivedPropertiesChanged();
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
                NotifyDerivedPropertiesChanged();
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
                NotifyDerivedPropertiesChanged();
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
                NotifyDerivedPropertiesChanged();
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
                NotifyDerivedPropertiesChanged();
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
                NotifyDerivedPropertiesChanged();
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
                NotifyDerivedPropertiesChanged();
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
                NotifyDerivedPropertiesChanged();
                Validate();
            }
        }
    }

    public string OverlayFontFamily
    {
        get => overlayFontFamily;
        set
        {
            if (SetProperty(ref overlayFontFamily, value))
            {
                NotifyDerivedPropertiesChanged();
                Validate();
            }
        }
    }

    public double OverlayFontSize
    {
        get => overlayFontSize;
        set
        {
            if (SetProperty(ref overlayFontSize, value))
            {
                NotifyDerivedPropertiesChanged();
                Validate();
            }
        }
    }

    public bool OverlayIsBold
    {
        get => overlayIsBold;
        set
        {
            if (SetProperty(ref overlayIsBold, value))
            {
                NotifyDerivedPropertiesChanged();
            }
        }
    }

    public bool OverlayIsItalic
    {
        get => overlayIsItalic;
        set
        {
            if (SetProperty(ref overlayIsItalic, value))
            {
                NotifyDerivedPropertiesChanged();
            }
        }
    }

    public bool OverlayCanExpandBeyondSource
    {
        get => overlayCanExpandBeyondSource;
        set
        {
            if (SetProperty(ref overlayCanExpandBeyondSource, value))
            {
                NotifyDerivedPropertiesChanged();
            }
        }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Unnamed zone" : Name;

    public string AbsoluteBoundsSummary => $"X {AbsoluteX}  Y {AbsoluteY}  W {AbsoluteWidth}  H {AbsoluteHeight}";

    public string RelativeBoundsSummary => $"X {RelativeX:0.###}  Y {RelativeY:0.###}  W {RelativeWidth:0.###}  H {RelativeHeight:0.###}";

    public int AbsoluteArea => AbsoluteWidth > 0 && AbsoluteHeight > 0
        ? AbsoluteWidth * AbsoluteHeight
        : 0;

    public double RelativeAreaPercent => RelativeWidth > 0 && RelativeHeight > 0
        ? Math.Round(RelativeWidth * RelativeHeight * 100, 2)
        : 0;

    public double SurfaceX => Math.Round(AbsoluteX * PreviewSurfaceWidth / ReferenceSurfaceWidth, 2);

    public double SurfaceY => Math.Round(AbsoluteY * PreviewSurfaceHeight / ReferenceSurfaceHeight, 2);

    public double SurfaceWidth => Math.Round(AbsoluteWidth * PreviewSurfaceWidth / ReferenceSurfaceWidth, 2);

    public double SurfaceHeight => Math.Round(AbsoluteHeight * PreviewSurfaceHeight / ReferenceSurfaceHeight, 2);

    public double SurfaceHandleX => Math.Max(0, SurfaceWidth - 10);

    public double SurfaceHandleY => Math.Max(0, SurfaceHeight - 10);

    public string OverlayTextStyleSummary =>
        $"{OverlayFontFamily} {OverlayFontSize:0.#}"
        + (OverlayIsBold ? " bold" : string.Empty)
        + (OverlayIsItalic ? " italic" : string.Empty)
        + (OverlayCanExpandBeyondSource ? " expand" : " fit");

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
            OverlayFontFamily = string.IsNullOrWhiteSpace(zone.TextStyle?.FontFamily)
                ? OcrZoneTextStyle.DefaultFontFamily
                : zone.TextStyle.FontFamily,
            OverlayFontSize = zone.TextStyle?.FontSize ?? OcrZoneTextStyle.DefaultFontSize,
            OverlayIsBold = zone.TextStyle?.IsBold ?? true,
            OverlayIsItalic = zone.TextStyle?.IsItalic ?? false,
            OverlayCanExpandBeyondSource = zone.TextStyle?.LayoutMode == OverlayTextLayoutMode.ExpandFromSourceCenter,
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
            TextStyle = new OcrZoneTextStyle
            {
                FontFamily = string.IsNullOrWhiteSpace(OverlayFontFamily)
                    ? OcrZoneTextStyle.DefaultFontFamily
                    : OverlayFontFamily.Trim(),
                FontSize = OverlayFontSize,
                IsBold = OverlayIsBold,
                IsItalic = OverlayIsItalic,
                LayoutMode = OverlayCanExpandBeyondSource
                    ? OverlayTextLayoutMode.ExpandFromSourceCenter
                    : OverlayTextLayoutMode.FitToSourceBounds,
            },
        };
    }

    public OcrZoneEditorViewModel CreateDuplicate(string name)
    {
        return new OcrZoneEditorViewModel
        {
            Name = name,
            AbsoluteX = AbsoluteX,
            AbsoluteY = AbsoluteY,
            AbsoluteWidth = AbsoluteWidth,
            AbsoluteHeight = AbsoluteHeight,
            RelativeX = RelativeX,
            RelativeY = RelativeY,
            RelativeWidth = RelativeWidth,
            RelativeHeight = RelativeHeight,
            OverlayFontFamily = OverlayFontFamily,
            OverlayFontSize = OverlayFontSize,
            OverlayIsBold = OverlayIsBold,
            OverlayIsItalic = OverlayIsItalic,
            OverlayCanExpandBeyondSource = OverlayCanExpandBeyondSource,
        };
    }

    private void Validate()
    {
        SetErrors(
            nameof(Name),
            string.IsNullOrWhiteSpace(Name)
                ? new[] { "Zone name is required." }
                : Array.Empty<string>());
        SetErrors(
            nameof(OverlayFontFamily),
            string.IsNullOrWhiteSpace(OverlayFontFamily)
                ? new[] { "Overlay font family is required." }
                : Array.Empty<string>());
        SetErrors(
            nameof(OverlayFontSize),
            OverlayFontSize is < OcrZoneTextStyle.MinimumFontSize or > OcrZoneTextStyle.MaximumFontSize
                ? new[] { $"Overlay font size must be between {OcrZoneTextStyle.MinimumFontSize:0} and {OcrZoneTextStyle.MaximumFontSize:0}." }
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

        if (RelativeX + RelativeWidth > 1 + RelativeBoundsTolerance || RelativeY + RelativeHeight > 1 + RelativeBoundsTolerance)
        {
            relativePositionErrors.Add("Relative bounds must fit within 0..1.");
        }

        SetErrors(nameof(RelativeX), relativePositionErrors);
        SetErrors(nameof(RelativeY), relativePositionErrors);
        SetErrors(nameof(RelativeWidth), relativeSizeErrors.Concat(relativePositionErrors));
        SetErrors(nameof(RelativeHeight), relativeSizeErrors.Concat(relativePositionErrors));
    }

    private void NotifyDerivedPropertiesChanged()
    {
        OnPropertyChanged(nameof(AbsoluteBoundsSummary));
        OnPropertyChanged(nameof(RelativeBoundsSummary));
        OnPropertyChanged(nameof(AbsoluteArea));
        OnPropertyChanged(nameof(RelativeAreaPercent));
        OnPropertyChanged(nameof(SurfaceX));
        OnPropertyChanged(nameof(SurfaceY));
        OnPropertyChanged(nameof(SurfaceWidth));
        OnPropertyChanged(nameof(SurfaceHeight));
        OnPropertyChanged(nameof(SurfaceHandleX));
        OnPropertyChanged(nameof(SurfaceHandleY));
        OnPropertyChanged(nameof(OverlayTextStyleSummary));
    }
}
