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
    public void KeepsMistakesMarkedWhileTypingAndAddingSpaces()
    {
        Assert.That(Checker.FindErrors("превет"), Is.EqualTo(new[] { (0, 6) }));
        Assert.That(Checker.FindErrors("превет "), Is.EqualTo(new[] { (0, 6) }));
        Assert.That(Checker.FindErrors("превет пишет"), Is.EqualTo(new[] { (0, 6) }));
        Assert.That(Checker.FindErrors("превет пишет "), Is.EqualTo(new[] { (0, 6) }));
        Assert.That(Checker.FindErrors("превет пишет перевет"), Is.EqualTo(new[] { (0, 6), (13, 20) }));
        Assert.That(Checker.FindErrors("привет пишет перевет"), Is.EqualTo(new[] { (13, 20) }));
        Assert.That(Checker.FindErrors("привет пишет привет"), Is.Empty);
    }

    [TestCase("/превет аргумент ")]
    [TestCase("персонаж Джиневра AFK https://пример.рф ")]
    [TestCase("эмоут слаймом вульпканином ")]
    [TestCase("персонаж abcпревет123 ")]
    public void IgnoresCommandsNamesLinksAndGameTerms(string text)
    {
        Assert.That(Checker.FindErrors(text), Is.Empty);
    }

    [Test]
    public void HandlesMaximumChatLength()
    {
        var text = new string('я', 16384) + " ";
        Assert.That(Checker.FindErrors(text), Is.EqualTo(new[] { (0, 16384) }));
    }

    [TestCase("привет-")]
    [TestCase("привет--привет")]
    [TestCase("при\u0301вет")]
    [TestCase("при\u00adвет")]
    [TestCase("эмо\u0301ут")]
    [TestCase("УФО привет")]
    [TestCase("https://пример.рф/?слово=превет&ещё=перевет")]
    [TestCase("www.пример.рф/превет")]
    [TestCase("превет@пример.рф")]
    [TestCase("@превет")]
    public void IgnoresTypographyAbbreviationsAndWholeLinks(string text)
    {
        Assert.That(Checker.FindErrors(text), Is.Empty);
    }

    [TestCase("Превет", 0, 6)]
    [TestCase("привет. Превет", 8, 14)]
    [TestCase("привет! Превет", 8, 14)]
    [TestCase("привет? Превет", 8, 14)]
    [TestCase("привет ПРЕВЕТ", 7, 13)]
    [TestCase("пре\u0301вет", 0, 7)]
    public void KeepsActualTyposMarked(string text, int start, int end)
    {
        Assert.That(Checker.FindErrors(text), Is.EqualTo(new[] { (start, end) }));
    }

    [Test]
    public void ChecksTextFollowingALink()
    {
        const string text = "https://пример.рф/?слово=превет перевет";
        var start = text.LastIndexOf("перевет", StringComparison.Ordinal);
        Assert.That(Checker.FindErrors(text), Is.EqualTo(new[] { (start, start + 7) }));
    }

    [TestCase("привет-то", true)]
    [TestCase("пре\u0301вет-то", false)]
    [TestCase("превет-то", false)]
    [TestCase("привет-нибудь", false)]
    [TestCase("привет-либе", false)]
    [TestCase("привет-персонаж", false)]
    [TestCase("кое-привет", false)]
    [TestCase("привет-то-то", false)]
    public void ChecksHyphenatedBasesInsteadOfIgnoringAllCompounds(string word, bool expected)
    {
        Assert.That(Checker.IsKnown(word), Is.EqualTo(expected));
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
        foreach (var word in new[] { "что-то", "кто-нибудь", "из-за", "по-русски", "что-либо", "ещё", "персонажами",
                     "причиндал", "причиндала", "причиндалу", "причиндалом", "причиндале", "причиндалы",
                     "что\u2010то", "кто\u2011нибудь", "причинда\u0301л", "чем-то", "чём-то",
                     "кому-то", "чьему-либо", "какими-нибудь", "кое-чем", "кое-какими",
                     "ЧЕМ\u2011ТО", "че\u0301м-то", "причиндал-то" })
            Assert.That(checker.IsKnown(word), Is.True, word);
        Assert.That(checker.FindErrors("чем-то чьему-либо какими-нибудь кое-чем причиндал "), Is.Empty);
        Assert.That(checker.FindErrors("что-то кто-нибудь из-за по-русски "), Is.Empty);
        Assert.That(checker.FindErrors("чтто-то"), Is.EqualTo(new[] { (0, 7) }));
        Assert.That(checker.FindErrors("чтто-то "), Is.EqualTo(new[] { (0, 7) }));
    }
}
