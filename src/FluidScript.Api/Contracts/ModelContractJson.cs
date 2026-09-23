using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Model;
using FluidScript.Core.Model.Contract;

namespace FluidScript.Api.Contracts;

/// <summary>Reads and writes <see cref="ModelContract"/> as JSON: the one place the wire meets a serializer (<c>26</c>, <c>D-47</c>).</summary>
/// <remarks>
/// <para>
/// Core hand-writes the contract and names no serializer; this is the serializer. System.Text.Json
/// from the BCL, reflection-based, with one options object every consumer shares: <c>camelCase</c>
/// names, members in declaration order, nulls written (a <see langword="null"/> means <em>not
/// computed</em>), a field Core marked <see cref="AbsentWhenNullAttribute"/> left out when it has no
/// value, and no escaping of non-ASCII so <c>°C</c> reads as <c>°C</c>.
/// </para>
/// <para>
/// <strong>The size cap lives here</strong>, because it is measured on bytes and Core cannot make any.
/// <see cref="Build"/> builds, measures the compact form, and if it is over <see cref="MaxPayloadBytes"/>
/// builds again with states omitted and <c>FS2502</c> added.
/// </para>
/// </remarks>
public static class ModelContractJson
{
    /// <summary>The size cap, bytes of the compact form, over which states are omitted (<c>FS2502</c>).</summary>
    /// <remarks>
    /// Twice <c>07</c>'s 512 KiB budget for the 200-component reference model. Project reasoning: the
    /// budget is for the model that should always fit, and the cap is where a bigger one stops being
    /// sent whole once per keystroke. P5.1c's payload baseline is what tunes it.
    /// </remarks>
    public const long MaxPayloadBytes = 1024 * 1024;

    /// <summary>The one options object.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        NumberHandling = JsonNumberHandling.Strict,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { AbsentWhenNull } },
    };

    private static readonly JsonSerializerOptions Indented = new(Options) { WriteIndented = true };

    /// <summary>Builds the contract and applies the size cap.</summary>
    /// <param name="input">What the pipeline produced.</param>
    /// <param name="maxPayloadBytes">The cap; <see cref="MaxPayloadBytes"/> unless a test lowers it.</param>
    /// <returns>The contract, states omitted if it would not fit.</returns>
    public static ModelContract Build(ModelContractInput input, long maxPayloadBytes = MaxPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(input);

        var whole = ModelContractBuilder.Build(input);

        if (input.Run is null)
        {
            return whole;
        }

        var size = MeasureBytes(whole);

        if (size <= maxPayloadBytes)
        {
            return whole;
        }

        var omitted = Diagnostic.Create(
            ContractDiagnostics.StatesOmitted,
            span: null,
            new DiagnosticArgument("size", (size / 1024).ToString(CultureInfo.InvariantCulture)),
            new DiagnosticArgument("cap", (maxPayloadBytes / 1024).ToString(CultureInfo.InvariantCulture)));

        return ModelContractBuilder.Build(input with { Diagnostics = [.. input.Diagnostics, omitted] }, statesOmitted: true);
    }

    /// <summary>Writes the contract.</summary>
    /// <param name="contract">The contract.</param>
    /// <param name="indented">Whether to indent, for a golden file or a human; the compact form is the wire's.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize(ModelContract contract, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return JsonSerializer.Serialize(contract, indented ? Indented : Options);
    }

    /// <summary>The compact form's size in bytes of UTF-8, which is what the size cap measures.</summary>
    /// <param name="contract">The contract.</param>
    /// <returns>Bytes.</returns>
    public static long MeasureBytes(ModelContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return JsonSerializer.SerializeToUtf8Bytes(contract, Options).LongLength;
    }

    /// <summary>Reads a contract back.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The contract, or <see langword="null"/> when the text is not one.</returns>
    public static ModelContract? Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            return JsonSerializer.Deserialize<ModelContract>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Core's <see cref="AbsentWhenNullAttribute"/> becomes the serializer's own ignore condition.</summary>
    private static void AbsentWhenNull(JsonTypeInfo type)
    {
        foreach (var property in type.Properties)
        {
            if (property.AttributeProvider?.GetCustomAttributes(typeof(AbsentWhenNullAttribute), inherit: false).Length > 0)
            {
                property.ShouldSerialize = static (_, value) => value is not null;
            }
        }
    }
}
