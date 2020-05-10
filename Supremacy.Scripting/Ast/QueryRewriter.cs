using System;

namespace Supremacy.Scripting.Ast
{
    internal static class QueryRewriter
    {
        internal static void Rewrite(IAst query)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            Ast.Walk(ref query, Prefix, Postfix);
        }

        private static bool Postfix(ref IAst ast)
        {
            if (ast is QueryStartClause fromExpression)
            {
                if (fromExpression.RangeVariable.HasExplicitType)
                {
                    fromExpression.Initializer = new InvokeExpression
                    {
                        Target = new MemberAccessExpression
                        {
                            Left = fromExpression.Initializer,
                            Name = "Cast",
                            TypeArguments =
                                                                      {
                                                                          fromExpression.RangeVariable.ElementType
                                                                      }
                        },
                        ParentAst = fromExpression.Initializer.ParentAst
                    };
                }
                return true;
            }

            if (ast is JoinClause joinExpression)
            {
                if (joinExpression.VariableName.HasExplicitType)
                {
                    joinExpression.Initializer = new InvokeExpression
                    {
                        Target = new MemberAccessExpression
                        {
                            Left = joinExpression.Initializer,
                            Name = "Cast",
                            TypeArguments =
                                                                      {
                                                                          joinExpression.VariableName.ElementType
                                                                      }
                        },
                        ParentAst = joinExpression.Initializer.ParentAst
                    };
                }
            }

            return true;
        }

        private static bool Prefix(ref IAst ast)
        {
            if (ast is IntoClause intoExpression)
            {
                ast = new QueryStartClause
                {
                    RangeVariable = intoExpression.RangeVariable,
                    Initializer = intoExpression.Initializer,
                    Next = intoExpression.Next
                };
            }

            return true;
        }
    }
}