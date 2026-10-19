using System.Diagnostics;
using System.Text;
using Wanderveil.Features.Discover;
using Wanderveil.Features.Stamps;
using Wanderveil.Infrastructure.Storage;

namespace Wanderveil.UiChecks;

// Local stamp storage: round trips, atomic replacement with a backup, recovery that never discards an
// unreadable file, newer and locked files left untouched, profile separation, and saving off the UI thread.
// Every case runs in its own temporary directory, removed afterwards.
internal static class StorageChecks
{
    private static readonly DateTimeOffset Issued = new(2026, 10, 19, 18, 30, 0, TimeSpan.FromHours(2));
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private const string Profile = "local";

    internal static void Run(Action<bool, string> check)
    {
        RoundTrip(check);
        Placements(check);
        Migration(check);
        Recovery(check);
        Untouched(check);
        Interrupted(check);
        Invalid(check);
        Profiles(check);
        Failure(check);
        Larger(check);
        Console.WriteLine("PASS stamp-storage");
    }

    private static void RoundTrip(Action<bool, string> check) => InDirectory(directory =>
    {
        var store = new StampStore(directory);
        var path = store.PathFor(Profile);
        using (var first = new StampPersistence(store, Profile, () => Issued))
        {
            check(first.Loaded == StampLoadOutcome.New && first.State == StampSaveState.Saved && first.Collection.Stamps.Count == 0, "A new profile does not start empty.");
            check(!File.Exists(path), "Loading a new profile writes a file.");
            first.Collection.Collect(DiscoveryPreview.Postcard.Id, DiscoveryPreview.Postcard.Stamp);
            check(first.Flush(Wait) && first.State == StampSaveState.Saved && File.Exists(path), "A collected stamp is not saved.");
            check(!File.Exists(path + ".tmp"), "A finished save leaves its temporary file.");
        }

        using (var second = new StampPersistence(store, Profile, () => Issued))
        {
            check(second.Loaded == StampLoadOutcome.Loaded && second.Collection.Stamps.Count == 1, "A saved stamp does not survive a reload.");
            var stamp = second.Collection.Stamps[0];
            check(stamp.Design == DiscoveryPreview.Postcard.Stamp && stamp.DiscoveryId == DiscoveryPreview.Postcard.Id && stamp.Provenance == StampProvenance.LocalDemo, "A reloaded stamp loses its design, discovery, or provenance.");
            check(stamp.CollectedAt == Issued && stamp.CollectedAt.Offset == Issued.Offset, "A reloaded stamp loses its date or offset.");
            check(second.Collection.Collect(DiscoveryPreview.Postcard.Id, DiscoveryPreview.Postcard.Stamp).Outcome == CollectOutcome.AlreadyCollected, "A reloaded stamp can be collected again.");
            var spot = second.Collection.Collect(DiscoveryPreview.StampSpot.Id, DiscoveryPreview.StampSpot.Stamp).Stamp!;
            check(spot.Id == "local-demo-0002", "Stamp identifiers do not continue after a reload.");
            check(second.Flush(Wait) && File.Exists(path + ".bak"), "Replacing a save keeps no backup of the previous one.");
            check(StampStore.Parse(File.ReadAllText(path)).Stamps.Count == 2 && StampStore.Parse(File.ReadAllText(path + ".bak")).Stamps.Count == 1, "The backup is not the previous save.");
        }

        // Disposed storage stops saving: the collection lives on in memory, the file does not change.
        var disposed = new StampPersistence(store, Profile, () => Issued);
        disposed.Dispose();
        var before = File.ReadAllText(path);
        disposed.Collection.Collect("demo-after-dispose", DiscoveryPreview.Postcard.Stamp);
        Thread.Sleep(50);
        check(File.ReadAllText(path) == before, "Storage saves after it was disposed.");
        disposed.Dispose();
    });

    private static void Recovery(Action<bool, string> check)
    {
        foreach (var (name, damage) in new (string, Func<string, string>)[]
        {
            ("corrupt", _ => "{ this is not a stamp file"),
            ("truncated", text => text[..(text.Length / 2)]),
            ("empty", _ => ""),
            ("wrong format", text => text.Replace(StampStore.Format, "something-else")),
        })
            InDirectory(directory =>
            {
                var store = new StampStore(directory);
                var path = Seed(store, 2);
                var damaged = damage(File.ReadAllText(path));
                File.WriteAllText(path, damaged);
                var load = store.Load(Profile);
                check(load.Outcome == StampLoadOutcome.Recovered && load.Book.Stamps.Count == 1, name + ": an unreadable save does not recover from its backup.");
                check(load.Kept.Count == 1 && File.ReadAllText(load.Kept[0]) == damaged, name + ": the unreadable file was not kept as it was.");
                check(store.Load(Profile).Outcome == StampLoadOutcome.Loaded, name + ": the recovered save is not readable afterwards.");
            });

        InDirectory(directory =>
        {
            var store = new StampStore(directory);
            var path = Seed(store, 2);
            File.WriteAllText(path, "broken");
            File.WriteAllText(path + ".bak", "also broken");
            using var storage = new StampPersistence(store, Profile, () => Issued);
            check(storage.Loaded == StampLoadOutcome.Unreadable && storage.Collection.Stamps.Count == 0 && storage.Kept.Count == 2, "Two unreadable files do not start an empty passport and keep both.");
            check(storage.Kept.All(File.Exists) && !File.Exists(path), "Unreadable files were not set aside.");
            check(StampStorageText.Notice(storage) != null, "An unreadable save is not mentioned.");
            storage.Collection.Collect(DiscoveryPreview.Postcard.Id, DiscoveryPreview.Postcard.Stamp);
            check(storage.Flush(Wait) && storage.State == StampSaveState.Saved && StampStore.Parse(File.ReadAllText(path)).Stamps.Count == 1, "A passport started after unreadable files cannot save.");
        });
    }

    // Files this version must not change: a newer format, and one it cannot open.
    private static void Untouched(Action<bool, string> check)
    {
        InDirectory(directory =>
        {
            var store = new StampStore(directory);
            var path = store.PathFor(Profile);
            Directory.CreateDirectory(directory);
            var newer = $$"""{ "format": "{{StampStore.Format}}", "version": {{StampStore.Version + 1}}, "stamps": [], "stickers": [] }""";
            File.WriteAllText(path, newer);
            using var storage = new StampPersistence(store, Profile, () => Issued);
            check(storage.Loaded == StampLoadOutcome.Newer && storage.State == StampSaveState.ReadOnly, "A newer stamp file is not left read-only.");
            storage.Collection.Collect(DiscoveryPreview.Postcard.Id, DiscoveryPreview.Postcard.Stamp);
            storage.Flush(Wait);
            check(File.ReadAllText(path) == newer && !File.Exists(path + ".bak") && storage.Collection.Stamps.Count == 1, "A newer stamp file was overwritten, or collecting stopped working.");
            check(StampStorageText.Count(1, storage).EndsWith("NOT SAVED", StringComparison.Ordinal), "An unsaved stamp is labelled saved.");
        });

        InDirectory(directory =>
        {
            var store = new StampStore(directory);
            var path = Seed(store, 1);
            var before = File.ReadAllBytes(path);
            StampLoad load;
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                load = store.Load(Profile);
            check(load.Outcome == StampLoadOutcome.Inaccessible && load.Kept.Count == 0, "A locked stamp file is not reported as inaccessible.");
            check(File.ReadAllBytes(path).SequenceEqual(before), "A locked stamp file was changed.");
        });
    }

    // A crash between writing the temporary file and replacing the save.
    private static void Interrupted(Action<bool, string> check)
    {
        InDirectory(directory =>
        {
            var store = new StampStore(directory);
            var path = Seed(store, 1);
            File.WriteAllText(path + ".tmp", StampStore.Serialize(Book(3)));
            var load = store.Load(Profile);
            check(load.Outcome == StampLoadOutcome.Loaded && load.Book.Stamps.Count == 3 && !File.Exists(path + ".tmp"), "A complete interrupted save is not finished on load.");
            check(StampStore.Parse(File.ReadAllText(path + ".bak")).Stamps.Count == 1, "Finishing an interrupted save loses the previous save.");
        });

        InDirectory(directory =>
        {
            var store = new StampStore(directory);
            var path = Seed(store, 1);
            var partial = StampStore.Serialize(Book(3));
            File.WriteAllText(path + ".tmp", partial[..(partial.Length / 3)]);
            var load = store.Load(Profile);
            check(load.Outcome == StampLoadOutcome.Loaded && load.Book.Stamps.Count == 1 && !File.Exists(path + ".tmp"), "An incomplete interrupted save is not dropped in favour of the intact save.");
        });
    }

    // Stamp files that parse as JSON but break the rules are unreadable, never partly loaded.
    private static void Invalid(Action<bool, string> check)
    {
        var valid = StampStore.Serialize(Book(2));
        check(StampStore.Parse(valid).SameAs(Book(2)), "A stamp file does not round-trip.");
        foreach (var (name, text) in new[]
        {
            ("duplicate discovery", valid.Replace("\"demo-discovery-1\"", "\"demo-discovery-0\"")),
            ("duplicate stamp", valid.Replace("\"local-demo-0002\"", "\"local-demo-0001\"")),
            ("revision zero", valid.Replace("\"revision\": 1", "\"revision\": 0")),
            ("blank design", valid.Replace("\"demo-design-0\"", "\"  \"")),
            ("other provenance", valid.Replace("\"provenance\": \"local-demo\"", "\"provenance\": \"official\"")),
            ("bad date", valid.Replace("2026-10-19T18:30:00.0000000+02:00", "yesterday")),
            ("missing stamps", $$"""{ "format": "{{StampStore.Format}}", "version": 1 }"""),
            ("version zero", valid.Replace($"\"version\": {StampStore.Version}", "\"version\": 0")),
            ("array", "[]"),
            ("placement off the page", valid.Replace("\"x\": 0.25", "\"x\": 1.5")),
            ("placement before the first page", valid.Replace("\"page\": 0", "\"page\": -1")),
            ("placement past the last page", valid.Replace("\"page\": 0", $"\"page\": {StampPlacement.MaximumSheet + 1}")),
            ("placement of an unknown stamp", valid.Replace("\"stamp\": \"local-demo-0001\"", "\"stamp\": \"local-demo-0099\"")),
            ("two placements for one stamp", valid.Replace("\"stamp\": \"local-demo-0002\"", "\"stamp\": \"local-demo-0001\"")),
            ("missing placements", valid.Replace("\"placements\"", "\"elsewhere\"")),
        })
        {
            check(text != valid, name + ": the damaged fixture is unchanged.");
            var thrown = false;
            try { StampStore.Parse(text); }
            catch (FormatException) { thrown = true; }
            check(thrown, name + ": an invalid stamp file was accepted.");
        }
        try { _ = new StampCollection(null, [.. Stamps(1), Stamps(1)[0] with { Id = "local-demo-0009" }]); check(false, "A restored collection accepts two stamps for one discovery."); }
        catch (ArgumentException) { }
    }

    private static void Profiles(Action<bool, string> check)
    {
        InDirectory(directory =>
        {
            var store = new StampStore(directory);
            store.Save("first", Book(1));
            store.Save("second", Book(2));
            check(store.PathFor("first") != store.PathFor("second"), "Two profiles share a file.");
            check(store.Load("first").Book.Stamps.Count == 1 && store.Load("second").Book.Stamps.Count == 2 && store.Load("third").Outcome == StampLoadOutcome.New, "Profiles do not keep separate stamps.");
        });
        foreach (var profile in new[] { "", " ", "../local", "Local", "local/other", "-local", new string('a', 65) })
        {
            try { new StampStore(Path.GetTempPath()).PathFor(profile); check(false, $"The profile '{profile}' was accepted."); }
            catch (ArgumentException) { }
        }
    }

    // Saving fails (the directory is a file), is reported, and a retry succeeds once it can.
    private static void Failure(Action<bool, string> check)
    {
        InDirectory(root =>
        {
            Directory.CreateDirectory(root);
            var directory = Path.Combine(root, "stamps");
            File.WriteAllText(directory, "not a directory");
            var reported = 0;
            using var storage = new StampPersistence(new StampStore(directory), Profile, () => Issued, _ => Interlocked.Increment(ref reported));
            storage.Collection.Collect(DiscoveryPreview.Postcard.Id, DiscoveryPreview.Postcard.Stamp);
            check(storage.Flush(Wait) && storage.State == StampSaveState.Failed && reported == 1, "A failed save is not reported.");
            check(storage.Collection.Stamps.Count == 1 && StampStorageText.Kept(storage).Contains("could not be saved", StringComparison.Ordinal), "A failed save loses the stamp or claims it was saved.");
            File.Delete(directory);
            storage.Retry();
            check(storage.Flush(Wait) && storage.State == StampSaveState.Saved && new StampStore(directory).Load(Profile).Book.Stamps.Count == 1, "Retrying a failed save does not save.");
        });
    }

    // A representative larger collection: save, load, and the cost of each, off the UI thread.
    private static void Larger(Action<bool, string> check)
    {
        InDirectory(directory =>
        {
            var store = new StampStore(directory);
            var stamps = Book(500);
            var watch = Stopwatch.StartNew();
            store.Save(Profile, stamps);
            var saved = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var load = store.Load(Profile);
            var loaded = watch.Elapsed.TotalMilliseconds;
            check(load.Outcome == StampLoadOutcome.Loaded && load.Book.SameAs(stamps), "Five hundred stamps do not round-trip.");
            var size = new FileInfo(store.PathFor(Profile)).Length;
            Console.WriteLine($"INFO stamp-storage 500 stamps: {size / 1024} KiB, save {saved:F1} ms, load {loaded:F1} ms");
        });
    }

    // Placements round-trip with their page, place, turn, and layer; only placed stamps are written, so a
    // stamp on page 26 of an otherwise empty passport is one entry, not 26 pages.
    private static void Placements(Action<bool, string> check)
    {
        var book = Book(3);
        check(StampStore.Parse(StampStore.Serialize(book)).SameAs(book), "Placements do not round-trip.");
        var stamps = Stamps(2);
        var sparse = new StampBook(stamps, [new PlacedStamp(stamps[1], new StampPlacement(21, 0.3f, 0.7f, -0.4f))]);
        var text = StampStore.Serialize(sparse);
        var parsed = StampStore.Parse(text);
        check(parsed.SameAs(sparse) && parsed.Placements.Count == 1 && parsed.Placements[0].Placement.Sheet == 21, "A lone stamp far into the book does not round-trip on its page.");
        check(System.Text.Json.Nodes.JsonNode.Parse(text)!["placements"]!.AsArray().Count == 1, "Empty pages or unplaced stamps are written to the file.");
        InDirectory(directory =>
        {
            var store = new StampStore(directory);
            store.Save(Profile, sparse);
            using var storage = new StampPersistence(store, Profile, () => Issued);
            check(storage.Collection.Unplaced.Single() == stamps[0] && storage.Collection.LastSheet == 21, "A reloaded passport loses which stamps wait and where the placed one is.");
            check(storage.Collection.Place(stamps[0], new StampPlacement(21, 0.5f, 0.5f, MathF.PI * 3)) && storage.Flush(Wait), "Placing a waiting stamp is not saved.");
            var saved = store.Load(Profile).Book;
            check(saved.Placements.Select(entry => entry.Stamp.Id).SequenceEqual([stamps[1].Id, stamps[0].Id]) && MathF.Abs(MathF.Abs(saved.Placements[1].Placement.Rotation) - MathF.PI) < 1e-3f,
                "Layer order or a normalized turn is not saved.");
        });
    }

    // A version 1 file (from before placements) loads with its stamps waiting to be placed, and is written
    // as version 2 by the next save, with the version 1 file kept as the backup.
    private static void Migration(Action<bool, string> check)
    {
        const string Version1 = """
            {
              "format": "wanderveil.passport-stamps",
              "version": 1,
              "stamps": [
                {
                  "id": "local-demo-0001",
                  "discoveryId": "demo-scenic-route",
                  "collectedAt": "2026-10-19T18:30:00.0000000+02:00",
                  "provenance": "local-demo",
                  "design": { "id": "demo-scenic-route-stamp", "revision": 1, "name": "Scenic route", "artwork": 0, "provenance": "local-demo" }
                }
              ]
            }
            """;
        var book = StampStore.Parse(Version1);
        check(book.Stamps.Count == 1 && book.Placements.Count == 0 && book.Stamps[0].Source == null && book.Stamps[0].Design.Creator == null, "A version 1 file does not migrate to unplaced stamps.");
        InDirectory(directory =>
        {
            var store = new StampStore(directory);
            Directory.CreateDirectory(directory);
            var path = store.PathFor(Profile);
            File.WriteAllText(path, Version1);
            using var storage = new StampPersistence(store, Profile, () => Issued);
            check(storage.Loaded == StampLoadOutcome.Loaded && storage.Collection.Unplaced.Count() == 1, "A version 1 save does not load.");
            storage.Collection.Place(storage.Collection.Stamps[0], new StampPlacement(0, 0.5f, 0.5f, 0f));
            check(storage.Flush(Wait) && System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!["version"]!.GetValue<int>() == StampStore.Version, "A migrated save is not written as the current version.");
            check(File.ReadAllText(path + ".bak") == Version1, "Migrating does not keep the version 1 file as the backup.");
        });
    }

    private static StampBook Book(int count)
    {
        var stamps = Stamps(count);
        // Spread out and sparse: stamp i on page 3i, most pages in between empty.
        return new StampBook(stamps, stamps.Select((stamp, index) => new PlacedStamp(stamp, new StampPlacement(index * 3, index % 2 == 0 ? 0.25f : 0.75f, 0.4f, StampPlacement.Normalize(index * 0.1f)))).ToList());
    }

    private static string Seed(StampStore store, int saves)
    {
        for (var count = 1; count <= saves; count++)
            store.Save(Profile, Book(count));
        return store.PathFor(Profile);
    }

    private static List<StampInstance> Stamps(int count) => Enumerable.Range(0, count)
        .Select(index => new StampInstance($"local-demo-{index + 1:D4}", new StampDesign($"demo-design-{index}", 1, $"Demo stamp {index + 1} / ✦ 遠回り", index % 3, StampProvenance.LocalDemo), $"demo-discovery-{index}", Issued, StampProvenance.LocalDemo))
        .ToList();

    private static void InDirectory(Action<string> run)
    {
        var directory = Path.Combine(Path.GetTempPath(), "wanderveil-checks-" + Guid.NewGuid().ToString("N"));
        try { run(directory); }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
