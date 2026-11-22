// Packs a published runtime folder into a release archive: .zip for Windows,
// .tar.gz for macOS/Linux with the executable bit set (Windows' tar.exe drops it,
// so the downloaded app would not start without a chmod). On macOS the executable
// goes into an Ascolotl.app bundle, so Finder starts it as an app with its icon;
// Linux archives carry a desktop entry and its icon for the applications menu.
// Usage: dotnet run tools/package/pack.cs -- <publish-dir> <output.zip|output.tar.gz> [<version> <icons-dir>]
// (macOS and Linux archives need the version and the folder holding icon-<size>.png.)
using System.Buffers.Binary;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

if (args.Length is not (2 or 4))
{
    Console.Error.WriteLine("usage: pack.cs <publish-dir> <output.zip|output.tar.gz> [<version> <icons-dir>]");
    return 2;
}
var (dir, output) = (args[0], args[1]);
var mac = Path.GetFileName(output).Contains("-osx-");
var linux = Path.GetFileName(output).Contains("-linux-");
if ((mac || linux) && args.Length != 4)
{
    Console.Error.WriteLine("a macOS or Linux archive needs <version> <icons-dir>");
    return 2;
}
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

void Write(string name, Stream data, bool executable, DateTime modified) =>
    tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
    {
        Mode = executable ? Executable : Regular,
        ModificationTime = modified,
        DataStream = data,
    });

foreach (var path in Directory.GetFiles(dir).Order())
{
    var name = Path.GetFileName(path);
    // The app binary has no extension; native libraries and data do.
    var executable = !Path.HasExtension(name);
    using var data = File.OpenRead(path);
    Write(mac && executable ? $"Ascolotl.app/Contents/MacOS/{name}" : name, data, executable, File.GetLastWriteTimeUtc(path));
}

if (mac)
{
    var now = DateTime.UtcNow;
    using var plist = new MemoryStream(Encoding.UTF8.GetBytes(InfoPlist(args[2]).ReplaceLineEndings("\n")));
    Write("Ascolotl.app/Contents/Info.plist", plist, false, now);
    using var icns = new MemoryStream(Icns(args[3]));
    Write("Ascolotl.app/Contents/Resources/Ascolotl.icns", icns, false, now);
}
if (linux)
{
    var now = DateTime.UtcNow;
    using var desktop = new MemoryStream(Encoding.UTF8.GetBytes(DesktopEntry().ReplaceLineEndings("\n")));
    Write("ascolotl.desktop", desktop, false, now);
    using var icon = File.OpenRead(Path.Combine(args[3], "icon-256.png"));
    Write("ascolotl.png", icon, false, now);
}
return 0;

// StartupWMClass is the X11 window class Avalonia sets: the executable's name.
static string DesktopEntry() => """
    [Desktop Entry]
    Type=Application
    Name=Ascolotl
    GenericName=Document Editor
    Comment=AsciiDoc and Markdown editor with live preview and export
    Exec=Ascolotl %F
    Icon=ascolotl
    Terminal=false
    Categories=Office;TextEditor;
    MimeType=text/markdown;text/x-markdown;text/asciidoc;text/x-asciidoc;
    StartupWMClass=Ascolotl

    """;

static string InfoPlist(string version) => $"""
    <?xml version="1.0" encoding="UTF-8"?>
    <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
    <plist version="1.0">
    <dict>
      <key>CFBundleName</key><string>Ascolotl</string>
      <key>CFBundleDisplayName</key><string>Ascolotl</string>
      <key>CFBundleIdentifier</key><string>io.github.sonatique.ascolotl</string>
      <key>CFBundleVersion</key><string>{version}</string>
      <key>CFBundleShortVersionString</key><string>{version}</string>
      <key>CFBundlePackageType</key><string>APPL</string>
      <key>CFBundleExecutable</key><string>Ascolotl</string>
      <key>CFBundleIconFile</key><string>Ascolotl.icns</string>
      <key>NSHighResolutionCapable</key><true/>
      <key>LSApplicationCategoryType</key><string>public.app-category.productivity</string>
      <key>CFBundleDocumentTypes</key>
      <array>
        <dict>
          <key>CFBundleTypeName</key><string>AsciiDoc document</string>
          <key>CFBundleTypeRole</key><string>Editor</string>
          <key>LSHandlerRank</key><string>Alternate</string>
          <key>CFBundleTypeExtensions</key><array><string>adoc</string><string>asciidoc</string></array>
        </dict>
        <dict>
          <key>CFBundleTypeName</key><string>Markdown document</string>
          <key>CFBundleTypeRole</key><string>Editor</string>
          <key>LSHandlerRank</key><string>Alternate</string>
          <key>CFBundleTypeExtensions</key><array><string>md</string><string>markdown</string></array>
        </dict>
      </array>
    </dict>
    </plist>
    """;

// An .icns file: "icns" and its length, then per image a type, its length (both
// big-endian, the length counting this 8-byte header) and the PNG. The @2x types
// reuse the larger PNGs.
static byte[] Icns(string iconsDir)
{
    (string Type, int Size)[] images =
    [
        ("icp4", 16), ("icp5", 32), ("ic11", 32), ("ic12", 64), ("ic07", 128),
        ("ic08", 256), ("ic13", 256), ("ic09", 512), ("ic14", 512), ("ic10", 1024),
    ];
    using var body = new MemoryStream();
    foreach (var (type, size) in images)
    {
        var png = File.ReadAllBytes(Path.Combine(iconsDir, $"icon-{size}.png"));
        body.Write(Encoding.ASCII.GetBytes(type));
        body.Write(BigEndian(png.Length + 8));
        body.Write(png);
    }
    using var icns = new MemoryStream();
    icns.Write("icns"u8);
    icns.Write(BigEndian((int)body.Length + 8));
    body.WriteTo(icns);
    return icns.ToArray();
}

static byte[] BigEndian(int value)
{
    var bytes = new byte[4];
    BinaryPrimitives.WriteInt32BigEndian(bytes, value);
    return bytes;
}
