using System;
using System.Security.Principal;
using Meziantou.Framework.Win32;

namespace BetterGenshinImpact.Helpers.Win32;

/// <summary>
/// Stores the optional Windows credential used by the local RDP Child Session.
/// The secret is kept in Windows Credential Manager and is never written to the
/// project configuration or workflow logs.
/// </summary>
public static class ChildSessionCredentialStore
{
    public const string TargetName = "BetterGenshinImpact.ChildSession.WindowsLogon";

    private const string Comment = "BetterGI RDP Child Session Windows logon credential";

    public static bool HasPassword()
    {
        return ReadPassword() is { Length: > 0 };
    }

    public static string? ReadPassword()
    {
        var credential = CredentialManagerHelper.ReadCredential(TargetName);
        return string.IsNullOrEmpty(credential?.Password) ? null : credential.Password;
    }

    public static void SavePassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        CredentialManagerHelper.SaveCredential(
            TargetName,
            WindowsIdentity.GetCurrent().Name,
            password,
            Comment,
            CredentialPersistence.LocalMachine);
    }

    public static void Delete()
    {
        CredentialManagerHelper.DeleteCredential(TargetName);
    }
}
