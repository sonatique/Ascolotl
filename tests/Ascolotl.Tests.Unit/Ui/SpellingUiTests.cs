namespace Ascolotl.Tests.Unit.Ui;

using Ascolotl.App.Views.Assist;
using Ascolotl.Core.Models;
using Ascolotl.Core.Resources;
using Ascolotl.Core.Spelling;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit;
using WeCantSpell.Hunspell;
using static PreviewEditingTests;

public class SpellingUiTests
{
    /// <summary>Lets the half-second pause and the background check run.</summary>
    private static void WaitFor(Window window, Func<bool> done)
    {
        for (var waited = 0; !done() && waited < 5000; waited += 50)
        {
            Pump(window);
            Thread.Sleep(50);
        }
        Pump(window);
    }

    [AvaloniaFact]
    public void Misspellings_AreMarked_AndTheMenuOffersTheirSuggestions()
    {
        SpellCheck.Enabled = true; // off where the app hasn't read its setting
        try
        {
            Speller.Use("xx", new Speller(WordList.CreateFromWords(["Doc", "some", "words", "here", "hello", "world"])));
            var (window, vm) = Open("= Doc\n:lang: xx\n\nSome wrods here, `codde`, helo world.\n", EditorMode.Source);
            var source = window.GetVisualDescendants().OfType<TextEditor>().First(t => t.IsEffectivelyVisible);
            var spelling = CompletionController.For(source)!.Spelling;

            WaitFor(window, () => spelling.Marked.Any());
            Assert.Equal(["wrods", "helo"], spelling.Marked); // not the attribute entry, nor the code

            source.CaretOffset = source.Text.IndexOf("helo", StringComparison.Ordinal) + 1;
            var items = spelling.MenuItems();
            var suggestion = Assert.IsType<MenuItem>(items[0]);
            Assert.Equal("hello", suggestion.Header);
            Assert.Contains(items, i => i is MenuItem { Header: var h } && Equals(h, Strings.MenuAddToDictionary));
            suggestion.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Contains("hello world.", vm.SelectedDocument!.SourceText);
            Assert.Equal(["wrods"], spelling.Marked); // unmarked at once
            source.CaretOffset = source.Text.IndexOf("wrods", StringComparison.Ordinal);
            Assert.Empty(new SpellingController(source, () => false, () => "", _ => { }).MenuItems()); // not yet checked: nothing marked

            SpellCheck.Enabled = false; // turned off: the marks go
            Assert.Empty(spelling.Marked);
        }
        finally
        {
            SpellCheck.Enabled = false;
        }
    }

    [AvaloniaFact]
    public void ALanguageWithoutADictionary_IsSaidOnce()
    {
        SpellCheck.Enabled = true;
        try
        {
            var said = new List<string>();
            var (window, vm) = Open("= Doc\n", EditorMode.Source);
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.StatusText) && vm.StatusText is { } s) said.Add(s); };
            vm.SelectedDocument!.SourceText = "= Doc\n:lang: zz\n\nAnything.\n";
            var source = window.GetVisualDescendants().OfType<TextEditor>().First(t => t.IsEffectivelyVisible);
            var told = string.Format(Strings.StatusNoDictionary, "zz", "").Split(':')[0];
            WaitFor(window, () => said.Any(s => s.StartsWith(told, StringComparison.Ordinal)));
            Assert.Single(said, s => s.StartsWith(told, StringComparison.Ordinal));
            Assert.Empty(CompletionController.For(source)!.Spelling.Marked);
        }
        finally
        {
            SpellCheck.Enabled = false;
        }
    }
}
