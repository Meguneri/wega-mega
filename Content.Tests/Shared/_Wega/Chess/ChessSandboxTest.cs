using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NUnit.Framework;

namespace Content.Tests.Shared._Wega.Chess;

/// <summary>
/// Страж клиентской песочницы для шахмат.
///
/// Content.Shared проверяется IL-вайтлистом при СТАРТЕ клиента, а не при сборке: `dotnet build`
/// проходит, клиент падает с «Assembly Content.Shared failed type checks». Уже наступали:
/// «строка + char» и интерполяция с char компилируются в string.Concat со span-конструктором,
/// а тип System.ReadOnlySpan песочницей запрещён целиком.
///
/// Тест читает метаданные собранной сборки и ищет ссылки на конструктор ReadOnlySpan в шахматных
/// типах. Дешевле, чем ловить это запуском клиента.
/// </summary>
[TestFixture]
public sealed class ChessSandboxTest
{
    [Test]
    public void ChessTypesDoNotConstructSpans()
    {
        var path = FindSharedAssembly();
        if (path == null)
        {
            Assert.Ignore("Content.Shared.dll не найдена — тест имеет смысл только после сборки");
            return;
        }

        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        // Ссылки на .ctor у ReadOnlySpan<T>/Span<T> — ровно то, что валит песочницу.
        var forbidden = new List<string>();
        foreach (var handle in reader.MemberReferences)
        {
            var member = reader.GetMemberReference(handle);
            var name = reader.GetString(member.Name);
            if (name != ".ctor")
                continue;

            if (member.Parent.Kind != HandleKind.TypeReference)
                continue;

            var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
            var typeName = reader.GetString(type.Name);
            if (typeName.StartsWith("ReadOnlySpan") || typeName.StartsWith("Span"))
                forbidden.Add(typeName);
        }

        Assert.That(forbidden, Is.Empty,
            "Content.Shared конструирует Span/ReadOnlySpan — клиент упадёт на проверке песочницы. "
            + "Обычная причина: склейка «строка + char» или интерполяция с char. "
            + $"Найдено: {string.Join(", ", forbidden.Distinct())}");
    }

    private static string? FindSharedAssembly()
    {
        // Тест запускается из bin/Content.Tests; сборка лежит рядом либо в bin/Content.Client.
        var candidates = new[]
        {
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Content.Shared.dll"),
            Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "Content.Client", "Content.Shared.dll"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
