using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.Presets;
using Xunit;
namespace Stellar.PhotoStudio.Tests;

public sealed class DataStorePresetFilesTests
{
    private sealed class FakeDataStore : IPluginDataStore
    {
        public readonly Dictionary<string, byte[]> Files = new(StringComparer.Ordinal);
        public void Write(string name, byte[] data) => Files[name] = data;
        public byte[]? Read(string name) => Files.TryGetValue(name, out var d) ? d : null;
        public void Delete(string name) => Files.Remove(name);
        public IReadOnlyList<string> List(string? prefix = null) =>
            Files.Keys.Where(k => prefix is null || k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }

    [Fact]
    public void Write_stores_under_the_presets_prefix_with_a_json_suffix()
    {
        var store = new FakeDataStore();
        var files = new DataStorePresetFiles(store);

        files.Write("Mine", "{\"a\":1}");

        Assert.True(store.Files.ContainsKey("presets/Mine.json"));
        Assert.Equal("{\"a\":1}", Encoding.UTF8.GetString(store.Files["presets/Mine.json"]));
    }

    [Fact]
    public void Read_round_trips_and_is_null_when_absent()
    {
        var store = new FakeDataStore();
        var files = new DataStorePresetFiles(store);
        files.Write("Mine", "hello");

        Assert.Equal("hello", files.Read("Mine"));
        Assert.Null(files.Read("Missing"));
    }

    [Fact]
    public void List_strips_the_prefix_and_suffix_and_ignores_other_directories()
    {
        var store = new FakeDataStore();
        store.Write("presets/A.json", Encoding.UTF8.GetBytes("x"));
        store.Write("presets/B.json", Encoding.UTF8.GetBytes("y"));
        store.Write("other/C.json", Encoding.UTF8.GetBytes("z"));
        var files = new DataStorePresetFiles(store);

        Assert.Equal(new[] { "A", "B" }, files.List().OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Delete_removes_the_prefixed_file()
    {
        var store = new FakeDataStore();
        var files = new DataStorePresetFiles(store);
        files.Write("Mine", "x");

        files.Delete("Mine");

        Assert.False(store.Files.ContainsKey("presets/Mine.json"));
        Assert.Null(files.Read("Mine"));
    }
}
