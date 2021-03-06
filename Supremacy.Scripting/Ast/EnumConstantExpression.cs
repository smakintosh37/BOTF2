using System;

using Supremacy.Annotations;
using Supremacy.Scripting.Utility;

namespace Supremacy.Scripting.Ast
{
    public class EnumConstantExpression : ConstantExpression
    {
        private Type _enumType;
        private ConstantExpression _child;

        public EnumConstantExpression([NotNull] ConstantExpression child, Type enumType)
        {
            _child = child ?? throw new ArgumentNullException("child");
            _enumType = enumType;

            Type = _child.Type;
        }

        internal EnumConstantExpression()
        {
            // For cloning purposes only.
        }

        public override void CloneTo<T>(CloneContext cloneContext, T target)
        {
            base.CloneTo(cloneContext, target);

            if (!(target is EnumConstantExpression clone))
            {
                return;
            }

            clone._child = Clone(cloneContext, _child);
            clone._enumType = _enumType;
        }

        public ConstantExpression Child => _child;

        public override bool IsZeroInteger => _child.IsZeroInteger;

        public override object Value => Enum.ToObject(_enumType, _child.Value);

        public override void Walk(AstVisitor prefix, AstVisitor postfix)
        {
            Walk(ref _child, prefix, postfix);

            Type = _child.Type;
        }

        public override ConstantExpression ConvertExplicitly(bool inCheckedContext, Type targetType)
        {
            return _child.Type == targetType ? _child : _child.ConvertExplicitly(inCheckedContext, targetType);
        }

        public override ConstantExpression ConvertImplicitly(Type type)
        {
            Type thisType = TypeManager.DropGenericTypeArguments(Type);

            type = TypeManager.DropGenericTypeArguments(type);

            if (thisType == type)
            {
                Type childType = TypeManager.DropGenericTypeArguments(_child.Type);

                if (type.UnderlyingSystemType != childType)
                {
                    _child = _child.ConvertImplicitly(type.UnderlyingSystemType);
                }

                return this;
            }

            return !TypeUtils.IsImplicitlyConvertible(Type, type) ? null : _child.ConvertImplicitly(type);
        }
    }
}