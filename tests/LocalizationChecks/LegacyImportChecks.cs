using LiteDB;
using PvpStats.Services;
using System.Security.Cryptography;

internal static class LegacyImportChecks {
    internal static void Run(Action<bool, string> check) {
        var root = Path.Combine(Path.GetTempPath(), "ccstats-import-" + Guid.NewGuid());
        var legacyDirectory = Path.Combine(root, "PvpStats");
        Directory.CreateDirectory(legacyDirectory);
        var sourcePath = Path.Combine(legacyDirectory, "data.db");
        try {
            check(LegacyDataImporter.SourcePath(Path.Combine(root, "CCStats") + Path.DirectorySeparatorChar) == sourcePath, "legacy path is sibling PvpStats, including trailing separator");
            var targetPath = Path.Combine(root, "ccstats.db");
            using var target = new LiteDatabase(targetPath);
            check(LegacyDataImporter.Import(target, sourcePath).State == "NotFound", "missing source is harmless and retryable");
            var id = ObjectId.NewObjectId();
            var timelineId = ObjectId.NewObjectId();
            using(var source = new LiteDatabase(sourcePath)) {
                source.GetCollection("ccmatch").Insert(new BsonDocument { ["_id"] = id, ["Name"] = "old", ["TimelineId"] = timelineId, ["UnknownHistoricalField"] = new BsonDocument { ["Damage"] = 123456L } });
                source.GetCollection("cctimeline").Insert(new BsonDocument { ["_id"] = timelineId, ["Events"] = new BsonArray(new BsonDocument { ["_type"] = "PvpStats.Types.Event.KillEvent, PvpStats", ["Text"] = "Name, PvpStats" }) });
                source.GetCollection("playerlinks_manual").Insert(new BsonDocument { ["_id"] = ObjectId.NewObjectId(), ["IsUnlink"] = true });
                source.GetCollection("frontlinematch").Insert(new BsonDocument { ["_id"] = ObjectId.NewObjectId() });
                source.Checkpoint();
            }
            var before = SHA256.HashData(File.ReadAllBytes(sourcePath));
            var result = LegacyDataImporter.Import(target, sourcePath);
            check(result.State == "Imported" && result.Matches == 1 && result.Timelines == 1 && result.Links == 1, "imports matches, timelines and alias links");
            check(target.GetCollection("ccmatch").FindById(id)["UnknownHistoricalField"]["Damage"] == 123456L, "raw BSON preserves unknown historical data");
            check(target.GetCollection("ccmatch").FindById(id)["TimelineId"].AsObjectId == timelineId && target.GetCollection("cctimeline").FindById(timelineId) != null, "timeline references remain valid");
            var importedEvent = target.GetCollection("cctimeline").FindById(timelineId)["Events"].AsArray[0].AsDocument;
            check(importedEvent["_type"] == "PvpStats.Types.Event.KillEvent, CCStats" && importedEvent["Text"] == "Name, PvpStats", "timeline assembly metadata migrates while user text is unchanged");
            check(!target.CollectionExists("frontlinematch"), "other game modes are excluded");
            check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(sourcePath))), "source database stays byte-for-byte unchanged");
            target.GetCollection("ccmatch").Update(new BsonDocument { ["_id"] = id, ["Name"] = "local edit" });
            check(LegacyDataImporter.Import(target, sourcePath).State == "AlreadyImported" && target.GetCollection("ccmatch").Count() == 1, "completion marker makes restart idempotent");
            check(target.GetCollection("ccmatch").FindById(id)["Name"] == "local edit", "restarts preserve local edits");

            using var merge = new LiteDatabase(Path.Combine(root, "merge.db"));
            merge.GetCollection("ccmatch").Insert(new BsonDocument { ["_id"] = id, ["Name"] = "existing" });
            var merged = LegacyDataImporter.Import(merge, sourcePath);
            check(merged.Matches == 0 && merged.Timelines == 1 && merge.GetCollection("ccmatch").FindById(id)["Name"] == "existing", "existing destination wins ID conflicts while missing timelines merge");

            using var rollback = new LiteDatabase(Path.Combine(root, "rollback.db"));
            rollback.GetCollection("playerlinks_manual").Insert(new BsonDocument { ["_id"] = ObjectId.NewObjectId(), ["IsUnlink"] = true });
            rollback.GetCollection("playerlinks_manual").EnsureIndex("IsUnlink", true);
            bool failed = false;
            try { LegacyDataImporter.Import(rollback, sourcePath); } catch(LiteException) { failed = true; }
            check(failed && rollback.GetCollection("ccmatch").Count() == 0 && rollback.GetCollection("cctimeline").Count() == 0, "late collection failure rolls back previously inserted match and timeline");
            check(rollback.GetCollection("ccstats_imports").Count() == 0, "failed transaction does not mark migration complete");
            rollback.GetCollection("playerlinks_manual").DropIndex("IsUnlink");
            check(LegacyDataImporter.Import(rollback, sourcePath).State == "Imported", "failed migration can retry successfully");
        } finally { Directory.Delete(root, true); }
    }
}
