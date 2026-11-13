using Ascolotl.Core.Models;
using Ascolotl.Core.ViewModels;

namespace Ascolotl.Tests.Unit.ViewModels;

public sealed class CommandPaletteViewModelTests
{
    private static List<CommandEntry> CreateTestCommands()
    {
        return
        [
            new("New Document", "Ctrl+N", "File", () => { }),
            new("Open Document", "Ctrl+O", "File", () => { }),
            new("Save", "Ctrl+S", "File", () => { }),
            new("Toggle Outline", "Ctrl+B", "View", () => { }),
            new("Zoom In", "Ctrl++", "View", () => { }),
        ];
    }

    [Fact]
    public void Constructor_PopulatesFilteredCommands()
    {
        var vm = new CommandPaletteViewModel(CreateTestCommands());

        Assert.Equal(5, vm.FilteredCommands.Count);
    }

    [Fact]
    public void Open_SetsIsOpenTrue()
    {
        var vm = new CommandPaletteViewModel(CreateTestCommands());

        vm.Open();

        Assert.True(vm.IsOpen);
    }

    [Fact]
    public void Open_ClearsFilter()
    {
        var vm = new CommandPaletteViewModel(CreateTestCommands());
        vm.FilterText = "something";

        vm.Open();

        Assert.Equal(string.Empty, vm.FilterText);
    }

    [Fact]
    public void Open_ResetsSelectedIndex()
    {
        var vm = new CommandPaletteViewModel(CreateTestCommands());
        vm.SelectedIndex = 3;

        vm.Open();

        Assert.Equal(0, vm.SelectedIndex);
    }

    [Fact]
    public void Close_SetsIsOpenFalse()
    {
        var vm = new CommandPaletteViewModel(CreateTestCommands());
        vm.Open();

        vm.Close();

        Assert.False(vm.IsOpen);
    }

    [Fact]
    public void FilterText_FiltersByName()
    {
        var vm = new CommandPaletteViewModel(CreateTestCommands());

        vm.FilterText = "doc";

        Assert.Equal(2, vm.FilteredCommands.Count);
        Assert.Contains(vm.FilteredCommands, c => c.Name == "New Document");
        Assert.Contains(vm.FilteredCommands, c => c.Name == "Open Document");
    }

    [Fact]
    public void FilterText_CaseInsensitive()
    {
        var vm = new CommandPaletteViewModel(CreateTestCommands());

        vm.FilterText = "ZOOM";

        Assert.Single(vm.FilteredCommands);
        Assert.Equal("Zoom In", vm.FilteredCommands[0].Name);
    }

    [Fact]
    public void FilterText_ResetsSelectedIndex()
    {
        var vm = new CommandPaletteViewModel(CreateTestCommands());
        vm.SelectedIndex = 3;

        vm.FilterText = "save";

        Assert.Equal(0, vm.SelectedIndex);
    }

    [Fact]
    public void MoveDown_IncrementsIndex()
    {
        var vm = new CommandPaletteViewModel(CreateTestCommands());
        Assert.Equal(0, vm.SelectedIndex);

        vm.MoveDown();

        Assert.Equal(1, vm.SelectedIndex);
    }

    [Fact]
    public void MoveDown_ClampsAtEnd()
    {
        var vm = new CommandPaletteViewModel(CreateTestCommands());
        vm.SelectedIndex = 4;

        vm.MoveDown();

        Assert.Equal(4, vm.SelectedIndex);
    }

    [Fact]
    public void MoveUp_DecrementsIndex()
    {
        var vm = new CommandPaletteViewModel(CreateTestCommands());
        vm.SelectedIndex = 2;

        vm.MoveUp();

        Assert.Equal(1, vm.SelectedIndex);
    }

    [Fact]
    public void MoveUp_ClampsAtZero()
    {
        var vm = new CommandPaletteViewModel(CreateTestCommands());

        vm.MoveUp();

        Assert.Equal(0, vm.SelectedIndex);
    }

    [Fact]
    public void ExecuteSelected_RunsCommand()
    {
        var executed = false;
        var commands = new List<CommandEntry>
        {
            new("Test", null, "Test", () => executed = true),
        };
        var vm = new CommandPaletteViewModel(commands);
        vm.Open();

        vm.ExecuteSelected();

        Assert.True(executed);
    }

    [Fact]
    public void ExecuteSelected_ClosePalette()
    {
        var commands = new List<CommandEntry>
        {
            new("Test", null, "Test", () => { }),
        };
        var vm = new CommandPaletteViewModel(commands);
        vm.Open();

        vm.ExecuteSelected();

        Assert.False(vm.IsOpen);
    }

    [Fact]
    public void Filter_ExcludesDisabledCommands()
    {
        var commands = new List<CommandEntry>
        {
            new("Enabled", null, "Test", () => { }, () => true),
            new("Disabled", null, "Test", () => { }, () => false),
        };
        var vm = new CommandPaletteViewModel(commands);

        Assert.Single(vm.FilteredCommands);
        Assert.Equal("Enabled", vm.FilteredCommands[0].Name);
    }
}
