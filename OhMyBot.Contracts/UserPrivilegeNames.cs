using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Contracts;

public static class UserPrivilegeNames
{
    public const string SupportedValues = "user, verified-user, admin, owner";

    public static bool TryParse(string value, out UserPrivilege privilege)
    {
        var parsed = value.Trim().ToLowerInvariant() switch
        {
            "user" => UserPrivilege.User,
            "verified-user" or "verified_user" or "verifieduser" => UserPrivilege.VerifiedUser,
            "admin" => UserPrivilege.Admin,
            "owner" => UserPrivilege.Owner,
            _ => (UserPrivilege?)null
        };

        privilege = parsed ?? default;
        return parsed.HasValue;
    }

    public static string Format(UserPrivilege privilege)
    {
        return privilege switch
        {
            UserPrivilege.User => "user",
            UserPrivilege.VerifiedUser => "verified-user",
            UserPrivilege.Admin => "admin",
            UserPrivilege.Owner => "owner",
            _ => privilege.ToString().ToLowerInvariant()
        };
    }
}
