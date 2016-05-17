namespace AstExample
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.IO;
    using System.Linq;
    using System.Management.Automation.Language;
    using System.Reflection;

    // An example AST parser inspired by https://github.com/lzybkr/ShowPSAst.
    // 
    public static class PoshParser
    {
        private class Property : IProperty
        {
            public string name, value, type;

            public string Name { get { return this.name; } }
            public string Value { get { return this.value; } }
            public string Type { get { return this.type; } }
        }

        private class Node : INode
        {
            public string name;
            public int offset, length;
            public List<INode> children;
            public List<IProperty> properties;

            public static Node Create(string name, int startOffset, int endOffset)
            {
                return new Node
                {
                    name = name,
                    offset = startOffset,
                    length = endOffset - startOffset,
                    children = new List<INode>(),
                    properties = new List<IProperty>(),
                };
            }

            public string Name { get { return this.name; } }
            public int Offset { get { return this.offset; } }
            public int Length { get { return this.length; } }
            public ICollection<INode> Children { get { return this.children; } }
            public ICollection<IProperty> Properties { get { return this.properties; } }
        }

        public static IReadOnlyCollection<INode> ParseScript(string script)
        {
            Token[] tokens;
            ParseError[] errors = null;
            Ast ast = Parser.ParseInput(script, out tokens, out errors);
            if (errors.Length != 0)
            {
                // There were errors, just list them as top level nodes.
                return errors.Select(FormatError).ToList();
            }

            var visitor = new TraversingVisitor();
            ast.Visit(visitor);
            return new [] { visitor.ROOT };
        }

        private static INode FormatError(ParseError error)
        {
            var message = string.Format("({0}, {1}) {2}:  {3}",
                error.Extent.StartLineNumber, error.Extent.StartColumnNumber,
                error.ErrorId, error.Message);
            return Node.Create(message, error.Extent.StartOffset, error.Extent.EndOffset);
        }

        private class TraversingVisitor : AstVisitor
        {
            struct Context
            {
                public Ast ast;
                public Node node;

                public static Context Create(Ast a, Node n)
                {
                    return new Context { ast = a, node = n };
                }
            }

            // The root of the Ast.
            public Node ROOT = null;

            // Current path from ROOT of Ast to the current visited node.
            Stack<Context> path = new Stack<Context>();

            // Note: there should probably be a better way to use this visitor,
            // but this approach gets the job done for the example...
            AstVisitAction DoVisit<T>(T ast) where T : Ast
            {
                if (ast.Parent == null)
                {
                    // This must be the root. Set `this.ROOT`, and empty stack.
                    path.Clear();
                    var node = Node.Create(
                        "ROOT :: " + ast.GetType().Name,
                        ast.Extent.StartOffset,
                        ast.Extent.EndOffset);
                    node.properties = ExtractProperties(ast);
                    this.ROOT = node;
                    path.Push(Context.Create(ast, node));
                }
                else
                {
                    // Find what is the name of the relation of ast to it's parent.
                    // I.e. which member of the parent is `ast`?
                    // Note that some Ast tree connections are hidden within collections,
                    // and `IfStatementAst` and `SwitchStatementAst` even has
                    // collection of tuples of ast.
                    string propertyName =
                        SearchIf(ast)
                        ?? SearchSwitch(ast)
                        ?? SearchProperty(ast)
                        ?? SearchPropertyList(ast)
                        ?? "<UNKNOWN>";

                    while (path.Count != 0 && path.Peek().ast != ast.Parent)
                    {
                        path.Pop();
                    }

                    if (path.Count == 0)
                    {
                        throw new InvalidOperationException("Broken Ast Tree!");
                    }

                    var node = Node.Create(
                        propertyName + " :: " + ast.GetType().Name,
                        ast.Extent.StartOffset,
                        ast.Extent.EndOffset);
                    node.properties = ExtractProperties(ast);
                    path.Peek().node.Children.Add(node);
                    path.Push(Context.Create(ast, node));
                }

                return AstVisitAction.Continue;
            }

            private string SearchPropertyList<T>(T ast) where T : Ast
            {
                foreach (var pList in ast.Parent.GetType().GetProperties()
                    .Where(IsAstListType))
                {
                    var ps = (IReadOnlyCollection<Ast>)pList.GetValue(ast.Parent);
                    var found = ps
                        .Select((v, i) => new { v, i })
                        .FirstOrDefault(e => e.v.Equals(ast));
                    if (found != null)
                    {
                        return pList.Name + "[" + found.i + "]";
                    }
                }

                return null;
            }

            private string SearchProperty<T>(T ast) where T : Ast
            {
                var property = ast.Parent.GetType().GetProperties()
                    .Where(IsAstType)
                    .SingleOrDefault(p => ast.Equals(p.GetValue(ast.Parent)));
                if (property != null)
                {
                    return property.Name;
                }

                return null;
            }

            private string SearchSwitch<T>(T ast) where T : Ast
            {
                var switchStmtAst = ast.Parent as SwitchStatementAst;
                if (switchStmtAst != null)
                {
                    return FindInClause(ast, switchStmtAst.Clauses);
                }

                return null;
            }

            private string SearchIf<T>(T ast) where T : Ast
            {
                var ifStmtAst = ast.Parent as IfStatementAst;
                if (ifStmtAst != null)
                {
                    return FindInClause(ast, ifStmtAst.Clauses);
                }

                return null;
            }

            private static string FindInClause<TAst, TAst1, TAst2>(
                TAst ast, ReadOnlyCollection<Tuple<TAst1, TAst2>> clauses)
                where TAst : Ast where TAst1 : Ast where TAst2 : Ast
            {
                var found = clauses
                    .Select((v, i) => new { pred = v.Item1, act = v.Item2, i })
                    .FirstOrDefault(e => e.pred.Equals(ast) || e.act.Equals(ast));
                if (found != null)
                {
                    return found.pred.Equals(ast)
                        ? "Predicate[" + found.i + "]"
                        : "Action[" + found.i + "]";
                }

                return null;
            }

            private List<IProperty> ExtractProperties<T>(T ast) where T : Ast
            {
                var result = new List<IProperty>();

                foreach (var property in ast.GetType().GetProperties())
                {
                    // Skip properties already represented in the Ast tree.
                    if (IsAstType(property) || IsAstListType(property))
                        continue;

                    result.Add(PropertyConverter<T>(property.GetValue(ast), property));
                }

                return result;
            }

            private static Property PropertyConverter<T>(object value, PropertyInfo property)
                where T : Ast
            {
                // Note: This simple approach only takes care of types properly
                // formattable with `ToString()`, but once again good enough for an example.
                var typeName = property.PropertyType.Name;
                if (property.PropertyType.IsGenericType &&
                    property.PropertyType.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>))
                {
                    typeName = property.PropertyType.GetGenericArguments()[0].Name + "[]";
                }

                // Let just special-format the extent.
                if (typeName == "IScriptExtent")
                {
                    var ext = value as IScriptExtent;
                    var file = ext.File == null ? "" : Path.GetFileName(ext.File);
                    value = string.Format("{0} ({1},{2})-({3},{4})",
                        file, ext.StartLineNumber, ext.StartColumnNumber,
                        ext.EndLineNumber, ext.EndColumnNumber);
                }

                return new Property
                {
                    name = property.Name,
                    value = (value ?? "NULL").ToString(),
                    type = typeName
                };
            }

            private static bool IsAstType(PropertyInfo pi)
            {
                return typeof(Ast).IsAssignableFrom(pi.PropertyType);
            }

            private static bool IsAstListType(PropertyInfo pi)
            {
                return pi.PropertyType.IsGenericType &&
                    typeof(Ast).IsAssignableFrom(pi.PropertyType.GetGenericArguments()[0]) &&
                    pi.PropertyType.GetGenericTypeDefinition() == typeof(ReadOnlyCollection<>);
            }

            #region Generated Visitor boilerplate
            public override AstVisitAction VisitArrayExpression(ArrayExpressionAst arrayExpressionAst) { return DoVisit(arrayExpressionAst); }
            public override AstVisitAction VisitArrayLiteral(ArrayLiteralAst arrayLiteralAst) { return DoVisit(arrayLiteralAst); }
            public override AstVisitAction VisitAssignmentStatement(AssignmentStatementAst assignmentStatementAst) { return DoVisit(assignmentStatementAst); }
            public override AstVisitAction VisitAttribute(AttributeAst attributeAst) { return DoVisit(attributeAst); }
            public override AstVisitAction VisitAttributedExpression(AttributedExpressionAst attributedExpressionAst) { return DoVisit(attributedExpressionAst); }
            public override AstVisitAction VisitBinaryExpression(BinaryExpressionAst binaryExpressionAst) { return DoVisit(binaryExpressionAst); }
            public override AstVisitAction VisitBlockStatement(BlockStatementAst blockStatementAst) { return DoVisit(blockStatementAst); }
            public override AstVisitAction VisitBreakStatement(BreakStatementAst breakStatementAst) { return DoVisit(breakStatementAst); }
            public override AstVisitAction VisitCatchClause(CatchClauseAst catchClauseAst) { return DoVisit(catchClauseAst); }
            public override AstVisitAction VisitCommand(CommandAst commandAst) { return DoVisit(commandAst); }
            public override AstVisitAction VisitCommandExpression(CommandExpressionAst commandExpressionAst) { return DoVisit(commandExpressionAst); }
            public override AstVisitAction VisitCommandParameter(CommandParameterAst commandParameterAst) { return DoVisit(commandParameterAst); }
            public override AstVisitAction VisitConstantExpression(ConstantExpressionAst constantExpressionAst) { return DoVisit(constantExpressionAst); }
            public override AstVisitAction VisitContinueStatement(ContinueStatementAst continueStatementAst) { return DoVisit(continueStatementAst); }
            public override AstVisitAction VisitConvertExpression(ConvertExpressionAst convertExpressionAst) { return DoVisit(convertExpressionAst); }
            public override AstVisitAction VisitDataStatement(DataStatementAst dataStatementAst) { return DoVisit(dataStatementAst); }
            public override AstVisitAction VisitDoUntilStatement(DoUntilStatementAst doUntilStatementAst) { return DoVisit(doUntilStatementAst); }
            public override AstVisitAction VisitDoWhileStatement(DoWhileStatementAst doWhileStatementAst) { return DoVisit(doWhileStatementAst); }
            public override AstVisitAction VisitErrorExpression(ErrorExpressionAst errorExpressionAst) { return DoVisit(errorExpressionAst); }
            public override AstVisitAction VisitErrorStatement(ErrorStatementAst errorStatementAst) { return DoVisit(errorStatementAst); }
            public override AstVisitAction VisitExitStatement(ExitStatementAst exitStatementAst) { return DoVisit(exitStatementAst); }
            public override AstVisitAction VisitExpandableStringExpression(ExpandableStringExpressionAst expandableStringExpressionAst) { return DoVisit(expandableStringExpressionAst); }
            public override AstVisitAction VisitFileRedirection(FileRedirectionAst redirectionAst) { return DoVisit(redirectionAst); }
            public override AstVisitAction VisitForEachStatement(ForEachStatementAst forEachStatementAst) { return DoVisit(forEachStatementAst); }
            public override AstVisitAction VisitForStatement(ForStatementAst forStatementAst) { return DoVisit(forStatementAst); }
            public override AstVisitAction VisitFunctionDefinition(FunctionDefinitionAst functionDefinitionAst) { return DoVisit(functionDefinitionAst); }
            public override AstVisitAction VisitHashtable(HashtableAst hashtableAst) { return DoVisit(hashtableAst); }
            public override AstVisitAction VisitIfStatement(IfStatementAst ifStmtAst) { return DoVisit(ifStmtAst); }
            public override AstVisitAction VisitIndexExpression(IndexExpressionAst indexExpressionAst) { return DoVisit(indexExpressionAst); }
            public override AstVisitAction VisitInvokeMemberExpression(InvokeMemberExpressionAst methodCallAst) { return DoVisit(methodCallAst); }
            public override AstVisitAction VisitMemberExpression(MemberExpressionAst memberExpressionAst) { return DoVisit(memberExpressionAst); }
            public override AstVisitAction VisitMergingRedirection(MergingRedirectionAst redirectionAst) { return DoVisit(redirectionAst); }
            public override AstVisitAction VisitNamedAttributeArgument(NamedAttributeArgumentAst namedAttributeArgumentAst) { return DoVisit(namedAttributeArgumentAst); }
            public override AstVisitAction VisitNamedBlock(NamedBlockAst namedBlockAst) { return DoVisit(namedBlockAst); }
            public override AstVisitAction VisitParamBlock(ParamBlockAst paramBlockAst) { return DoVisit(paramBlockAst); }
            public override AstVisitAction VisitParameter(ParameterAst parameterAst) { return DoVisit(parameterAst); }
            public override AstVisitAction VisitParenExpression(ParenExpressionAst parenExpressionAst) { return DoVisit(parenExpressionAst); }
            public override AstVisitAction VisitPipeline(PipelineAst pipelineAst) { return DoVisit(pipelineAst); }
            public override AstVisitAction VisitReturnStatement(ReturnStatementAst returnStatementAst) { return DoVisit(returnStatementAst); }
            public override AstVisitAction VisitScriptBlock(ScriptBlockAst scriptBlockAst) { return DoVisit(scriptBlockAst); }
            public override AstVisitAction VisitScriptBlockExpression(ScriptBlockExpressionAst scriptBlockExpressionAst) { return DoVisit(scriptBlockExpressionAst); }
            public override AstVisitAction VisitStatementBlock(StatementBlockAst statementBlockAst) { return DoVisit(statementBlockAst); }
            public override AstVisitAction VisitStringConstantExpression(StringConstantExpressionAst stringConstantExpressionAst) { return DoVisit(stringConstantExpressionAst); }
            public override AstVisitAction VisitSubExpression(SubExpressionAst subExpressionAst) { return DoVisit(subExpressionAst); }
            public override AstVisitAction VisitSwitchStatement(SwitchStatementAst switchStatementAst) { return DoVisit(switchStatementAst); }
            public override AstVisitAction VisitThrowStatement(ThrowStatementAst throwStatementAst) { return DoVisit(throwStatementAst); }
            public override AstVisitAction VisitTrap(TrapStatementAst trapStatementAst) { return DoVisit(trapStatementAst); }
            public override AstVisitAction VisitTryStatement(TryStatementAst tryStatementAst) { return DoVisit(tryStatementAst); }
            public override AstVisitAction VisitTypeConstraint(TypeConstraintAst typeConstraintAst) { return DoVisit(typeConstraintAst); }
            public override AstVisitAction VisitTypeExpression(TypeExpressionAst typeExpressionAst) { return DoVisit(typeExpressionAst); }
            public override AstVisitAction VisitUnaryExpression(UnaryExpressionAst unaryExpressionAst) { return DoVisit(unaryExpressionAst); }
            public override AstVisitAction VisitUsingExpression(UsingExpressionAst usingExpressionAst) { return DoVisit(usingExpressionAst); }
            public override AstVisitAction VisitVariableExpression(VariableExpressionAst variableExpressionAst) { return DoVisit(variableExpressionAst); }
            public override AstVisitAction VisitWhileStatement(WhileStatementAst whileStatementAst) { return DoVisit(whileStatementAst); }
            #endregion
        }
    }
}
