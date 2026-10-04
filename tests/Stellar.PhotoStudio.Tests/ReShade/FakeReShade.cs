using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio.Tests.ReShade;

/// <summary>An IReShade whose setters only RECORD the request (like the real, asynchronous service); a test "applies"
/// a request by changing <see cref="EnabledNow"/> / <see cref="List"/> / <see cref="CurrentPreset"/>.</summary>
internal sealed class FakeReShade : IReShade
{
    public ReShadeState State { get; set; } = ReShadeState.Ready;
    public bool EnabledNow = true;
    public readonly List<bool> EnabledRequests = new();
    public bool Enabled { get => EnabledNow; set => EnabledRequests.Add(value); }
    public List<ReShadeTechnique> List = new();
    public IReadOnlyList<ReShadeTechnique> Techniques => List;
    public string? CurrentPreset { get; set; }
    public readonly List<(string Effect, string Name, bool On)> TechniqueCalls = new();
    public readonly List<string> PresetCalls = new();
    public readonly List<(IReadOnlyList<string> Effects, IReadOnlyList<string> Textures)> SearchCalls = new();
    public void SetTechnique(string effectFile, string name, bool enabled) => TechniqueCalls.Add((effectFile, name, enabled));
    public void SetPreset(string path) => PresetCalls.Add(path);
    public void SetSearchPaths(IReadOnlyList<string> effectFolders, IReadOnlyList<string> textureFolders) => SearchCalls.Add((effectFolders, textureFolders));
    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();
}
