using System.Collections.Generic;

namespace Nori.Compiler
{
    public interface IExternCatalog
    {
        PropertyInfo ResolveProperty(string ownerUdonType, string propertyName);
        ExternSignature ResolveMethod(string ownerUdonType, string methodName, string[] argTypes);
        ExternSignature ResolveStaticMethod(string typeUdonName, string methodName, string[] argTypes);
        OperatorInfo ResolveOperator(TokenKind op, string leftType, string rightType);
        OperatorInfo ResolveUnaryOperator(TokenKind op, string operandType);
        bool IsKnownType(string udonType);
        IEnumerable<string> GetMethodNames(string ownerUdonType);
        IEnumerable<string> GetPropertyNames(string ownerUdonType);

        // Phase 2 additions
        List<ExternSignature> GetMethodOverloads(string ownerUdonType, string methodName);
        List<ExternSignature> GetStaticMethodOverloads(string typeUdonName, string methodName);
        EnumTypeInfo ResolveEnum(string udonType);
        bool IsEnumType(string udonType);
        CatalogTypeInfo GetTypeInfo(string udonType);
        ImplicitConversion GetImplicitConversion(string fromType, string toType);
        IEnumerable<string> GetStaticTypeNames();
        string GetClrTypeName(string udonType);
    }
}
