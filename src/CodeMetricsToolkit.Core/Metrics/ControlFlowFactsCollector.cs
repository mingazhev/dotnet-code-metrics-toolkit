using CodeMetricsToolkit.Core.Facts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetricsToolkit.Core.Metrics;

internal static class ControlFlowFactsCollector
{
    public static ControlFlowFacts Collect(SyntaxNode declaration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        cancellationToken.ThrowIfCancellationRequested();

        var walker = new ControlFlowWalker(cancellationToken);
        walker.Visit(declaration);

        return new ControlFlowFacts
        {
            CyclomaticComplexity = 1 + walker.DecisionPointCount,
            DecisionPointCount = walker.DecisionPointCount,
            CognitiveComplexity = walker.CognitiveComplexity,
            NestingDepth = walker.NestingDepth
        };
    }

    private sealed class ControlFlowWalker : CSharpSyntaxWalker
    {
        private readonly CancellationToken _cancellationToken;
        private int _currentNestingDepth;

        public ControlFlowWalker(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
        }

        public int DecisionPointCount { get; private set; }

        public int CognitiveComplexity { get; private set; }

        public int NestingDepth { get; private set; }

        public override void Visit(SyntaxNode? node)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            base.Visit(node);
        }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            AddStructuralDecision();
            VisitWithNesting(() => base.VisitIfStatement(node));
        }

        public override void VisitForStatement(ForStatementSyntax node)
        {
            AddStructuralDecision();
            VisitWithNesting(() => base.VisitForStatement(node));
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            AddStructuralDecision();
            VisitWithNesting(() => base.VisitForEachStatement(node));
        }

        public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
        {
            AddStructuralDecision();
            VisitWithNesting(() => base.VisitForEachVariableStatement(node));
        }

        public override void VisitWhileStatement(WhileStatementSyntax node)
        {
            AddStructuralDecision();
            VisitWithNesting(() => base.VisitWhileStatement(node));
        }

        public override void VisitDoStatement(DoStatementSyntax node)
        {
            AddStructuralDecision();
            VisitWithNesting(() => base.VisitDoStatement(node));
        }

        public override void VisitCatchClause(CatchClauseSyntax node)
        {
            AddStructuralDecision();

            if (node.Filter is not null)
            {
                AddDecision();
            }

            VisitWithNesting(() => base.VisitCatchClause(node));
        }

        public override void VisitCaseSwitchLabel(CaseSwitchLabelSyntax node)
        {
            AddStructuralDecision();
            base.VisitCaseSwitchLabel(node);
        }

        public override void VisitCasePatternSwitchLabel(CasePatternSwitchLabelSyntax node)
        {
            AddStructuralDecision();

            if (node.WhenClause is not null)
            {
                AddDecision();
            }

            base.VisitCasePatternSwitchLabel(node);
        }

        public override void VisitSwitchExpressionArm(SwitchExpressionArmSyntax node)
        {
            AddStructuralDecision();

            if (node.WhenClause is not null)
            {
                AddDecision();
            }

            VisitWithNesting(() => base.VisitSwitchExpressionArm(node));
        }

        public override void VisitSwitchStatement(SwitchStatementSyntax node)
        {
            VisitWithNesting(() => base.VisitSwitchStatement(node));
        }

        public override void VisitSwitchExpression(SwitchExpressionSyntax node)
        {
            VisitWithNesting(() => base.VisitSwitchExpression(node));
        }

        public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            AddStructuralDecision();
            VisitWithNesting(() => base.VisitConditionalExpression(node));
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.LogicalAndExpression) ||
                node.IsKind(SyntaxKind.LogicalOrExpression) ||
                node.IsKind(SyntaxKind.CoalesceExpression))
            {
                AddDecision();
            }

            base.VisitBinaryExpression(node);
        }

        public override void VisitBinaryPattern(BinaryPatternSyntax node)
        {
            if (node.IsKind(SyntaxKind.AndPattern) || node.IsKind(SyntaxKind.OrPattern))
            {
                AddDecision();
            }

            base.VisitBinaryPattern(node);
        }

        public override void VisitIsPatternExpression(IsPatternExpressionSyntax node)
        {
            if (HasRelationalOrTypePattern(node.Pattern))
            {
                AddDecision();
            }

            base.VisitIsPatternExpression(node);
        }

        private void VisitWithNesting(Action visit)
        {
            _currentNestingDepth++;
            NestingDepth = Math.Max(NestingDepth, _currentNestingDepth);

            visit();

            _currentNestingDepth--;
        }

        private void AddStructuralDecision()
        {
            AddDecision();
            CognitiveComplexity += _currentNestingDepth;
        }

        private void AddDecision()
        {
            DecisionPointCount++;
            CognitiveComplexity++;
        }

        private static bool HasRelationalOrTypePattern(PatternSyntax pattern)
        {
            return pattern
                .DescendantNodesAndSelf()
                .Any(node =>
                    node is RelationalPatternSyntax ||
                    node is DeclarationPatternSyntax ||
                    node is RecursivePatternSyntax);
        }
    }
}
