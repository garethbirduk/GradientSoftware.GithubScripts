#r "System.Net.Http"
#r "System.Text.Json"

using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

internal class Package
{
    public string Name { get; set; }
}

if (Args.Count() < 4)
{
    Console.WriteLine("Usage: dotnet script update-version.csx <github_owner> <package_name> <github_token> <project_csproj_path> <branchname>");
    Environment.Exit(1);
}

string githubOwner   = Args[0];
string packageName   = Args[1];
string githubToken   = Args[2];
string projectFilePath = Args[3];

Console.WriteLine($"githubOwner:    {githubOwner}");
Console.WriteLine($"packageName:    {packageName}");
Console.WriteLine($"githubToken:    {(githubToken != null ? "<<supplied ok>>" : "<<not ok>>")}");
Console.WriteLine($"projectFilePath: {projectFilePath}");

var name   = Args.Count() > 4 ? Args[4] : "unknown";
Console.WriteLine($"name: {name}");

var update = Update.Unknown;
if      (name.Contains("__breaking",  StringComparison.OrdinalIgnoreCase)) update = Update.Major;
else if (name.Contains("__feature",   StringComparison.OrdinalIgnoreCase)) update = Update.Minor;
else if (name.Contains("__bug",       StringComparison.OrdinalIgnoreCase)) update = Update.Patch;
else if (name.Contains("__nosource",  StringComparison.OrdinalIgnoreCase)) update = Update.None;
else if (name.Contains("__coverage",  StringComparison.OrdinalIgnoreCase)) update = Update.Coverage;
Console.WriteLine($"update: {update}");

// Read current version from csproj — this is the primary source of truth
var csprojVersion = ReadVersionFromCsproj(projectFilePath);
Console.WriteLine($"csproj version: {csprojVersion}");

// Read latest published NuGet version — may be behind, equal, or (edge case) ahead
var nugetVersion = await GetLatestNugetVersionAsync(
    $"https://api.github.com/users/{githubOwner}/packages/nuget/{packageName}/versions",
    githubToken);
Console.WriteLine($"nuget version:  {nugetVersion}");

// Use whichever is higher, then increment
var baseVersion = MaxVersion(csprojVersion, nugetVersion);
Console.WriteLine($"base version:   {baseVersion}");

var newVersion = UpdateVersion(baseVersion, update);
Console.WriteLine($"new version:    {newVersion}");

SetVersion(projectFilePath, newVersion);

// ── Helpers ───────────────────────────────────────────────────────────────────

static string ReadVersionFromCsproj(string filepath)
{
    var pattern = @"<Version>(\d+\.\d+\.\d+)<\/Version>";
    foreach (var line in File.ReadAllLines(filepath))
    {
        var m = Regex.Match(line, pattern);
        if (m.Success) return m.Groups[1].Value;
    }
    return "0.0.0";
}

async Task<string> GetLatestNugetVersionAsync(string apiUrl, string token)
{
    Console.WriteLine($"nuget apiUrl: {apiUrl}");
    var client = new HttpClient();
    client.DefaultRequestHeaders.Add("Authorization", $"token {token}");
    client.DefaultRequestHeaders.Add("User-Agent", "MyApp");
    try
    {
        var response = await client.GetAsync(apiUrl);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var packages = JsonSerializer.Deserialize<Package[]>(json, options).ToList();
        return packages.OrderByDescending(x => x.Name).Select(x => x.Name).FirstOrDefault() ?? "0.0.0";
    }
    catch
    {
        return "0.0.0";
    }
}

static string MaxVersion(string a, string b)
{
    var pa = ParseVersion(a);
    var pb = ParseVersion(b);
    for (int i = 0; i < 3; i++)
    {
        if (pa[i] > pb[i]) return a;
        if (pb[i] > pa[i]) return b;
    }
    return a; // equal — either will do
}

static int[] ParseVersion(string v) =>
    v.Split('.').Select(x => int.TryParse(x, out var n) ? n : 0).Take(3).ToArray();

static void SetVersion(string filepath, string newVersion)
{
    var lines   = File.ReadAllLines(filepath);
    var pattern = @"^(.*<Version>)(\d+\.\d+\.\d+)(<\/Version>.*)$";
    var updated = false;
    for (var i = 0; i < lines.Length; i++)
    {
        var m = Regex.Match(lines[i], pattern);
        if (m.Success)
        {
            var replacement = m.Groups[1].Value + newVersion + m.Groups[3].Value;
            Console.WriteLine($"Original: {lines[i]}");
            Console.WriteLine($"Replaced: {replacement}");
            lines[i] = replacement;
            updated  = true;
        }
    }
    if (updated)
        File.WriteAllLines(filepath, lines);
}

static string UpdateVersion(string version, Update update)
{
    var parts = ParseVersion(version).ToList();
    switch (update)
    {
        case Update.Major:   parts[0]++; parts[1] = 0; parts[2] = 0; break;
        case Update.Minor:   parts[1]++; parts[2] = 0; break;
        case Update.Patch:   parts[2]++; break;
        case Update.Unknown: parts[2]++; break;
        case Update.None:    break;
        case Update.Coverage: break;
    }
    return string.Join(".", parts);
}

enum Update { Unknown, None, Patch, Minor, Major, Coverage }
