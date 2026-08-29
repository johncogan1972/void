using System;
using System.Collections.Generic;
using System.IO;

namespace Void;

/// <summary>
/// Temp-and-rename file writer (save-format-spec §10.1). Every save file goes
/// through here so that a crash mid-write leaves the previous version intact:
/// the bytes land in <c>&lt;target&gt;.tmp</c>, are flushed to the platter,
/// and only then replace the target with a single rename.
///
/// Node- and engine-free by design; it is plain <c>System.IO</c> so the format
/// stays testable outside Godot.
/// </summary>
public static class AtomicWriter
{
    /// <summary>Suffix appended to the target path for the in-progress file.</summary>
    public const string TempSuffix = ".tmp";

    /// <summary>Writes <paramref name="bytes"/> to <paramref name="path"/> atomically.</summary>
    public static void Write(string path, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(path);

        // A local copy so the span does not have to survive into the lambda.
        byte[] copy = bytes.ToArray();
        Write(path, stream => stream.Write(copy, 0, copy.Length));
    }

    /// <summary>
    /// Writes whatever <paramref name="writeBody"/> emits to
    /// <paramref name="path"/> atomically. The parent directory is created if
    /// missing. If <paramref name="writeBody"/> or any I/O step throws, the
    /// temp file is deleted and the exception is rethrown, leaving any existing
    /// target untouched.
    /// </summary>
    public static void Write(string path, Action<Stream> writeBody)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(writeBody);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = path + TempSuffix;

        try
        {
            using (FileStream stream = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                writeBody(stream);

                // Flush(true) pushes through the OS buffers to the device, so
                // the rename below cannot expose a file whose contents are
                // still in flight.
                stream.Flush(true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Crash cleanup (§10.1 step 5). Deletes every <c>*.tmp</c> under
    /// <paramref name="root"/>, recursively, whose last write time is older
    /// than <paramref name="olderThan"/>. Fresh temp files — a save may be in
    /// flight in another thread — are left alone.
    ///
    /// Individual delete failures are tolerated: a locked or vanished temp file
    /// must not abort startup. Returns the number of files actually deleted.
    /// </summary>
    public static int CleanStaleTempFiles(string root, TimeSpan olderThan)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (!Directory.Exists(root))
        {
            return 0;
        }

        DateTime cutoff = DateTime.UtcNow - olderThan;
        int deleted = 0;

        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(root, "*" + TempSuffix, SearchOption.AllDirectories);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        foreach (string file in candidates)
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) > cutoff)
                {
                    continue;
                }

                File.Delete(file);
                deleted++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Locked, in use, or already gone. Next startup can try again.
            }
        }

        return deleted;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort: CleanStaleTempFiles will sweep it later.
        }
    }
}
