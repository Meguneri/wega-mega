using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Shared.FixedPoint;
using Content.Shared.Store;
using Robust.Shared.Prototypes;

namespace Content.Server._Wega.Raid;

/// <summary>
/// Serializes <c>Dictionary&lt;ProtoId&lt;CurrencyPrototype&gt;, FixedPoint2&gt;</c> as a simple JSON object
/// with currency prototype IDs as keys and integer hundredths as values.
/// </summary>
public sealed class RaidStashCurrencyDictionaryConverter : JsonConverter<Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2>>
{
    public override Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2>();

        if (reader.TokenType != JsonTokenType.StartObject)
            return result;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return result;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            var key = reader.GetString() ?? string.Empty;
            if (!reader.Read())
                break;

            var value = FixedPoint2.Zero;
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var intValue))
            {
                value = FixedPoint2.FromCents(intValue);
            }

            result[new ProtoId<CurrencyPrototype>(key)] = value;
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (currency, amount) in value)
        {
            writer.WriteNumber(currency.Id, amount.Value);
        }
        writer.WriteEndObject();
    }
}
