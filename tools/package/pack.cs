// Packs a published runtime folder into a release archive: .zip for Windows,
// .tar.gz for macOS/Linux with the executable bit set (Windows' tar.exe drops it,
// so the downloaded app would not start without a chmod).
// Usage: dotnet run tools/package/pack.cs -- <publish-dir> <output.zip|output.tar.gz>
using System.Formats.Tar;
using System.IO.Compression;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: pack.cs <publish-dir> <output.zip|output.tar.gz>");
    return 2;
}
var (dir, output) = (args[0], args[1]);
File.Delete(output);

if (output.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
{
    ZipFile.CreateFromDirectory(dir, output, CompressionLevel.Optimal, includeBaseDirectory: false);
    return 0;
}

const UnixFileMode Executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
const UnixFileMode Regular = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

using var file = File.Create(output);
using var gzip = new GZipStream(file, CompressionLevel.Optimal);
using var tar = new TarWriter(gzip, TarEntryFormat.Pax);
foreach (var path in Directory.GetFiles(dir).Order())
{
    var name = Path.GetFileName(path);
    using var data = File.OpenRead(path);
    tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
    {
        // The app binary has no extension; native libraries and data do.
        Mode = Path.HasExtension(name) ? Regular : Executable,
        ModificationTime = File.GetLastWriteTimeUtc(path),
        DataStream = data,
    });
}
return 0;
