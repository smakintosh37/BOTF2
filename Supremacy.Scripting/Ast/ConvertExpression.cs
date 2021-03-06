using System;
using System.ComponentModel;

using Microsoft.Scripting;
using Microsoft.Scripting.Actions;

using Supremacy.Annotations;
using Supremacy.Scripting.Runtime;
using Supremacy.Scripting.Utility;

using MSAst = System.Linq.Expressions.Expression;

namespace Supremacy.Scripting.Ast
{
    public class ConvertExpression : Expression
    {
        private FullNamedExpression _targetType;
        private Expression _operand;

        public Expression Operand => _operand;

        public ConvertExpression() { }

        public ConvertExpression([NotNull] FullNamedExpression targetType, [NotNull] Expression operand) : this(targetType, operand, operand.Span) { }

        public ConvertExpression([NotNull] FullNamedExpression targetType, [NotNull] Expression operand, SourceSpan span)
        {
            _operand = operand ?? throw new ArgumentNullException("operand");
            _targetType = targetType ?? throw new ArgumentNullException("targetType");

            Span = span;
            Type = _targetType.Type;
        }

        public override void CloneTo<T>(CloneContext cloneContext, T target)
        {
            base.CloneTo(cloneContext, target);

            if (!(target is ConvertExpression clone))
            {
                return;
            }

            clone._operand = Clone(cloneContext, _operand);
            clone._targetType = Clone(cloneContext, _targetType);
        }

        [DefaultValue(false)]
        public bool IsImplicitConversionRequired
        {
            get; set;
        }

        public FullNamedExpression TargetType
        {
            get => _targetType;
            set
            {
                _targetType = value;
                Type = _targetType?.Type;
            }
        }

        [DefaultValue(false)]
        public override bool IsPrimaryExpression => IsImplicitConversionRequired && _operand.IsPrimaryExpression;

        public override void Walk(AstVisitor prefix, AstVisitor postfix)
        {
            Walk(ref _targetType, prefix, postfix);
            Walk(ref _operand, prefix, postfix);
        }

        public override void Dump(SourceWriter sw, int indentChange)
        {
            if (IsImplicitConversionRequired)
            {
                DumpChild(_operand, sw, indentChange);
            }
            else
            {
                sw.Write("(");
                DumpChild(_targetType, sw, indentChange);
                sw.Write(")");
            }
        }

        public override MSAst TransformCore(ScriptGenerator generator)
        {
            MSAst transformedOperator = _operand.Transform(generator);

            if (transformedOperator.Type == _targetType.Type)
            {
                return transformedOperator;
            }

            return Type == TypeManager.CoreTypes.String
                ? MSAst.Call(
                    transformedOperator,
                    CommonMembers.ObjectToString)
                : generator.ConvertTo(
                Type,
                IsImplicitConversionRequired
                    ? ConversionResultKind.ImplicitCast
                    : ConversionResultKind.ExplicitCast,
                transformedOperator);
        }

        public override Expression DoResolve(ParseContext parseContext)
        {
            _operand = _operand.Resolve(parseContext);
            _targetType = _targetType.ResolveAsTypeStep(parseContext, false);

            Type = _targetType.Type;

            return this;
        }

        public static Expression MakeImplicitConversion(ParseContext ec, Expression source, Type destinationType, SourceSpan location)
        {
            if (source is LambdaExpression)
            {
                if (((LambdaExpression)source).ImplicitStandardConversionExists(ec, destinationType))
                {
                    return source;
                }

                Type returnType = destinationType;

                if (TypeManager.IsDelegateType(returnType))
                {
                    returnType = returnType.GetMethod("Invoke").ReturnType;
                }

                return TypeManager.IsEqual(((LambdaExpression)source).Body.Type, returnType) ? source : null;
            }

            return TypeManager.IsEqual(source.Type, destinationType)
                ? source
                : new ConvertExpression(TypeExpression.Create(destinationType), source, location)
                {
                    IsImplicitConversionRequired = true
                }.Resolve(ec);
        }

        public static Expression MakeExplicitConversion(ParseContext ec, Expression source, Type destinationType, SourceSpan location)
        {
            return new ConvertExpression(TypeExpression.Create(destinationType), source, location)
            {
                IsImplicitConversionRequired = false
            }.Resolve(ec);
        }

        public static Expression ImplicitNullableConversion(ParseContext ec, Expression expr, Type targetType)
        {
            Type exprType = expr.Type;

            //
            // From null to any nullable type
            //
            if (exprType == TypeManager.CoreTypes.Null)
            {
                return ec == null ? EmptyExpression.Null : LiftedNullExpression.Create(targetType, expr.Span);
            }

            // S -> T?
            Type elementType = targetType.GetGenericArguments()[0];

            // S? -> T?
            if (TypeManager.IsNullableType(exprType))
            {
                exprType = exprType.GetGenericArguments()[0];
            }

            //
            // Predefined implicit identity or implicit numeric conversion
            // has to exist between underlying type S and underlying type T
            //

            // Handles probing
            if (ec == null)
            {
                return exprType == elementType ? EmptyExpression.Null : MakeImplicitNumericConversion(ec, null, exprType, elementType);
            }

            Expression unwrap = exprType != expr.Type ? Unwrap.Create(expr) : expr;
            Expression conv = exprType == elementType ? unwrap : MakeImplicitNumericConversion(ec, unwrap, exprType, elementType);
            if (conv == null)
            {
                return null;
            }

            if (exprType != expr.Type)
            {
                return new LiftedExpression(conv, unwrap, targetType).Resolve(ec);
            }

            // Do constant optimization for S -> T?
            if (unwrap is ConstantExpression)
            {
                conv = ((ConstantExpression)unwrap).ConvertImplicitly(elementType);
            }

            return Wrap.Create(conv, targetType);
        }

        internal static Expression MakeImplicitNumericConversion(ParseContext ec, Expression value, Type sourceType, Type targetType)
        {
            return !TypeUtils.IsImplicitNumericConversion(sourceType, targetType)
                ? null
                : TypeManager.IsEqual(sourceType, targetType) ? value : MakeImplicitConversion(ec, value, targetType, value.Span);
        }

        public static bool ImplicitConversionExists(ParseContext ec, Expression source, Type targetType)
        {
            return source is LambdaExpression sourceLambda
                ? sourceLambda.ImplicitStandardConversionExists(ec, targetType)
                : TypeUtils.IsImplicitlyConvertible(source.Type, targetType, true);
        }
    }
}