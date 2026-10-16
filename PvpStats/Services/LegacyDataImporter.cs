using LiteDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PvpStats.Services;

internal sealed record LegacyImportResult(string State, int Matches = 0, int Timelines = 0, int Links = 0);

// Use raw BSON to preserve historical fields without loading removed game-mode types.
internal static class LegacyDataImporter {
    private const string MarkerCollection = "ccstats_imports";
    private static readonly string[] Collections = ["ccmatch", "cctimeline", "playerlinks_auto", "playerlinks_manual"];

    internal static string SourcePath(string configDirectory) =>
        Path.Combine(Directory.GetParent(Path.TrimEndingDirectorySeparator(Path.GetFullPath(configDirectory)))!.FullName, "PvpStats", "data.db");

    internal static LegacyImportResult? PreviousImport(LiteDatabase destination) {
        var previous = destination.GetCollection(MarkerCollection).FindById("PvpStats-watermark-v1");
        return previous == null ? null : new("AlreadyImported", previous["Matches"].AsInt32, previous["Timelines"].AsInt32, previous["Links"].AsInt32);
    }

    internal static LegacyImportResult Import(LiteDatabase destination, string sourcePath) {
        var markers = destination.GetCollection(MarkerCollection);
        const string marker = "PvpStats-watermark-v1";
        if(PreviousImport(destination) is { } previous) return previous;
        if(!File.Exists(sourcePath)) return new("NotFound");

        // Opening a locked or damaged database throws; caller reports and retries next load.
        // No marker is written until ALL collections and the completion marker commit together.
        using var source = new LiteDatabase(new ConnectionString { Filename = sourcePath, ReadOnly = true });
        var counts = new int[Collections.Length];
        if(!destination.BeginTrans()) throw new InvalidOperationException("Import requires its own transaction.");
        try {
            var available = new HashSet<string>(source.GetCollectionNames(), StringComparer.OrdinalIgnoreCase);
            for(int i = 0; i < Collections.Length; i++) {
                var name = Collections[i];
                if(!available.Contains(name)) continue;
                var target = destination.GetCollection(name);
                foreach(var document in source.GetCollection(name).FindAll()) {
                    var id = document["_id"];
                    if(id.IsNull) throw new InvalidDataException($"Missing ID in {name}.");
                    if(target.FindById(id) != null) continue;
                    NormalizeTypeNames(document);
                    target.Insert(document);
                    counts[i]++;
                }
            }
            markers.Insert(new BsonDocument {
                ["_id"] = marker, ["Source"] = Path.GetFullPath(sourcePath), ["CompletedUtc"] = DateTime.UtcNow,
                ["Matches"] = counts[0], ["Timelines"] = counts[1], ["Links"] = counts[2] + counts[3],
            });
            destination.Commit();
            return new("Imported", counts[0], counts[1], counts[2] + counts[3]);
        } catch {
            destination.Rollback();
            throw;
        }
    }
    // LiteDB embeds assembly-qualified type names for polymorphic timeline events.
    // Only rewrite its metadata, never player names, tags, IDs or other string fields.
    private static void NormalizeTypeNames(BsonValue value) {
        if(value.IsDocument) {
            foreach(var pair in value.AsDocument.ToArray()) {
                if(pair.Key == "_type" && pair.Value.IsString)
                    value.AsDocument[pair.Key] = Regex.Replace(pair.Value.AsString, @",\s*PvpStats(?=,|$|\])", ", CCStats");
                else NormalizeTypeNames(pair.Value);
            }
        } else if(value.IsArray) {
            foreach(var item in value.AsArray) NormalizeTypeNames(item);
        }
    }

}
