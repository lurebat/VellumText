using System.Reflection;

namespace VellumText.Tests.Model;

/// <summary>
/// The hierarchies that a closed switch consumes are closed to derivation.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Node"/>, <see cref="BlockNode"/>, <see cref="InlineEmbed"/> and
/// <see cref="VellumText.Selection"/> are each walked by a <c>switch</c> that throws, drops, or refuses on a
/// shape it does not recognise — <c>DocumentPresenter</c>'s block factory, the HTML and RTF
/// exporters, the JSON writer, every command that asks whether it has a rectangle of cells. A type
/// a host derived would compile against the library and then fail the first time it was rendered
/// or saved. Extensibility that fails at run time in someone else's application is worse than no
/// extensibility, which fails at compile time in ours.
/// </para>
/// <para>
/// A reflection test rather than a compiler probe because the guarantee is about what an assembly
/// that is <em>not</em> a friend can do, and this assembly is one. The constructor's declared
/// accessibility is exactly the thing that decides it, so that is what gets asserted.
/// </para>
/// </remarks>
public class ClosedHierarchyTests
{
    public static TheoryData<Type> Closed =>
    [
        typeof(Node),
        typeof(BlockNode),
        typeof(InlineEmbed),
        typeof(VellumText.Selection),
    ];

    [Theory]
    [MemberData(nameof(Closed))]
    public void Every_constructor_is_private_protected(Type type)
    {
        var constructors = type.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotEmpty(constructors);

        foreach (var constructor in constructors)
        {
            // FamANDAssem is `private protected`: derivable inside this assembly and its friends,
            // and nowhere else. `protected` (Family) would be the fake-extensibility case, and
            // `protected internal` (FamORAssem) would be the same thing with extra steps.
            Assert.True(
                constructor.IsFamilyAndAssembly || IsTheRecordCopyConstructor(type, constructor),
                $"{type.Name}..ctor({Signature(constructor)}) is {Accessibility(constructor)}");
        }
    }

    /// <summary>
    /// The gap the closure cannot cover, pinned so it is a known quantity rather than a surprise.
    /// </summary>
    /// <remarks>
    /// A record's copy constructor must be public or protected — declaring it
    /// <c>private protected</c> is CS8878 — so a caller outside this assembly can still derive
    /// from the two records by declaring a copy constructor that chains to it. This test exists so
    /// that the day the language offers a way to close it, the assertion below fails and says so.
    /// </remarks>
    [Theory]
    [InlineData(typeof(InlineEmbed))]
    [InlineData(typeof(VellumText.Selection))]
    public void The_record_copy_constructor_is_the_one_hole_the_language_leaves(Type type)
    {
        var copy = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(c => c.GetParameters() is [{ } only] && only.ParameterType == type);

        Assert.True(copy.IsFamily, $"{type.Name}'s copy constructor is {Accessibility(copy)}");
    }

    /// <summary>The types above are abstract, so nothing can be constructed past the switch anyway.</summary>
    [Theory]
    [MemberData(nameof(Closed))]
    public void Every_closed_base_is_abstract(Type type) => Assert.True(type.IsAbstract);

    /// <summary>
    /// <see cref="Step"/> is deliberately <em>not</em> closed, and that is not an oversight.
    /// </summary>
    /// <remarks>
    /// Nothing switches on a step's concrete type: the four obligations — apply, invert, map,
    /// rebase — are all on the base, so a step written elsewhere is fully usable by everything
    /// that consumes one. That is the difference between real extensibility and the fake kind, and
    /// this test fails if someone closes it by reflex.
    /// </remarks>
    [Fact]
    public void Step_stays_open_because_nothing_switches_on_which_step_it_is()
    {
        var constructor = Assert.Single(
            typeof(Step).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            c => c.GetParameters().Length == 0);

        Assert.True(constructor.IsFamily, $"Step..ctor() is {Accessibility(constructor)}");
    }

    private static bool IsTheRecordCopyConstructor(Type type, ConstructorInfo constructor) =>
        constructor.GetParameters() is [{ } only] && only.ParameterType == type;

    private static string Signature(ConstructorInfo constructor) =>
        string.Join(", ", constructor.GetParameters().Select(p => p.ParameterType.Name));

    private static string Accessibility(ConstructorInfo constructor) => constructor switch
    {
        { IsPublic: true } => "public",
        { IsFamilyAndAssembly: true } => "private protected",
        { IsFamilyOrAssembly: true } => "protected internal",
        { IsFamily: true } => "protected",
        { IsAssembly: true } => "internal",
        _ => "private",
    };
}
