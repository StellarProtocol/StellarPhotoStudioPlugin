using System.Collections.Generic;
namespace Stellar.PhotoStudio.Presets;

/// <summary>Raw name/JSON storage for user presets, independent of the underlying medium
/// (plugin data store on the running client, in-memory in tests).</summary>
internal interface IPresetFiles
{
    IEnumerable<string> List();
    string? Read(string name);
    void Write(string name, string json);
    void Delete(string name);
}
