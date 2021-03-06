using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;

using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Scripting;
using Microsoft.Scripting.Actions;
using System.Linq.Expressions;

using Microsoft.Scripting.Ast;
using Microsoft.Scripting.Runtime;
using Microsoft.Scripting.Utils;

using Supremacy.Scripting.Ast;
using Supremacy.Scripting.Runtime.Binders;

using MSAst = System.Linq.Expressions.Expression;
using ExpressionType = System.Linq.Expressions.ExpressionType;

namespace Supremacy.Scripting.Runtime
{
    public class ScriptGenerator
    {
        private readonly ScriptLanguageContext _sxeContext;

        internal ScriptGenerator(ScriptLanguageContext sxeContext, CompilerContext compilerContext)
        {
            _sxeContext = sxeContext ?? throw new ArgumentNullException("sxeContext");
            CompilerContext = compilerContext ?? throw new ArgumentNullException("compilerContext");
            Binder = sxeContext.DefaultBinderState;

            Scope = new ScriptScope(null, "<Default>", compilerContext.SourceUnit.Document);
        }

        internal CompilerContext CompilerContext { get; }

        internal ScriptScope Scope { get; private set; }

        internal ScriptScope PushNewScope(string name, SymbolDocumentInfo document)
        {
            return Scope = new ScriptScope(Scope, name, document);
        }

        internal ScriptScope PushNewScope(string name)
        {
            return Scope = new ScriptScope(Scope, name, Scope.Document);
        }

        internal ScriptScope PushNewScope()
        {
            return Scope = new ScriptScope(Scope, null, Scope.Document);
        }

        internal void PopScope()
        {
            Scope = Scope.Parent;
        }

        internal LanguageContext Context => _sxeContext;

        #region Binding Methods

        internal MSAst Operator(ExpressionType op, MSAst left)
        {
            return Operator(op, left, null);
        }

        internal MSAst Operator(ExpressionType op, MSAst left, MSAst right)
        {
            Type returnType = ((op == ExpressionType.IsTrue) || (op == ExpressionType.IsFalse)) ? typeof(bool) : typeof(object);
            if (right == null)
            {
                return MSAst.Dynamic(
                    Binder.UnaryOperation(op),
                    returnType,
                    left);
            }
            return MSAst.Dynamic(
                Binder.BinaryOperation(op),
                returnType,
                left,
                right);
        }

        internal MSAst GetMember(MSAst target, string memberName)
        {
            System.Runtime.CompilerServices.CallSiteBinder binder = Microsoft.CSharp.RuntimeBinder.Binder.GetMember(CSharpBinderFlags.None, memberName, null, new[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null) });
            return MSAst.Dynamic(binder, typeof(object), target);
        }

        internal MSAst Invoke(MSAst target, Type[] typeArguments, MSAst[] arguments)
        {
            return MSAst.Dynamic(
                Binder.Invoke(arguments.Length),
                typeof(object),
                ArrayUtils.Insert(
                    target,
                    //MSAst.Constant(typeArguments, typeof(Type[])),
                    arguments));
        }

        internal MSAst InvokeQueryMethod(MSAst target, string methodName, MSAst[] arguments)
        {
            return MSAst.Dynamic(
                new SxeQueryMethodBinder(Binder.Binder, methodName, false, new CallInfo(arguments.Length)),
                typeof(object),
                ArrayUtils.Insert(
                    target,
                    //MSAst.Constant(typeArguments, typeof(Type[])),
                    arguments));
        }

        internal MSAst InvokeStaticMember(MSAst target, string memberName, Type[] typeArguments, MSAst[] arguments)
        {
            MSAst[] newArgs = ArrayUtils.Insert(
                //MSAst.Constant(typeof(Enumerable)),
                target,
                arguments);

            System.Runtime.CompilerServices.CallSiteBinder binder = Microsoft.CSharp.RuntimeBinder.Binder.InvokeMember(
                CSharpBinderFlags.None,
                memberName,
                typeArguments,
                typeof(QueryClause.QueryExpressionInvocation),
                newArgs.Select((e, i) => CSharpArgumentInfo.Create(((i == 1) ? CSharpArgumentInfoFlags.None : CSharpArgumentInfoFlags.UseCompileTimeType) | ((i == 0) ? CSharpArgumentInfoFlags.IsStaticType : ((i == 1) ? CSharpArgumentInfoFlags.None : CSharpArgumentInfoFlags.UseCompileTimeType)), null)));

            return MSAst.Dynamic(
                binder,
                typeof(object),
                newArgs);
        }

        internal MSAst Call(string methodName, MSAst instance = null, Type type = null, params MSAst[] arguments)
        {
            if (((instance == null) && (type == null)) || ((instance != null) && (type != null)))
            {
                throw new ArgumentException("Either 'instance' or 'type' must be specified, but not both.");
            }

            List<MethodBase> candidates;

            bool isCandidate(MethodBase m) => (m.Name == methodName) &&
                      ((m.GetParameters().Length == arguments.Length) ||
                       (BinderHelpers.IsParamsMethod(m) && ((m.GetParameters().Length + 1) <= arguments.Length)));

            candidates = type != null
                ? type
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where((Func<MethodBase, bool>)isCandidate)
                    .Cast<MethodBase>()
                    .ToList()
                : instance.Type
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where((Func<MethodBase, bool>)isCandidate)
                    .Cast<MethodBase>()
                    .ToList();

            return Binder.Binder.CallMethod(
                _sxeContext.OverloadResolver.CreateOverloadResolver(
                arguments.Select(o => DynamicUtils.ObjectToMetaObject(null, o)).ToList(),
                new CallSignature(arguments.Length),
                CallTypes.None),
                candidates).Expression;
        }

        internal MSAst InvokeMember(MSAst target, string memberName, Type[] typeArguments, MSAst[] arguments)
        {
            MSAst[] newArgs = ArrayUtils.Insert(
                //MSAst.Constant(typeof(Enumerable)),
                target,
                arguments);

            System.Runtime.CompilerServices.CallSiteBinder binder = Microsoft.CSharp.RuntimeBinder.Binder.InvokeMember(
                CSharpBinderFlags.None,
                memberName,
                Enumerable.Empty<Type>(),
                null,
                arguments.Select((e, i) => CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)));

            return MSAst.Dynamic(
                binder,
                typeof(object),
                newArgs);
        }

        internal MSAst GetIndex(MSAst target, MSAst index)
        {
            return MSAst.Dynamic(Binder.GetIndex(1), typeof(object), target, index);
        }

        internal MSAst GetIndex(MSAst target, params MSAst[] arguments)
        {
            return MSAst.Dynamic(
                Binder.GetIndex(arguments.Length),
                typeof(object),
                ArrayUtils.Insert(
                    target,
                    MSAst.Constant(arguments, typeof(MSAst[])),
                    arguments));
        }

        internal MSAst ConvertTo(Type type, MSAst expression)
        {
            return ConvertTo(type, ConversionResultKind.ExplicitCast, expression);
        }

        internal MSAst Add(MSAst left, MSAst right)
        {
            return Operator(ExpressionType.Add, left, right);
        }

        internal MSAst Subtract(MSAst left, MSAst right)
        {
            return Operator(ExpressionType.Subtract, left, right);
        }

        internal MSAst Multiply(MSAst left, MSAst right)
        {
            return Operator(ExpressionType.Multiply, left, right);
        }

        internal MSAst Divide(MSAst left, MSAst right)
        {
            return Operator(ExpressionType.Divide, left, right);
        }

        internal MSAst LessThan(MSAst left, MSAst right)
        {
            return Operator(ExpressionType.LessThan, left, right);
        }

        internal MSAst LessThanOrEqual(MSAst left, MSAst right)
        {
            return Operator(ExpressionType.LessThanOrEqual, left, right);
        }

        internal MSAst GreaterThan(MSAst left, MSAst right)
        {
            return Operator(ExpressionType.GreaterThan, left, right);
        }

        internal MSAst GreaterThanOrEqual(MSAst left, MSAst right)
        {
            return Operator(ExpressionType.GreaterThanOrEqual, left, right);
        }

        internal MSAst Equal(MSAst left, MSAst right)
        {
            return Operator(ExpressionType.Equal, left, right);
        }

        internal MSAst NotEqual(MSAst left, MSAst right)
        {
            return Operator(ExpressionType.NotEqual, left, right);
        }
        internal MSAst And(MSAst left, MSAst right)
        {
            return Operator(ExpressionType.And, left, right);
        }

        internal MSAst AndAlso(MSAst left, MSAst right)
        {
            return Operator(ExpressionType.AndAlso, left, right);
        }

        internal MSAst Or(MSAst left, MSAst right)
        {
            return Operator(ExpressionType.Or, left, right);
        }

        internal MSAst OrElse(MSAst left, MSAst right)
        {
            return Operator(ExpressionType.OrElse, left, right);
        }

        internal MSAst UnaryPlus(MSAst operand)
        {
            return Operator(ExpressionType.UnaryPlus, operand, null);
        }

        internal MSAst UnaryMinus(MSAst operand)
        {
            return Operator(ExpressionType.Negate, operand, null);
        }

        internal MSAst Exponent(MSAst left, MSAst right)
        {
            return Operator(ExpressionType.Power, left, right);
        }

        internal MSAst Not(MSAst operand)
        {
            return Operator(ExpressionType.IsFalse, operand, null);
        }

        internal MSAst Complement(MSAst operand)
        {
            return Operator(ExpressionType.Not, operand, null);
        }

        internal MSAst AddSpan(SourceSpan span, MSAst expression)
        {
            if (Scope.Document != null)
            {
                expression = Utils.AddDebugInfo(expression, Scope.Document, span.Start, span.End);
            }

            return expression;
        }

        internal MSAst IsTrue(MSAst operand)
        {
            return Operator(ExpressionType.IsTrue, operand, null);
        }

        internal MSAst IsFalse(MSAst operand)
        {
            return Operator(ExpressionType.IsFalse, operand, null);
        }

        internal BinderState Binder { get; }
        #endregion

        #region Conversions
        internal MSAst ConvertTo(Type toType, ConversionResultKind kind, MSAst expression)
        {
            return Binder.Binder.ConvertTo(toType, kind, expression);
        }
        #endregion
    }
}