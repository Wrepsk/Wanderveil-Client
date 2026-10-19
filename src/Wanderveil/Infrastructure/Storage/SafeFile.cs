using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Wanderveil.Features;

namespace Wanderveil.Infrastructure.Storage;

// One saved file that never loses its last good save. Writing goes to a temporary file, flushed to disk, which
// then replaces the old file in one step, keeping the previous save as <file>.bak; a crash mid-save leaves the
// old file whole. Loading never deletes anything it cannot read: unreadable files are renamed aside and kept.
// Blocking file I/O: call it from startup or a background task, never from a draw callback.
internal static class SafeFile
{
    // parse reads a file's text. It throws FormatException when the text is not a valid file of its kind and
    // NotSupportedException when it is a later version this one does not know. value is null unless the
    // outcome is Loaded or Recovered.
    internal static LoadOutcome Load<T>(string path, long maximumBytes, Func<string, T> parse, List<string> kept, out T? value) where T : class
    {
        value = null;
        try { return Recover(path, maximumBytes, parse, kept, out value); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Setting a file aside or restoring the backup failed: touch nothing more and save nothing.
            value = null;
            return LoadOutcome.Inaccessible;
        }
    }

    private static LoadOutcome Recover<T>(string path, long maximumBytes, Func<string, T> parse, List<string> kept, out T? value) where T : class
    {
        value = null;
        var backup = path + ".bak";
        // A leftover temporary file is a save interrupted before its final step. If it is complete it is the
        // newest save, so finish that step; otherwise drop it, since the file it was replacing is intact.
        var temporary = path + ".tmp";
        var interrupted = Read(temporary, maximumBytes, parse).Kind;
        if (interrupted == ReadKind.Ok)
        {
            if (File.Exists(path)) File.Replace(temporary, path, backup, ignoreMetadataErrors: true);
            else File.Move(temporary, path);
        }
        else if (interrupted == ReadKind.Unreadable) TryDelete(temporary);
        var main = Read(path, maximumBytes, parse);
        if (main.Kind == ReadKind.Ok) { value = main.Value; return LoadOutcome.Loaded; }
        if (main.Kind is ReadKind.Newer or ReadKind.Inaccessible) return Outcome(main.Kind);
        var previous = Read(backup, maximumBytes, parse);
        if (previous.Kind is ReadKind.Newer or ReadKind.Inaccessible) return Outcome(previous.Kind);
        if (main.Kind == ReadKind.Missing && previous.Kind == ReadKind.Missing) return LoadOutcome.New;
        if (main.Kind == ReadKind.Unreadable) kept.Add(SetAside(path));
        if (previous.Kind == ReadKind.Ok)
        {
            // Put the backup back as the saved file, so the next save backs it up in turn.
            File.Copy(backup, path, overwrite: false);
            value = previous.Value;
            return LoadOutcome.Recovered;
        }
        if (previous.Kind == ReadKind.Unreadable) kept.Add(SetAside(backup));
        return LoadOutcome.Unreadable;
    }

    internal static void Write(string path, ReadOnlySpan<byte> bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
        else File.Move(temporary, path);
    }

    internal static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private enum ReadKind { Missing, Ok, Unreadable, Newer, Inaccessible }

    private readonly record struct ReadResult<T>(ReadKind Kind, T? Value);

    private static ReadResult<T> Read<T>(string path, long maximumBytes, Func<string, T> parse) where T : class
    {
        string text;
        try
        {
            if (!File.Exists(path)) return new(ReadKind.Missing, null);
            if (new FileInfo(path).Length > maximumBytes) return new(ReadKind.Unreadable, null);
            text = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(ReadKind.Inaccessible, null);
        }
        try { return new(ReadKind.Ok, parse(text)); }
        catch (NotSupportedException) { return new(ReadKind.Newer, null); }
        catch (FormatException) { return new(ReadKind.Unreadable, null); }
    }

    private static LoadOutcome Outcome(ReadKind kind) => kind == ReadKind.Newer ? LoadOutcome.Newer : LoadOutcome.Inaccessible;

    // Renames an unreadable file aside, never over another, and returns where it went.
    private static string SetAside(string path)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var target = $"{path}.unreadable-{stamp}";
        for (var index = 2; File.Exists(target); index++) target = $"{path}.unreadable-{stamp}-{index}";
        File.Move(path, target);
        return target;
    }
}

// Profiles name files, so they are restricted to a safe, stable spelling.
internal static partial class StorageProfile
{
    internal static bool IsValid(string profile) => Pattern().IsMatch(profile);

    internal static string Checked(string profile) => IsValid(profile) ? profile : throw new ArgumentException("A profile is 1-64 lowercase letters, digits, or hyphens.", nameof(profile));

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex Pattern();
}
