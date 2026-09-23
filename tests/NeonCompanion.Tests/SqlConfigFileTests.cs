using Microsoft.Data.SqlClient;
using NeonCompanion.Sql;

namespace NeonCompanion.Tests;

/// <summary><c>sql.json</c> (2026-09-23): the shape, the problems it reports, the profile over the home, and the connection string it builds.</summary>
public sealed class SqlConfigFileTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "NeonCompanion.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _profile;

    public SqlConfigFileTests()
    {
        _profile = Path.Combine(_home, "profiles", "default");
        Directory.CreateDirectory(_profile);
    }

    public void Dispose()
    {
        try { Directory.Delete(_home, recursive: true); } catch { /* best effort */ }
    }

    private void Profile(string json) => File.WriteAllText(SqlConfigFile.ProfilePath(_profile), json);

    private void Global(string json) => File.WriteAllText(SqlConfigFile.GlobalPath(_home), json);

    [Fact]
    public void AMissingFile_IsEmpty_AndTheEmptyShape_LoadsEmpty()
    {
        Assert.Same(SqlCatalog.Empty, SqlConfigFile.Load(SqlConfigFile.ProfilePath(_profile)));
        string path = SqlConfigFile.ProfilePath(_profile);
        Assert.True(SqlConfigFile.EnsureExists(path));
        Assert.False(SqlConfigFile.EnsureExists(path));
        Assert.Equal(SqlConfigFile.EmptyText, File.ReadAllText(path));
        var loaded = SqlConfigFile.Load(path);
        Assert.Empty(loaded.Connections);
        Assert.Empty(loaded.Problems);
    }

    /// <summary>The fresh file's commented examples (later on 2026-09-23): one of each kind, and each a usable connection once its <c>//</c> are gone.</summary>
    [Fact]
    public void TheEmptyShapesExamples_AreEachAUsableConnection_OnceUncommented()
    {
        var example = SqlConfigFile.EmptyText.Split('\n')
            .Where(l => l.StartsWith("  // ", StringComparison.Ordinal))
            .Select(l => l["  // ".Length..])
            .Where(l => l.EndsWith('{') || l.EndsWith(',') || l.EndsWith('}') || l.EndsWith('"') || l.EndsWith("true", StringComparison.Ordinal))
            .Where(l => l.StartsWith('"') || l.StartsWith("  ", StringComparison.Ordinal) || l.StartsWith('}'));
        Profile("{ \"connections\": {\n" + string.Join("\n", example) + "\n} }");

        var loaded = SqlConfigFile.Load(SqlConfigFile.ProfilePath(_profile));

        Assert.Empty(loaded.Problems);
        Assert.Equal(["adventureworks", "reports-me", "reports-admin", "reports-admin-file"], loaded.Connections.Select(c => c.Name));
        Assert.Equal(["sql", "windows", "runas", "runas"], loaded.Connections.Select(c => c.Config.Auth));
        Assert.True(loaded.Connections[2].Config.InCredentialManager);
        Assert.Equal(@"CONTOSO\svc-reader", loaded.Connections[2].Config.User);
        Assert.Contains(@"cmdkey /generic:NeonCompanion/sql/reports-admin /user:CONTOSO\svc-reader /pass", SqlConfigFile.EmptyText);
    }

    [Fact]
    public void Connections_LoadInFileOrder_AndABadEntryIsAProblem_NotAThrow()
    {
        Profile("""
            {
              // comments and a trailing comma are fine
              "connections": {
                "adventureworks": { "server": "127.0.0.1,1433", "database": "AdventureWorks2022", "user": "sa", "password": "p", "trustServerCertificate": true, "encrypt": "optional", "description": "the sample" },
                "corp": { "server": "corp\\inst", "auth": "windows" },
                "nouser": { "server": "x" },
                "noserver": { "user": "u" },
                "badauth": { "server": "x", "auth": "entra", "user": "u" },
                "badencrypt": { "server": "x", "user": "u", "encrypt": "sometimes" },
                "slow": { "server": "x", "user": "u", "connectTimeoutSeconds": 0 },
                "empty": null,
              }
            }
            """);

        var loaded = SqlConfigFile.Load(SqlConfigFile.ProfilePath(_profile));

        Assert.Equal(["adventureworks", "corp"], loaded.Connections.Select(c => c.Name));
        var path = SqlConfigFile.ProfilePath(_profile);
        Assert.Equal(
            [
                new SqlConfigProblem($"{path} (nouser)", SqlText.NoUser),
                new SqlConfigProblem($"{path} (noserver)", SqlText.NoServer),
                new SqlConfigProblem($"{path} (badauth)", SqlText.BadAuth("entra")),
                new SqlConfigProblem($"{path} (badencrypt)", SqlText.BadEncrypt("sometimes")),
                new SqlConfigProblem($"{path} (slow)", SqlText.BadConnectTimeout(0, SqlConnectionConfig.MaxConnectTimeoutSeconds)),
                new SqlConfigProblem($"{path} (empty)", SqlText.NoServer),
            ],
            loaded.Problems);
        Assert.True(loaded.Connections[1].Config.IsWindows);
    }

    [Fact]
    public void AFileThatIsNotJson_IsOneProblem()
    {
        Profile("{ not json");
        var loaded = SqlConfigFile.Load(SqlConfigFile.ProfilePath(_profile));
        Assert.Empty(loaded.Connections);
        var problem = Assert.Single(loaded.Problems);
        Assert.StartsWith("the file cannot be read (", problem.Reason);
    }

    [Fact]
    public void TheProfilesFile_WinsByName_OverTheHomes_AndFindPicksByNameThenDefaultThenFirst()
    {
        Profile("""{ "connections": { "Shared": { "server": "profile-server", "user": "u" }, "mine": { "server": "m", "user": "u" } } }""");
        Global("""{ "connections": { "shared": { "server": "home-server", "user": "u" }, "theirs": { "server": "t", "user": "u" } } }""");

        var catalog = SqlConfigFile.LoadCatalog(_profile, _home);

        Assert.Equal(["Shared", "mine", "theirs"], catalog.Connections.Select(c => c.Name));
        Assert.Equal("profile-server", catalog.Find("shared", null)!.Config.Server);   // names are case-insensitive
        Assert.Equal("theirs", catalog.Find(null, "THEIRS")!.Name);
        Assert.Equal("Shared", catalog.Find(null, "gone")!.Name);   // a default no longer there = the first
        Assert.Equal("Shared", catalog.Find(null, "")!.Name);
        Assert.Null(catalog.Find("nope", null));
        Assert.Equal(["Shared", "mine"], SqlConfigFile.LoadCatalog(_profile, null).Connections.Select(c => c.Name));
    }

    [Fact]
    public void TheBuilder_IsReadOnlyIntent_TheDatabaseOverridable_AndTheLoginAsConfigured()
    {
        var sql = new SqlConnectionConfig { Server = " 127.0.0.1,1433 ", Database = "AdventureWorks2022", User = "sa", Password = "secret", Encrypt = "Optional", TrustServerCertificate = true };
        var builder = sql.Builder();
        Assert.Equal("127.0.0.1,1433", builder.DataSource);
        Assert.Equal("AdventureWorks2022", builder.InitialCatalog);
        Assert.Equal(ApplicationIntent.ReadOnly, builder.ApplicationIntent);
        Assert.Equal("NeonCompanion", builder.ApplicationName);
        Assert.Equal(SqlConnectionEncryptOption.Optional, builder.Encrypt);
        Assert.True(builder.TrustServerCertificate);
        Assert.Equal(SqlConnectionConfig.DefaultConnectTimeoutSeconds, builder.ConnectTimeout);
        Assert.False(builder.IntegratedSecurity);
        Assert.Equal("sa", builder.UserID);
        Assert.Equal("master", sql.Builder("master").InitialCatalog);

        var windows = new SqlConnectionConfig { Server = "corp", Auth = "Windows", User = "ignored" }.Builder();
        Assert.True(windows.IntegratedSecurity);
        Assert.Equal("", windows.UserID);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, windows.Encrypt);   // the default
        Assert.Equal("", windows.InitialCatalog);   // the login's default database
        Assert.Equal(SqlConnectionEncryptOption.Strict, new SqlConnectionConfig { Server = "x", User = "u", Encrypt = "strict" }.Builder().Encrypt);
    }

    [Fact]
    public void RunAs_AndThePasswordStores_AreChecked()
    {
        Assert.Null(new SqlConnectionConfig { Server = "x", Auth = "runas", User = @"CONTOSO\svc" }.Problem);
        Assert.Null(new SqlConnectionConfig { Server = "x", Auth = "RunAs", User = "svc@contoso.com", PasswordStore = "credman" }.Problem);
        Assert.Equal(SqlText.RunAsNeedsDomain("svc"), new SqlConnectionConfig { Server = "x", Auth = "runas", User = "svc" }.Problem);
        Assert.Equal(SqlText.RunAsNeedsDomain(""), new SqlConnectionConfig { Server = "x", Auth = "runas" }.Problem);
        Assert.Equal(SqlText.BadPasswordStore("vault"), new SqlConnectionConfig { Server = "x", User = "u", PasswordStore = "vault" }.Problem);
        Assert.Equal(SqlText.CredmanWithWindows, new SqlConnectionConfig { Server = "x", Auth = "windows", PasswordStore = "credman" }.Problem);
        Assert.Equal(SqlText.BadAuth("entra"), new SqlConnectionConfig { Server = "x", Auth = "entra", User = "u" }.Problem);

        var runas = new SqlConnectionConfig { Server = "sqlhost01,1453", Database = "db", Auth = "runas", User = @"CONTOSO\svc", Password = "never used" }.Builder(password: "p");
        Assert.True(runas.IntegratedSecurity);
        Assert.False(runas.Pooling);   // an integrated pool is keyed by the process's SID, which the netonly token keeps
        Assert.Equal("NeonCompanion (runas)", runas.ApplicationName);
        Assert.Equal("", runas.UserID);
        Assert.Equal("", runas.Password);
        Assert.Equal("p", new SqlConnectionConfig { Server = "x", User = "u", Password = "dpapi:…" }.Builder(password: "p").Password);   // the resolved password, never the stored value
    }

    [Fact]
    public void APlainPassword_IsEncryptedInPlace_TheRestOfTheFileUntouched()
    {
        string text = """
            {
              // the dev box — keep this comment
              "connections": {
                "a": { "server": "x", "user": "sa", "password": "hunter2" },   // trailing note
                "b": { "server": "y", "auth": "windows" },
                "c": { "server": "z", "user": "u", "passwordStore": "credman", "password": "left alone" },
              }
            }
            """;
        Profile(text);
        string path = SqlConfigFile.ProfilePath(_profile);

        var loaded = SqlConfigFile.Load(path);

        string after = File.ReadAllText(path);
        Assert.DoesNotContain("hunter2", after);
        Assert.Contains("// the dev box — keep this comment", after);
        Assert.Contains("},   // trailing note", after);
        Assert.Contains("\"password\": \"left alone\"", after);   // the credman store's password is not the file's to keep
        int start = after.IndexOf("\"password\": \"dpapi:", StringComparison.Ordinal);
        Assert.True(start > 0);
        Assert.Equal(text[..text.IndexOf("\"password\"", StringComparison.Ordinal)], after[..start]);   // everything before the value byte for byte
        Assert.Equal("hunter2", SqlSecrets.Resolve(loaded.Connections[0]).Value);
        Assert.StartsWith("dpapi:", loaded.Connections[0].Config.Password);

        string again = File.ReadAllText(path);
        SqlConfigFile.Load(path);
        Assert.Equal(again, File.ReadAllText(path));   // once: an encrypted value is left as it is
    }

    [Fact]
    public void EncryptAll_ReadsTheHomesFile_AndEveryProfiles_NotOnlyTheLoadedOnes()
    {
        const string plain = """{ "connections": { "a": { "server": "x", "user": "u", "password": "hunter2" } } }""";
        string work = Path.Combine(_home, "profiles", "work");
        Directory.CreateDirectory(work);
        Directory.CreateDirectory(Path.Combine(_home, "profiles", "empty"));   // a profile without a sql.json
        Global(plain);
        Profile(plain);
        File.WriteAllText(SqlConfigFile.ProfilePath(work), plain);

        var read = SqlConfigFile.EncryptAll(_home);

        Assert.Equal([SqlConfigFile.GlobalPath(_home), SqlConfigFile.ProfilePath(_profile), SqlConfigFile.ProfilePath(work)], read);
        foreach (string path in read)
        {
            string text = File.ReadAllText(path);
            Assert.DoesNotContain("hunter2", text);
            Assert.Contains("\"password\": \"dpapi:", text);
        }

        Assert.Empty(SqlConfigFile.EncryptAll(Path.Combine(_home, "nowhere")));   // no home yet: nothing to read
    }

    [Fact]
    public void WritePassword_ReplacesTheValue_OrInsertsTheKey()
    {
        Profile("""{ "connections": { "a": { "server": "x", "user": "u", "password": null }, "b": { "server": "y", "user": "v" }, "c": { "server": "z" } } }""");
        string path = SqlConfigFile.ProfilePath(_profile);

        Assert.Null(SqlConfigFile.WritePassword(path, "a", "dpapi:AAA+/="));
        Assert.Null(SqlConfigFile.WritePassword(path, "b", "dpapi:BBB"));
        Assert.Null(SqlConfigFile.WritePassword(path, "c", "dpapi:CCC"));
        Assert.Equal(
            """{ "connections": { "a": { "server": "x", "user": "u", "password": "dpapi:AAA+/=" }, "b": { "server": "y", "user": "v", "password": "dpapi:BBB" }, "c": { "password": "dpapi:CCC", "server": "z" } } }""",
            File.ReadAllText(path));
        Assert.Equal(SqlText.ConnectionNotInFile("nope"), SqlConfigFile.WritePassword(path, "nope", "dpapi:x"));
    }

    [Fact]
    public void TheListing_NamesEveryConnection_TheDefaultMarked_AndNeverThePassword()
    {
        Profile("""{ "connections": { "aw": { "server": "127.0.0.1,1433", "database": "AdventureWorks2022", "user": "sa", "password": "hunter2", "description": "the sample sales database" }, "corp": { "server": "corp\\inst", "auth": "windows" }, "bad": { "server": "x" } } }""");
        var catalog = SqlConfigFile.LoadCatalog(_profile, _home);

        string text = SqlText.Connections(catalog, "corp");

        Assert.Equal(
            "2 SQL connections (every SQL tool takes one by name in \"connection\"; the default is used when it is left out):\n" +
            "- aw: 127.0.0.1,1433 / AdventureWorks2022, sql login sa — the sample sales database\n" +
            "- corp (default): corp\\inst / (the login's default database), windows sign-in\n" +
            $"Skipped {SqlConfigFile.ProfilePath(_profile)} (bad): {SqlText.NoUser}",
            text);
        Assert.DoesNotContain("hunter2", text);
        Assert.Equal(SqlText.NoConnections, SqlText.Connections(SqlCatalog.Empty, null));
    }
}
