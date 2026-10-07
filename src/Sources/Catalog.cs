using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using TvDesk.Core;

namespace TvDesk.Sources;

public sealed class LocalText
{
    public string Fa { get; set; } = "";
    public string En { get; set; } = "";

    public override string ToString() => Loc.I.IsRtl && Fa.Length > 0 ? Fa : (En.Length > 0 ? En : Fa);
}

public sealed class PlaylistRef
{
    public string Url { get; set; } = "";

    /// <summary>What group-title means in this playlist: category | country | language | auto.</summary>
    public string GroupMeans { get; set; } = "auto";

    /// <summary>Country to assign to every channel of this playlist (e.g. "IR").</summary>
    public string? Country { get; set; }

    /// <summary>Only add information (language, country) to channels that other playlists already provided.</summary>
    public bool EnrichOnly { get; set; }
}

public sealed class InlineChannel
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Category { get; set; }
    public string? Country { get; set; }
    public string? Language { get; set; }
    public string? Logo { get; set; }
}

public sealed class SourceDef
{
    public string Id { get; set; } = "";
    public LocalText Name { get; set; } = new();
    public LocalText Description { get; set; } = new();
    public string Icon { get; set; } = "";
    public bool DefaultEnabled { get; set; }
    public int Priority { get; set; } = 10;
    public string? Homepage { get; set; }
    public List<PlaylistRef> Playlists { get; set; } = new();
    public List<InlineChannel> Channels { get; set; } = new();

    [JsonIgnore] public bool IsCustom { get; set; }
    [JsonIgnore] public string DisplayName => Name.ToString();
}

public sealed class CatalogFile
{
    public int Version { get; set; }
    public List<SourceDef> Sources { get; set; } = new();
}

/// <summary>The list of free sources TvDesk offers. Loaded from the embedded catalog.json;
/// a %AppData%\TvDesk\catalog.json overrides it (handy for testing new lists without rebuilding).</summary>
public sealed class Catalog
{
    public List<SourceDef> Sources { get; private set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Catalog Load()
    {
        var catalog = new Catalog();
        try
        {
            string? json = null;
            if (File.Exists(AppPaths.CatalogOverrideFile))
            {
                json = File.ReadAllText(AppPaths.CatalogOverrideFile);
                Logger.Log("Using catalog override from " + AppPaths.CatalogOverrideFile);
            }
            if (json == null)
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TvDesk.catalog.json");
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    json = reader.ReadToEnd();
                }
            }
            if (json != null)
            {
                var file = JsonSerializer.Deserialize<CatalogFile>(json, Options);
                if (file?.Sources != null)
                    catalog.Sources = file.Sources.Where(s => !string.IsNullOrWhiteSpace(s.Id)).ToList();
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Catalog load failed", ex);
        }
        Logger.Log($"Catalog: {catalog.Sources.Count} built-in sources");
        return catalog;
    }

    public SourceDef? Find(string id) => Sources.FirstOrDefault(s => s.Id == id);

    public bool IsEnabled(AppSettings settings, SourceDef def)
        => settings.Sources.TryGetValue(def.Id, out bool on) ? on : def.DefaultEnabled;
}
