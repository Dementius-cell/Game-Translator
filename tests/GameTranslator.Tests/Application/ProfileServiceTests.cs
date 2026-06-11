using GameTranslator.Application.Profiles;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class ProfileServiceTests
{
    private readonly InMemoryProfileRepository repository = new();
    private readonly ProfileService service;

    public ProfileServiceTests()
    {
        service = new ProfileService(repository, new ProfileValidator());
    }

    [Fact]
    public async Task CreateAsync_WhenProfileIsValid_SavesAndReturnsProfile()
    {
        var profile = CreateProfile("Mass Effect");

        var created = await service.CreateAsync(profile);

        Assert.Equal(profile.Id, created.Id);
        Assert.Equal(profile.Name, created.Name);
        Assert.Same(profile, await repository.GetByIdAsync(profile.Id));
    }

    [Fact]
    public async Task CreateAsync_WhenProfileIsInvalid_DoesNotSaveAndThrowsValidationException()
    {
        var profile = CreateProfile("Broken") with
        {
            OcrZones = new[]
            {
                CreateZone("first", new AbsoluteRectangle(0, 0, 100, 100)),
                CreateZone("second", new AbsoluteRectangle(50, 50, 100, 100)),
            },
        };

        var exception = await Assert.ThrowsAsync<ProfileValidationException>(
            () => service.CreateAsync(profile));

        Assert.Contains(
            exception.Errors,
            error => error.Code == ProfileValidationErrorCodes.OverlappingOcrZones);
        Assert.Empty(repository.SavedProfiles);
    }

    [Fact]
    public async Task UpdateAsync_WhenProfileExists_ReplacesStoredProfile()
    {
        var profile = CreateProfile("Original");
        await repository.SaveAsync(profile);

        var updated = profile with
        {
            Name = "Updated",
        };

        await service.UpdateAsync(updated);

        var stored = await repository.GetByIdAsync(profile.Id);
        Assert.Equal("Updated", stored?.Name);
    }

    [Fact]
    public async Task UpdateAsync_WhenProfileDoesNotExist_ThrowsProfileNotFoundException()
    {
        var profile = CreateProfile("Missing");

        await Assert.ThrowsAsync<ProfileNotFoundException>(
            () => service.UpdateAsync(profile));
    }

    [Fact]
    public async Task DeleteAsync_WhenProfileExists_DeletesProfileAndReturnsTrue()
    {
        var profile = CreateProfile("Delete me");
        await repository.SaveAsync(profile);

        var deleted = await service.DeleteAsync(profile.Id);

        Assert.True(deleted);
        Assert.Null(await repository.GetByIdAsync(profile.Id));
    }

    [Fact]
    public async Task DeleteAsync_WhenProfileDoesNotExist_ReturnsFalse()
    {
        var deleted = await service.DeleteAsync("missing");

        Assert.False(deleted);
    }

    [Fact]
    public async Task GetAndListAsync_ReturnStoredProfiles()
    {
        var first = CreateProfile("First");
        var second = CreateProfile("Second");
        await repository.SaveAsync(first);
        await repository.SaveAsync(second);

        var loaded = await service.GetAsync(first.Id);
        var profiles = await service.ListAsync();

        Assert.Equal(first.Id, loaded?.Id);
        Assert.Equal(new[] { "First", "Second" }, profiles.Select(profile => profile.Name).Order());
    }

    [Fact]
    public async Task CloneAsync_WhenProfileExists_CreatesCopyWithNewIdAndName()
    {
        var source = CreateProfile("Source");
        await repository.SaveAsync(source);

        var clone = await service.CloneAsync(source.Id, "Copy");

        Assert.NotEqual(source.Id, clone.Id);
        Assert.Equal("Copy", clone.Name);
        Assert.Equal(source.OcrZones.Count, clone.OcrZones.Count);
        Assert.NotNull(await repository.GetByIdAsync(clone.Id));
    }

    private static GameProfile CreateProfile(string name)
    {
        return new GameProfile
        {
            Name = name,
            OcrZones = new[]
            {
                CreateZone("subtitles", new AbsoluteRectangle(0, 0, 100, 100)),
            },
        };
    }

    private static OcrZone CreateZone(string name, AbsoluteRectangle bounds)
    {
        return new OcrZone
        {
            Name = name,
            AbsoluteBounds = bounds,
            RelativeBounds = new RelativeRectangle(0, 0, 0.5, 0.5),
        };
    }

    private sealed class InMemoryProfileRepository : IProfileRepository
    {
        private readonly Dictionary<string, GameProfile> profiles = new(StringComparer.Ordinal);

        public IReadOnlyList<GameProfile> SavedProfiles => profiles.Values.ToArray();

        public Task<IReadOnlyList<GameProfile>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<GameProfile>>(
                profiles.Values.OrderBy(profile => profile.Name, StringComparer.Ordinal).ToArray());
        }

        public Task<GameProfile?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            profiles.TryGetValue(id, out var profile);
            return Task.FromResult(profile);
        }

        public Task SaveAsync(GameProfile profile, CancellationToken cancellationToken = default)
        {
            profiles[profile.Id] = profile;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            profiles.Remove(id);
            return Task.CompletedTask;
        }
    }
}
