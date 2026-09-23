namespace NeonSidekick.Sql;

/// <summary>
/// The password a connection signs in with (later on 2026-09-23, the user's call: every SQL password kept safe, the
/// store chosen per connection): nothing for <c>windows</c>; under <c>passwordStore: credman</c> the Windows
/// Credential Manager entry <see cref="SqlConnectionConfig.CredentialTarget"/>; under <c>file</c> (the default) the
/// <c>password</c> of <c>sql.json</c> decrypted — or, while it is still plain text (a file the app could not rewrite),
/// as it stands. A missing or undecryptable password is the sentence that names the fix, never a throw.
/// </summary>
public static class SqlSecrets
{
    public static CredentialResult Resolve(SqlNamedConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var config = connection.Config;
        if (!config.NeedsPassword)
        {
            return CredentialResult.Ok("");
        }

        if (config.InCredentialManager)
        {
            return WindowsCredentials.ReadGeneric(config.CredentialTarget(connection.Name));
        }

        string password = config.Password ?? "";
        if (password.Length == 0)
        {
            return CredentialResult.Failed(SqlText.NoPassword(connection.Name));
        }

        return WindowsCredentials.IsProtected(password) ? WindowsCredentials.Unprotect(password) : CredentialResult.Ok(password);
    }

    /// <summary>
    /// Saves <paramref name="password"/> for <paramref name="connection"/> in its store (the SQL tab's masked prompt,
    /// later on 2026-09-23): under <c>credman</c> the Credential Manager entry, written with the connection's account;
    /// under <c>file</c> DPAPI-encrypted into the <c>sql.json</c> the connection came from. The status line's sentence either way.
    /// </summary>
    public static (bool Saved, string Notice) Save(SqlNamedConnection connection, string password)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(password);
        var config = connection.Config;
        if (config.InCredentialManager)
        {
            string target = config.CredentialTarget(connection.Name);
            var written = WindowsCredentials.WriteGeneric(target, config.User?.Trim() ?? "", password);
            return written.Error is { } refused ? (false, SqlText.PasswordSaveFailed(connection.Name, refused)) : (true, SqlText.PasswordSavedToCredman(connection.Name, target));
        }

        var encrypted = WindowsCredentials.Protect(password);
        if (encrypted.Error is { } failed)
        {
            return (false, SqlText.PasswordSaveFailed(connection.Name, failed));
        }

        return SqlConfigFile.WritePassword(connection.Source, connection.Name, encrypted.Value!) is { } error
            ? (false, SqlText.PasswordSaveFailed(connection.Name, error))
            : (true, SqlText.PasswordSavedToFile(connection.Name, connection.Source));
    }
}
