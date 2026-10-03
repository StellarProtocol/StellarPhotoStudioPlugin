using System;
using System.IO;
using System.Linq;
using Xunit;
namespace Stellar.PhotoStudio.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Lang_files_have_the_same_keys()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "../../../../../Lang");
        var en = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "en.json"))).RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
        foreach (var code in new[] { "ja", "th", "id", "fil" })
        {
            var keys = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, code + ".json"))).RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
            Assert.True(en.SetEquals(keys), code);
        }
    }
}
