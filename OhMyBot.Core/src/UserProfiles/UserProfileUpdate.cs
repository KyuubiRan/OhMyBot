using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.UserProfiles;

public sealed record UserProfileUpdate(
    BotPlatform Platform,
    string Uid,
    string? Username,
    string? FirstName,
    string? LastName,
    string? Nickname)
{
    public static UserProfileUpdate FromRequest(CommandRequest request)
    {
        return new UserProfileUpdate(
            request.Platform,
            request.UserId,
            Normalize(request.Username),
            Normalize(request.FirstName),
            Normalize(request.LastName),
            Normalize(request.Nickname));
    }

    public static UserProfileUpdate FromRequest(UserProfileRequest request)
    {
        return new UserProfileUpdate(
            request.Platform,
            request.Uid,
            Normalize(request.Username),
            Normalize(request.FirstName),
            Normalize(request.LastName),
            Normalize(request.Nickname));
    }

    public bool HasSameProfile(UserProfileUpdate other)
    {
        return Platform == other.Platform
            && string.Equals(Uid, other.Uid, StringComparison.Ordinal)
            && string.Equals(Username, other.Username, StringComparison.Ordinal)
            && string.Equals(FirstName, other.FirstName, StringComparison.Ordinal)
            && string.Equals(LastName, other.LastName, StringComparison.Ordinal)
            && string.Equals(Nickname, other.Nickname, StringComparison.Ordinal);
    }

    private static string? Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
