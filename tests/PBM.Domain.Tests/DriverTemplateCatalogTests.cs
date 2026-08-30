using System.Text.RegularExpressions;
using PBM.Application;
using Xunit;

namespace PBM.Domain.Tests;

public sealed class DriverTemplateCatalogTests
{
    private static readonly Regex VariableRegex = new(@"\[([^\]]+)\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void Template_codes_are_unique()
    {
        var templates = DriverTemplateCatalog.GetAll();
        Assert.Equal(templates.Count, templates.Select(x => x.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("SALES_REVENUE")]
    [InlineData("PAYROLL")]
    [InlineData("IMPORT_LANDED_COST")]
    [InlineData("FINANCING")]
    [InlineData("OPEX_INFLATION")]
    public void Template_dependencies_are_self_contained(string templateCode)
    {
        var template = DriverTemplateCatalog.GetRequired(templateCode);
        var measures = template.Measures.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assumptions = template.Assumptions.Select(x => $"ASSUMP:{x.Code}").ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(template.Measures.Count, measures.Count);
        Assert.Equal(template.Assumptions.Count, template.Assumptions.Select(x => x.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var measure in template.Measures.Where(x => x.IsCalculated))
        {
            Assert.False(string.IsNullOrWhiteSpace(measure.FormulaExpression));
            foreach (var dependency in ExtractVariables(measure.FormulaExpression!))
                Assert.True(measures.Contains(dependency) || assumptions.Contains(dependency),
                    $"Template {template.Code}, measure {measure.Code} references unknown variable {dependency}.");
        }
    }

    [Theory]
    [InlineData("SALES_REVENUE")]
    [InlineData("PAYROLL")]
    [InlineData("IMPORT_LANDED_COST")]
    [InlineData("FINANCING")]
    [InlineData("OPEX_INFLATION")]
    public void Template_measure_dependencies_are_acyclic(string templateCode)
    {
        var template = DriverTemplateCatalog.GetRequired(templateCode);
        var measureCodes = template.Measures.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var graph = template.Measures.ToDictionary(
            x => x.Code,
            x => ExtractVariables(x.FormulaExpression ?? string.Empty).Where(measureCodes.Contains).ToArray(),
            StringComparer.OrdinalIgnoreCase);

        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in graph.Keys)
            Visit(node, graph, state);
    }

    [Fact]
    public void Unknown_template_is_rejected()
    {
        Assert.Throws<KeyNotFoundException>(() => DriverTemplateCatalog.GetRequired("NOT_A_TEMPLATE"));
    }

    private static IReadOnlyList<string> ExtractVariables(string expression) =>
        VariableRegex.Matches(expression)
            .Select(x => x.Groups[1].Value.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void Visit(
        string node,
        IReadOnlyDictionary<string, string[]> graph,
        IDictionary<string, int> state)
    {
        if (state.TryGetValue(node, out var existing))
        {
            if (existing == 1) throw new Xunit.Sdk.XunitException($"Formula dependency cycle detected at {node}.");
            return;
        }

        state[node] = 1;
        foreach (var next in graph[node]) Visit(next, graph, state);
        state[node] = 2;
    }
}
