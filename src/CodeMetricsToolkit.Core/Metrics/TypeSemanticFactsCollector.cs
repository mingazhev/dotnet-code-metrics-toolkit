using CodeMetricsToolkit.Core.Facts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetricsToolkit.Core.Metrics;

internal sealed record SemanticTypeDeclaration(
    BaseTypeDeclarationSyntax Declaration,
    SemanticModel SemanticModel,
    INamedTypeSymbol Symbol);

internal static class TypeSemanticFactsCollector
{
    public static TypeSemanticFacts? Collect(
        IReadOnlyList<SemanticTypeDeclaration> declarations,
        CancellationToken cancellationToken)
    {
        if (declarations.Count == 0)
        {
            return null;
        }

        INamedTypeSymbol typeSymbol = declarations[0].Symbol.OriginalDefinition;
        var couplingKeys = new HashSet<string>(StringComparer.Ordinal);

        AddSymbolSignatureTypes(typeSymbol, typeSymbol, couplingKeys, cancellationToken);

        foreach (SemanticTypeDeclaration declaration in declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<TypeSyntax> typeSyntaxes = declaration.Declaration
                .DescendantNodes(node => node is not BaseTypeDeclarationSyntax)
                .OfType<TypeSyntax>();

            foreach (TypeSyntax typeSyntax in typeSyntaxes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ITypeSymbol? referencedType = declaration.SemanticModel
                    .GetTypeInfo(typeSyntax, cancellationToken)
                    .Type;
                AddCoupledType(referencedType, typeSymbol, couplingKeys);
            }
        }

        ISymbol[] publicApiSymbols = EnumeratePublicApiSymbols(typeSymbol).ToArray();

        return new TypeSemanticFacts
        {
            InheritanceDepth = GetInheritanceDepth(typeSymbol),
            ClassCoupling = couplingKeys.Count,
            PublicApiCount = publicApiSymbols.Length,
            DocumentedPublicApiCount = publicApiSymbols.Count(HasDocumentation)
        };
    }

    private static int GetInheritanceDepth(INamedTypeSymbol typeSymbol)
    {
        var depth = 0;

        for (INamedTypeSymbol? current = typeSymbol.BaseType;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType)
        {
            depth++;
        }

        return depth;
    }

    private static IEnumerable<ISymbol> EnumeratePublicApiSymbols(INamedTypeSymbol typeSymbol)
    {
        if (IsExternallyAccessible(typeSymbol))
        {
            yield return typeSymbol;
        }

        foreach (ISymbol member in typeSymbol.GetMembers()
                     .Where(member => !member.IsImplicitlyDeclared)
                     .Where(member => member is not INamedTypeSymbol)
                     .Where(member => member is not IMethodSymbol
                     {
                         MethodKind: MethodKind.PropertyGet or
                             MethodKind.PropertySet or
                             MethodKind.EventAdd or
                             MethodKind.EventRemove
                     }))
        {
            if (IsExternallyAccessible(member))
            {
                yield return member;
            }
        }
    }

    private static bool IsExternallyAccessible(ISymbol symbol)
    {
        for (ISymbol? current = symbol;
             current is not null && current.Kind != SymbolKind.Namespace;
             current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (
                Accessibility.Public or
                Accessibility.Protected or
                Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasDocumentation(ISymbol symbol)
    {
        return !string.IsNullOrWhiteSpace(
            symbol.GetDocumentationCommentXml(expandIncludes: false));
    }

    private static void AddSymbolSignatureTypes(
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol ownType,
        HashSet<string> couplingKeys,
        CancellationToken cancellationToken)
    {
        AddCoupledType(typeSymbol.BaseType, ownType, couplingKeys);
        foreach (INamedTypeSymbol interfaceType in typeSymbol.Interfaces)
        {
            AddCoupledType(interfaceType, ownType, couplingKeys);
        }

        AddAttributeTypes(typeSymbol, ownType, couplingKeys);

        foreach (ISymbol member in typeSymbol.GetMembers()
                     .Where(member => !member.IsImplicitlyDeclared)
                     .Where(member => member is not INamedTypeSymbol))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddAttributeTypes(member, ownType, couplingKeys);

            switch (member)
            {
                case IMethodSymbol method:
                    AddCoupledType(method.ReturnType, ownType, couplingKeys);
                    foreach (IParameterSymbol parameter in method.Parameters)
                    {
                        AddCoupledType(parameter.Type, ownType, couplingKeys);
                    }

                    foreach (ITypeParameterSymbol typeParameter in method.TypeParameters)
                    {
                        foreach (ITypeSymbol constraintType in typeParameter.ConstraintTypes)
                        {
                            AddCoupledType(constraintType, ownType, couplingKeys);
                        }
                    }

                    break;
                case IPropertySymbol property:
                    AddCoupledType(property.Type, ownType, couplingKeys);
                    foreach (IParameterSymbol parameter in property.Parameters)
                    {
                        AddCoupledType(parameter.Type, ownType, couplingKeys);
                    }

                    break;
                case IFieldSymbol field:
                    AddCoupledType(field.Type, ownType, couplingKeys);
                    break;
                case IEventSymbol eventSymbol:
                    AddCoupledType(eventSymbol.Type, ownType, couplingKeys);
                    break;
            }
        }
    }

    private static void AddAttributeTypes(
        ISymbol symbol,
        INamedTypeSymbol ownType,
        HashSet<string> couplingKeys)
    {
        foreach (AttributeData attribute in symbol.GetAttributes())
        {
            AddCoupledType(attribute.AttributeClass, ownType, couplingKeys);
        }
    }

    private static void AddCoupledType(
        ITypeSymbol? referencedType,
        INamedTypeSymbol ownType,
        HashSet<string> couplingKeys)
    {
        switch (referencedType)
        {
            case null:
                return;
            case IArrayTypeSymbol arrayType:
                AddCoupledType(arrayType.ElementType, ownType, couplingKeys);
                return;
            case IPointerTypeSymbol pointerType:
                AddCoupledType(pointerType.PointedAtType, ownType, couplingKeys);
                return;
            case ITypeParameterSymbol typeParameter:
                foreach (ITypeSymbol constraintType in typeParameter.ConstraintTypes)
                {
                    AddCoupledType(constraintType, ownType, couplingKeys);
                }

                return;
        }

        if (referencedType is not INamedTypeSymbol namedType)
        {
            return;
        }

        foreach (ITypeSymbol typeArgument in namedType.TypeArguments)
        {
            AddCoupledType(typeArgument, ownType, couplingKeys);
        }

        INamedTypeSymbol originalDefinition = namedType.OriginalDefinition;
        if (originalDefinition.SpecialType != SpecialType.None ||
            originalDefinition.TypeKind == TypeKind.Error ||
            SymbolEqualityComparer.Default.Equals(originalDefinition, ownType))
        {
            return;
        }

        var assemblyName = originalDefinition.ContainingAssembly?.Identity.Name ?? "<source>";
        couplingKeys.Add(
            $"{assemblyName}/{originalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}");
    }
}
