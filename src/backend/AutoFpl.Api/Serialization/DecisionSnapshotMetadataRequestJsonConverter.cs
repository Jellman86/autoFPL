using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class DecisionSnapshotMetadataRequestJsonConverter
    : JsonConverter<DecisionSnapshotMetadataRequest>
{
    public override DecisionSnapshotMetadataRequest Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Decision snapshot metadata must be a JSON object.");
        }

        string? schemaVersion = null;
        string? sourceType = null;
        bool hasSchemaVersion = false;
        bool hasSourceType = false;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                if (!hasSchemaVersion)
                {
                    throw new JsonException("Required property schemaVersion is missing.");
                }
                if (!hasSourceType)
                {
                    throw new JsonException("Required property sourceType is missing.");
                }
                return new DecisionSnapshotMetadataRequest(schemaVersion, sourceType);
            }
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected a decision snapshot metadata property.");
            }

            string propertyName = reader.GetString()
                ?? throw new JsonException("Decision snapshot metadata property names cannot be null.");
            if (!reader.Read())
            {
                throw new JsonException("Decision snapshot metadata property value is missing.");
            }

            switch (propertyName)
            {
                case "schemaVersion":
                    if (hasSchemaVersion)
                    {
                        throw new JsonException("Duplicate schemaVersion property.");
                    }
                    hasSchemaVersion = true;
                    schemaVersion = ReadRequiredString(ref reader, propertyName);
                    break;
                case "sourceType":
                    if (hasSourceType)
                    {
                        throw new JsonException("Duplicate sourceType property.");
                    }
                    hasSourceType = true;
                    sourceType = ReadRequiredString(ref reader, propertyName);
                    break;
                default:
                    throw new JsonException($"Undeclared property: {propertyName}.");
            }
        }

        throw new JsonException("Decision snapshot metadata JSON object is incomplete.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        DecisionSnapshotMetadataRequest value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("schemaVersion", value.SchemaVersion);
        writer.WriteString("sourceType", value.SourceType);
        writer.WriteEndObject();
    }

    private static string ReadRequiredString(
        ref Utf8JsonReader reader,
        string propertyName)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"{propertyName} must be a string.");
        }

        return reader.GetString()
            ?? throw new JsonException($"{propertyName} must be a string.");
    }
}
