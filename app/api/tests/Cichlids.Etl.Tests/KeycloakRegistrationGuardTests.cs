// Story: task-2.27
using System.Net;
using System.Text;
using Cichlids.Etl.Identity;
using Cichlids.Infrastructure.Identity;

namespace Cichlids.Etl.Tests;

/// <summary>
/// The keycloak-import command opens self-registration only when every planned account exists in
/// Keycloak. The test runs the command against an admin API double that accepts the creation of
/// users but never lists them, so the accounts stay unresolved after the import.
/// </summary>
[Trait("Story", "task-2.27")]
public sealed class KeycloakRegistrationGuardTests(KeycloakImportFixture fixture)
    : IClassFixture<KeycloakImportFixture>
{
    [Fact]
    public async Task KeepsRegistrationClosedAndFailsWhileAnAccountIsUnresolved()
    {
        using var admin = new AdminApiDouble();
        var settings = new KeycloakAdminSettings(admin.BaseUrl, "cichlids", "admin", "admin");

        var exitCode = await KeycloakAccountImportCommand.RunAsync(
            fixture.LegacyConnectionString,
            fixture.TargetConnectionString,
            fixture.Auth0ExportPath,
            settings,
            new GeneratedNames(KeycloakAccountImportTests.Scenario.GeneratedNamesSecret),
            openRegistration: true,
            CancellationToken.None);

        Assert.NotEqual(0, exitCode);
        Assert.True(admin.UsersImported > 0, "the double received no user to create");
        Assert.False(admin.RegistrationOpened);
    }

    /// <summary>
    /// A minimal Keycloak admin API: the realm prerequisites exist, the user list is empty, a
    /// partial import reports every user as added, and a realm update is recorded.
    /// </summary>
    private sealed class AdminApiDouble : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _serving;

        public AdminApiDouble()
        {
            var port = FreePort();
            BaseUrl = new Uri($"http://127.0.0.1:{port}/");
            _listener.Prefixes.Add(BaseUrl.ToString());
            _listener.Start();
            _serving = Task.Run(ServeAsync);
        }

        public Uri BaseUrl { get; }

        public int UsersImported { get; private set; }

        public bool RegistrationOpened { get; private set; }

        public void Dispose()
        {
            _listener.Close();
            try
            {
                _serving.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
            }
        }

        private static int FreePort()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception) when (!_listener.IsListening)
                {
                    return;
                }

                Respond(context);
            }
        }

        private void Respond(HttpListenerContext context)
        {
            var path = context.Request.Url!.AbsolutePath.TrimEnd('/');
            var method = context.Request.HttpMethod;
            var body = string.Empty;
            if (context.Request.HasEntityBody)
            {
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                body = reader.ReadToEnd();
            }

            string json = path switch
            {
                "/realms/master/protocol/openid-connect/token" => """{"access_token":"t","expires_in":300}""",
                "/admin/realms/cichlids/authentication/required-actions" => """[{"alias":"TERMS_AND_CONDITIONS"}]""",
                "/admin/realms/cichlids/authentication/required-actions/TERMS_AND_CONDITIONS" => """{"alias":"TERMS_AND_CONDITIONS","enabled":true}""",
                "/admin/realms/cichlids/client-scopes" => """[{"id":"scope-1","name":"email"}]""",
                "/admin/realms/cichlids/client-scopes/scope-1/protocol-mappers/models" => """[{"config":{"claim.name":"email_verified"}}]""",
                "/admin/realms/cichlids/identity-provider/instances/google" => "{}",
                "/admin/realms/cichlids/identity-provider/instances/facebook" => "{}",
                "/admin/realms/cichlids/users" => "[]",
                "/admin/realms/cichlids/partialImport" => PartialImportResponse(body),
                "/admin/realms/cichlids" when method == "PUT" => RecordRealmUpdate(body),
                _ => string.Empty,
            };

            if (path.Length > 0 && json.Length == 0)
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes);
            context.Response.Close();
        }

        private string PartialImportResponse(string body)
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            var count = document.RootElement.GetProperty("users").GetArrayLength();
            UsersImported += count;
            return $$"""{"added":{{count}},"skipped":0}""";
        }

        private string RecordRealmUpdate(string body)
        {
            if (body.Contains("\"registrationAllowed\":true", StringComparison.Ordinal))
            {
                RegistrationOpened = true;
            }

            return "{}";
        }
    }
}
