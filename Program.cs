using System.Net.Http.Headers;
using System.Text.Json;
using GitHubProjectConnection;
using Microsoft.Extensions.Configuration;

// ---------------------------------------------------------------------------
// Configuration. appsettings.json can be overridden by environment variables,
// e.g. set GitHubApp__ClientIdOrAppId / GitHubApp__PrivateKeyPath.
// ---------------------------------------------------------------------------
IConfiguration config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

string clientIdOrAppId = Require(config, "GitHubApp:ClientIdOrAppId");
string privateKeyPath = Require(config, "GitHubApp:PrivateKeyPath");
string owner = Require(config, "Target:Owner");
bool isOrg = config["Target:OwnerType"]?.Equals("organization", StringComparison.OrdinalIgnoreCase) ?? true;
string repo = Require(config, "Target:Repo");
int projectNumber = int.Parse(Require(config, "Target:ProjectNumber"));
string issueTitle = Require(config, "Issue:Title");
string? issueBody = config["Issue:Body"];

string privateKeyPem = File.ReadAllText(ResolvePath(privateKeyPath));

// Custom fields the user wants populated: field name -> raw JSON value.
Dictionary<string, JsonElement> desiredFields = ReadCustomFields(config);

// One HttpClient for everything. GitHub requires a User-Agent header.
using var http = new HttpClient { BaseAddress = new Uri("https://api.github.com/") };
http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GitHubProjectConnection", "1.0"));

// ---------------------------------------------------------------------------
// 1. Authenticate as the GitHub App -> installation access token.
// ---------------------------------------------------------------------------
var authenticator = new GitHubAppAuthenticator(http, clientIdOrAppId, privateKeyPem);
string jwt = authenticator.CreateJwt();

long installationId = long.TryParse(config["GitHubApp:InstallationId"], out long configured) && configured > 0
    ? configured
    : await authenticator.GetInstallationIdAsync(jwt, owner, isOrg);
Console.WriteLine($"Using installation id {installationId}.");

string token = await authenticator.CreateInstallationTokenAsync(jwt, installationId);
Console.WriteLine("Obtained installation access token.");

var github = new GitHubClient(http, token);

// ---------------------------------------------------------------------------
// 2. Create the issue. Use freshly generated sample data so repeated runs
//    produce distinct issues and field values.
// ---------------------------------------------------------------------------
GeneratedIssueData sample = SampleDataGenerator.Generate();
CreatedIssue issue = await github.CreateIssueAsync(owner, repo, sample.Title, sample.Body);
Console.WriteLine($"Created issue #{issue.Number}: {issue.HtmlUrl}");

// ---------------------------------------------------------------------------
// 3. Resolve the project and read its custom fields.
// ---------------------------------------------------------------------------
string projectId = await github.GetProjectIdAsync(owner, isOrg, projectNumber);
IReadOnlyDictionary<string, ProjectField> fields = await github.GetProjectFieldsAsync(projectId);
Console.WriteLine($"Resolved project {projectId} with {fields.Count} fields:");
foreach (ProjectField f in fields.Values)
{
    string options = f.Options.Count > 0 ? $" [options: {string.Join(", ", f.Options.Keys)}]" : "";
    Console.WriteLine($"    - {f.Name} ({f.DataType}){options}");
}

// ---------------------------------------------------------------------------
// 4. Add the issue to the project.
// ---------------------------------------------------------------------------
string itemId = await github.AddItemToProjectAsync(projectId, issue.NodeId);
Console.WriteLine($"Added issue to project as item {itemId}.");

// ---------------------------------------------------------------------------
// 5. Populate each requested custom field.
// ---------------------------------------------------------------------------
foreach ((string fieldName, JsonElement value) in sample.Fields)
{
    if (!fields.TryGetValue(fieldName, out ProjectField? field))
    {
        Console.WriteLine($"  ! Skipping '{fieldName}' — no such field in the project.");
        continue;
    }

    try
    {
        await github.UpdateFieldValueAsync(projectId, itemId, field, value);
        Console.WriteLine($"  ✓ Set '{fieldName}' = {value}");
    }
    catch (Exception ex)
    {
        // Don't abort the whole run because one field value is wrong.
        Console.WriteLine($"  ! Could not set '{fieldName}': {ex.Message}");
    }
}

Console.WriteLine("\nDone.");

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
static string Require(IConfiguration config, string key) =>
    config[key] ?? throw new InvalidOperationException($"Missing required config value '{key}'.");

static string ResolvePath(string path)
{
    if (Path.IsPathRooted(path)) return path;

    // Look next to the running binary (output folder) first, then in the current
    // working directory (the project root when launched via `dotnet run`).
    foreach (string root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
    {
        string candidate = Path.Combine(root, path);
        if (File.Exists(candidate)) return candidate;
    }

    throw new FileNotFoundException(
        $"Could not find '{path}'. Place it next to appsettings.json or set " +
        "GitHubApp:PrivateKeyPath to an absolute path.");
}

static Dictionary<string, JsonElement> ReadCustomFields(IConfiguration config)
{
    // Re-read the raw JSON so values keep their real types (number vs string).
    // Allow JSONC-style comments so appsettings.json can be annotated inline.
    var options = new JsonDocumentOptions
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    using JsonDocument doc = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")),
        options);

    var result = new Dictionary<string, JsonElement>();
    if (doc.RootElement.TryGetProperty("CustomFields", out JsonElement custom))
        foreach (JsonProperty prop in custom.EnumerateObject())
            result[prop.Name] = prop.Value.Clone();

    return result;
}
