namespace OhMyBot.Core.UserProfiles;

public sealed record UserProfileCacheEntry(
    UserProfileUpdate Profile,
    bool Persisted)
{
    public static UserProfileCacheEntry PersistedProfile(UserProfileUpdate profile)
    {
        return new UserProfileCacheEntry(profile, Persisted: true);
    }
}
