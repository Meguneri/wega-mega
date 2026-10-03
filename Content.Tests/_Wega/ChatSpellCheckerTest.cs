using System;
using System.IO;
using System.Linq;
using Content.Client._Wega.Chat;
using NUnit.Framework;

namespace Content.Tests._Wega;

[TestFixture]
public sealed class ChatSpellCheckerTest
{
    private static ChatSpellChecker Checker => new(new[] { "привет", "персонаж", "пишет", "еще" }
        .Select(ChatSpellChecker.Fingerprint).OrderBy(x => x).ToArray());

    [Test]
    public void RecognizesCaseAndYo()
    {
        Assert.That(Checker.IsKnown("ПРИВЕТ"), Is.True);
        Assert.That(Checker.IsKnown("ещё"), Is.True);
        Assert.That(Checker.IsKnown("превет"), Is.False);
    }

    [Test]
    public void FindsCompletedMistakeAndWaitsForCurrentWord()
    {
        Assert.That(Checker.FindErrors("превет пишет", 12), Is.EqualTo(new[] { (0, 6) }));
        Assert.That(Checker.FindErrors("превет", 6), Is.Empty);
        Assert.That(Checker.FindErrors("превет ", 7), Is.EqualTo(new[] { (0, 6) }));
    }

    [TestCase("/превет аргумент ")]
    [TestCase("персонаж Джиневра AFK https://пример.рф ")]
    [TestCase("эмоут слаймом вульпканином ")]
    [TestCase("персонаж abcпревет123 ")]
    public void IgnoresCommandsNamesLinksAndGameTerms(string text)
    {
        Assert.That(Checker.FindErrors(text, text.Length), Is.Empty);
    }

    [Test]
    public void HandlesMaximumChatLength()
    {
        var text = new string('я', 16384) + " ";
        Assert.That(Checker.FindErrors(text, text.Length), Is.EqualTo(new[] { (0, 16384) }));
    }

    [Test]
    public void RealDictionaryRecognizesHyphenatedWordsAndFindsTypos()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "Resources", "_Wega", "Spelling")))
            root = root.Parent;
        Assert.That(root, Is.Not.Null);
        using var reader = new BinaryReader(File.OpenRead(Path.Combine(root!.FullName,
            "Resources", "_Wega", "Spelling", "russian.bin")));
        var words = new ulong[reader.ReadInt32()];
        for (var i = 0; i < words.Length; i++)
            words[i] = reader.ReadUInt64();
        var checker = new ChatSpellChecker(words);
        foreach (var word in new[] { "что-то", "кто-нибудь", "из-за", "по-русски", "что-либо", "ещё", "персонажами" })
            Assert.That(checker.IsKnown(word), Is.True, word);
        Assert.That(checker.FindErrors("что-то кто-нибудь из-за по-русски ", 33), Is.Empty);
        Assert.That(checker.FindErrors("чтто-то ", 8), Is.EqualTo(new[] { (0, 7) }));
    }
}
