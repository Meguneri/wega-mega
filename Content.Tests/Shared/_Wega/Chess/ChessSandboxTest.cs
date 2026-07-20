#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NUnit.Framework;

namespace Content.Tests.Shared._Wega.Chess;

/// <summary>
/// Страж клиентской песочницы.
///
/// Content.Shared проверяется IL-вайтлистом при СТАРТЕ клиента, а не при сборке: `dotnet build`
/// молчит, а клиент падает с «Assembly Content.Shared failed type checks» и вообще не открывается.
/// Тип System.ReadOnlySpan запрещён целиком (в Sandbox.yml у него нет ни одного разрешённого
/// члена), поэтому любой его конструктор в общей сборке = неработающий клиент.
///
/// ВАЖНО: у обобщённых типов родитель ссылки на член — TypeSpecification, а не TypeReference.
/// Первая версия этого теста проверяла только TypeReference и молча «проходила» даже на
/// намеренно вставленном нарушении — бесполезный зелёный тест хуже отсутствующего.
/// </summary>
[TestFixture]
public sealed class ChessSandboxTest
{
    [Test]
    public void SharedAssemblyDoesNotConstructSpans()
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
        var provider = new TypeNameProvider();

        var forbidden = new List<string>();
        foreach (var handle in reader.MemberReferences)
        {
            var member = reader.GetMemberReference(handle);
            if (reader.GetString(member.Name) != ".ctor")
                continue;

            var owner = member.Parent.Kind switch
            {
                HandleKind.TypeReference =>
                    reader.GetString(reader.GetTypeReference((TypeReferenceHandle)member.Parent).Name),
                // Обобщённый владелец (ReadOnlySpan`1<char> и т.п.) — имя лежит в сигнатуре.
                HandleKind.TypeSpecification =>
                    reader.GetTypeSpecification((TypeSpecificationHandle)member.Parent)
                        .DecodeSignature(provider, null),
                _ => null,
            };

            if (owner != null && (owner.StartsWith("ReadOnlySpan") || owner.StartsWith("Span")))
                forbidden.Add(owner);
        }

        Assert.That(forbidden, Is.Empty,
            "Content.Shared конструирует Span/ReadOnlySpan — клиент упадёт на проверке песочницы "
            + "и не запустится. Найдено: " + string.Join(", ", forbidden.Distinct()));
    }

    private static string? FindSharedAssembly()
    {
        var candidates = new[]
        {
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Content.Shared.dll"),
            Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "Content.Client", "Content.Shared.dll"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Минимальный декодер сигнатур: нужно только имя типа-владельца.</summary>
    private sealed class TypeNameProvider : ISignatureTypeProvider<string, object?>
    {
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => reader.GetString(reader.GetTypeReference(handle).Name);

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => reader.GetString(reader.GetTypeDefinition(handle).Name);

        // Для ReadOnlySpan`1<char> важен сам обобщённый тип, аргументы не нужны.
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
            => genericType;

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
        public string GetPinnedType(string elementType) => elementType;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext,
            TypeSpecificationHandle handle, byte rawTypeKind)
            => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
    }
}
