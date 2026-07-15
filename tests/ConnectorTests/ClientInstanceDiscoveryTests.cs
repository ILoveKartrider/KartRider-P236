using System.Text;
using KartRider.P236.Connector;
using Xunit;

namespace KartRider.P236.Connector.Tests;

public sealed class ClientInstanceDiscoveryTests
{
    [Fact]
    public void DiscoveryCombinesPortableRootsAndRegistryWithManifestPrecedence()
    {
        using TemporaryDirectory temporary = new TemporaryDirectory();
        string package = CreateClient(Path.Combine(temporary.Path, "package"));
        string client1 = CreateClient(Path.Combine(package, "clients", "client1"));
        string client2 = CreateClient(Path.Combine(package, "clients", "client2"));
        string external = CreateClient(Path.Combine(temporary.Path, "external"));
        _ = ClientInstanceDiscovery.RememberInstance(package, client1, "Portable One");

        ClientInstanceDiscoveryResult result = ClientInstanceDiscovery.DiscoverPreparedInstances(
            package,
            new[]
            {
                ("Registry Duplicate", client1),
                ("Registry External", external)
            },
            File.Exists,
            File.Exists,
            _ => { },
            root => Path.GetFileName(root) + "-user");

        Assert.Equal(4, result.Instances.Count);
        ClientInstanceOption manifest = Assert.Single(result.Instances.Where(instance =>
            string.Equals(instance.RootDirectory, client1, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("Portable One", manifest.Name);
        Assert.Equal("client1-user", manifest.Username);
        Assert.Contains(result.Instances, instance =>
            string.Equals(instance.RootDirectory, package, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Instances, instance =>
            string.Equals(instance.RootDirectory, client2, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Instances, instance =>
            string.Equals(instance.RootDirectory, external, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Path.Combine(client1, "KartRider.exe"), result.PreferredExecutablePath);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void RelativeCatalogSurvivesMovingTheWholePackage()
    {
        using TemporaryDirectory temporary = new TemporaryDirectory();
        string originalPackage = Path.Combine(temporary.Path, "package-a");
        string originalClient = CreateClient(Path.Combine(originalPackage, "clients", "client2"));
        string catalogPath = ClientInstanceDiscovery.RememberInstance(
            originalPackage,
            originalClient,
            "Portable Two");
        string json = File.ReadAllText(catalogPath, Encoding.UTF8);

        Assert.Contains("clients/client2", json);
        Assert.DoesNotContain(originalPackage, json, StringComparison.OrdinalIgnoreCase);

        string movedPackage = Path.Combine(temporary.Path, "package-b");
        Directory.Move(originalPackage, movedPackage);
        string movedClient = Path.Combine(movedPackage, "clients", "client2");
        ClientInstanceDiscoveryResult result = ClientInstanceDiscovery.DiscoverPreparedInstances(
            movedPackage,
            Array.Empty<(string Name, string Root)>(),
            File.Exists,
            File.Exists,
            _ => { },
            _ => null);

        ClientInstanceOption instance = Assert.Single(result.Instances);
        Assert.Equal("Portable Two", instance.Name);
        Assert.Equal(movedClient, instance.RootDirectory);
        Assert.Equal(Path.Combine(movedClient, "KartRider.exe"), result.PreferredExecutablePath);
    }

    [Fact]
    public void MalformedOrEscapingCatalogDoesNotBlockAutomaticDiscovery()
    {
        using TemporaryDirectory temporary = new TemporaryDirectory();
        string package = CreateClient(Path.Combine(temporary.Path, "package"));
        string catalogPath = Path.Combine(package, ClientInstanceDiscovery.CatalogFileName);
        File.WriteAllText(catalogPath, "{not-json", new UTF8Encoding(false));

        ClientInstanceDiscoveryResult malformed = ClientInstanceDiscovery.DiscoverPreparedInstances(
            package,
            Array.Empty<(string Name, string Root)>(),
            File.Exists,
            File.Exists,
            _ => { },
            _ => null);

        Assert.Single(malformed.Instances);
        Assert.Single(malformed.Warnings);

        File.WriteAllText(
            catalogPath,
            """
            {
              "version": 1,
              "lastSelectedPath": "../outside",
              "instances": [
                null,
                { "name": "escape", "path": "../outside" },
                { "name": "base", "path": "." }
              ]
            }
            """,
            new UTF8Encoding(false));
        ClientInstanceDiscoveryResult escaping = ClientInstanceDiscovery.DiscoverPreparedInstances(
            package,
            Array.Empty<(string Name, string Root)>(),
            File.Exists,
            File.Exists,
            _ => { },
            _ => null);

        ClientInstanceOption only = Assert.Single(escaping.Instances);
        Assert.Equal("base", only.Name);
        Assert.Null(escaping.PreferredExecutablePath);
        Assert.Single(escaping.Warnings);
    }

    private static string CreateClient(string root)
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "KartRider.exe"), [0x4D, 0x5A]);
        return Path.GetFullPath(root);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "KartRider.P236.Connector.DiscoveryTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
