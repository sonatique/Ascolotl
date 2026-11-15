using Ascolotl.App.Services;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace Ascolotl.Tests.Unit.Ui;

public sealed class AvaloniaFileServiceTests
{
    [AvaloniaFact]
    public async Task ReadFile_LockedByAnotherProgram_ReturnsNullInsteadOfThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ascolotl-locked-{Guid.NewGuid():N}.adoc");
        File.WriteAllText(path, "= Locked");
        try
        {
            var service = new AvaloniaFileService(new Window());
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.Null(await service.ReadFileAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
