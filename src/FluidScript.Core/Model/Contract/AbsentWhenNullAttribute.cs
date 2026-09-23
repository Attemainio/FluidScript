namespace FluidScript.Core.Model.Contract;

/// <summary>Marks a wire field that is left out entirely, rather than written as <see langword="null"/>, when it has no value.</summary>
/// <remarks>
/// The contract's own vocabulary for <c>26</c>'s rule that <see langword="null"/> means <em>not
/// computed</em> and absence means <em>not applicable</em>. The serializer outside Core reads this and
/// applies its own ignore condition (<c>D-47</c>: no serialization type is named in Core).
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AbsentWhenNullAttribute : Attribute;
