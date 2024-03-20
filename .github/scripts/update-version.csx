#r "System.Net.Http"
#r "System.Text.Json"

using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

// Define the class structure
internal class Response
{
    public Package[] Packages { get; set; }
}

internal class Package
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Url { get; set; }
    public string PackageHtmlUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string HtmlUrl { get; set; }
    public Metadata Metadata { get; set; }
}

internal class Metadata
{
    public string PackageType { get; set; }
}

// Ensure correct number of arguments provided
if (Args.Count() < 4)
{
    Console.WriteLine("Usage: dotnet script get-latest-package-version.csx <github_owner> <package_name> <github_token> <project_csproj_path> <branchname>");
    Environment.Exit(1);
}

var local = true;

string githubOwner = Args[0];
string packageName = Args[1];
string githubToken = Args[2];

var projectFilePath = "";
if (Args != null)
{
    if (Args.Count() > 3 && Args[3] != null)
    {
        projectFilePath = Args[3];
        Console.WriteLine($"projectFilePath: {projectFilePath}");
    }
}

var name = "unknown";
var update = Update.Unknown;
if (Args != null)
{
    if (Args.Count() > 4 && Args[4] != null)
    {
        name = Args[4];
        Console.WriteLine($"name: {name}");
    }

    if (name.Contains("__breaking", StringComparison.OrdinalIgnoreCase))
        update = Update.Major;

    if (name.Contains("__feature", StringComparison.OrdinalIgnoreCase))
        update = Update.Minor;

    if (name.Contains("__bug", StringComparison.OrdinalIgnoreCase))
        update = Update.Patch;

    if (name.Contains("__nosource", StringComparison.OrdinalIgnoreCase))
        update = Update.None;

    if (name.Contains("__coverage", StringComparison.OrdinalIgnoreCase))
        update = Update.Coverage;
}

var version = await GetLatestVersionOrDefaultAsync($"https://api.github.com/users/{githubOwner}/packages/nuget/{packageName}/versions", githubToken);
SetVersion(projectFilePath, UpdateVersion(version, update));

async Task<string> GetLatestVersionOrDefaultAsync(string apiUrl, string githubToken)
{
    Console.WriteLine($"apiUrl: {apiUrl}");
    HttpClient client = new HttpClient();
    client.DefaultRequestHeaders.Add("Authorization", $"token {githubToken}");
    client.DefaultRequestHeaders.Add("User-Agent", "MyApp");

    try
    {
        HttpResponseMessage response = await client.GetAsync(apiUrl);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var packages = JsonSerializer.Deserialize<Package[]>(json, options).ToList();

        var latestVersion = packages.OrderByDescending(x => x.Name).Select(x => x.Name).FirstOrDefault();
        if (latestVersion == null)
            return "0.0.0";
        return latestVersion;
    }
    catch
    {
        return "0.0.0";
    }
}

static void SetVersion(string filepath, string newVersion)
{
    // Read all lines from the project file
    string[] lines = File.ReadAllLines(filepath);

    // Define the pattern
    string pattern = @"^(.*<Version>)(\d+\.\d+\.\d+)(<\/Version>.*)$";

    var updated = false;
    // Iterate over the list of strings and find matches
    for (var i = 0; i < lines.Count(); i++)
    {
        var line = lines[i];
        Match match = Regex.Match(line, pattern);

        // If a match is found, replace the version number with an incremented patch number
        if (match.Success)
        {
            string replacedLine = match.Groups[1].Value + newVersion + match.Groups[3].Value;
            Console.WriteLine("Original: " + line);
            Console.WriteLine("Replaced: " + replacedLine);

            lines[i] = replacedLine;
            updated = true;
        }
    }

    if (updated)
        File.WriteAllLines(filepath, lines);
}

static string UpdateVersion(string version, Update update)
{
    Console.WriteLine($"update type: {update}");
    var parts = version.Split('.').Select(x => int.Parse(x)).ToList();

    switch (update)
    {
        case Update.Major:
            parts[0]++;
            parts[1] = 0;
            parts[2] = 0;
            break;

        case Update.Minor:
            parts[1]++;
            parts[2] = 0;
            break;

        case Update.Patch:
            parts[2]++;
            break;

        case Update.Unknown:
            parts[2]++;
            break;

        case Update.None:
            break;

        case Update.Coverage:
            break;
    }
    return string.Join(".", parts);
}

enum Update
{
    Unknown,
    None,
    Patch,
    Minor,
    Major,
    Coverage
}