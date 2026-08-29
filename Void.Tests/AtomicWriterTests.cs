using System;
using System.IO;
using System.Text;
using Void;
using Xunit;

namespace Void.Tests;

/// <summary>
/// Atomic-write and stale-temp-cleanup coverage for VOID-007
/// (save-format-spec §10.1). Every test gets its own temp directory and
/// removes it afterwards; nothing here depends on the working directory.
/// </summary>
public sealed class AtomicWriterTests : IDisposable
{
    private readonly string _root;

    public AtomicWriterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "void-save-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Leftovers in the OS temp dir are harmless.
        }
    }

    private string Path_(params string[] parts) => Path.Combine(_root, Path.Combine(parts));

    [Fact]
    public void CreatesMissingDirectoriesAndWritesBytes()
    {
        string target = Path_("campaigns", "abc", "worlds", "def", "chunks", "0_0.chunk");
        byte[] bytes = Encoding.UTF8.GetBytes("hello");

        AtomicWriter.Write(target, bytes);

        Assert.True(File.Exists(target));
        Assert.Equal(bytes, File.ReadAllBytes(target));
        Assert.False(File.Exists(target + AtomicWriter.TempSuffix));
    }

    [Fact]
    public void FailureMidWriteLeavesPreviousFileIntactAndNoTempBehind()
    {
        string target = Path_("world.manifest");
        byte[] original = Encoding.UTF8.GetBytes("original contents");
        AtomicWriter.Write(target, original);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
            AtomicWriter.Write(target, stream =>
            {
                stream.Write(Encoding.UTF8.GetBytes("partial garbage"));
                throw new InvalidOperationException("simulated crash");
            }));

        Assert.Equal("simulated crash", thrown.Message);
        Assert.Equal(original, File.ReadAllBytes(target));
        Assert.False(File.Exists(target + AtomicWriter.TempSuffix));
    }

    [Fact]
    public void TargetIsNeverObservedPartial()
    {
        string target = Path_("chunks", "1_1.chunk");
        byte[] first = new byte[8192];
        Array.Fill(first, (byte)0x11);
        AtomicWriter.Write(target, first);

        byte[] second = new byte[16384];
        Array.Fill(second, (byte)0x22);

        AtomicWriter.Write(target, stream =>
        {
            // Mid-write the target must still be the complete previous version.
            byte[] observed = File.ReadAllBytes(target);
            Assert.Equal(first, observed);
            stream.Write(second);
        });

        Assert.Equal(second, File.ReadAllBytes(target));
    }

    [Fact]
    public void StaleTempFilesAreCleanedAndFreshOnesSpared()
    {
        string stale = Path_("chunks", "0_0.chunk" + AtomicWriter.TempSuffix);
        string fresh = Path_("chunks", "0_1.chunk" + AtomicWriter.TempSuffix);
        string keeper = Path_("chunks", "0_2.chunk");

        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllBytes(stale, new byte[] { 1 });
        File.WriteAllBytes(fresh, new byte[] { 2 });
        File.WriteAllBytes(keeper, new byte[] { 3 });

        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow - TimeSpan.FromHours(2));

        int deleted = AtomicWriter.CleanStaleTempFiles(_root, TimeSpan.FromMinutes(30));

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(keeper));
    }

    [Fact]
    public void CleanupOnMissingRootIsANoOp()
    {
        Assert.Equal(0, AtomicWriter.CleanStaleTempFiles(Path_("nope"), TimeSpan.Zero));
    }

    [Fact]
    public void SaveAndLoadRoundTripThroughDisk()
    {
        string target = Path_("characters", "hero.character");
        byte[] payload = Encoding.UTF8.GetBytes("character payload placeholder");

        SaveEnvelope written = SaveFile.Save(target, SaveFileKind.Character, 1, 0x1234UL, payload);
        SaveLoadResult result = SaveFile.Load(target);

        Assert.True(result.IntegrityOk);
        Assert.Equal(payload, result.Payload);
        Assert.Equal(written.FileSalt, result.Envelope.FileSalt);
        Assert.Equal(SaveFileKind.Character, result.Envelope.FileKind);
    }

    [Fact]
    public void OverwritingAnExistingSaveKeepsItLoadable()
    {
        string target = Path_("chunks", "2_2.chunk");
        byte[] first = Encoding.UTF8.GetBytes("first version");
        byte[] second = Encoding.UTF8.GetBytes("second version, longer than the first");

        SaveFile.Save(target, SaveFileKind.Chunk, 1, 7UL, first);
        SaveFile.Save(target, SaveFileKind.Chunk, 1, 7UL, second);

        Assert.Equal(second, SaveFile.Load(target).Payload);
        Assert.False(File.Exists(target + AtomicWriter.TempSuffix));
    }
}
