using System.Collections.Generic;

namespace Nori.Compiler
{
    public class ExternSignature
    {
        public string Extern { get; }
        public string[] ParamTypes { get; }
        public string ReturnType { get; }
        public bool IsInstance { get; }
        public MethodParam[] Params { get; }

        public ExternSignature(string externSig, string[] paramTypes, string returnType, bool isInstance,
            MethodParam[] namedParams = null)
        {
            Extern = externSig;
            ParamTypes = paramTypes;
            ReturnType = returnType;
            IsInstance = isInstance;
            Params = namedParams;
        }
    }

    public class MethodParam
    {
        public string Name { get; }
        public string UdonType { get; }

        public MethodParam(string name, string udonType)
        {
            Name = name;
            UdonType = udonType;
        }
    }

    public class PropertyInfo
    {
        public string Type { get; }
        public ExternSignature Getter { get; }
        public ExternSignature Setter { get; }

        public PropertyInfo(string type, ExternSignature getter, ExternSignature setter = null)
        {
            Type = type;
            Getter = getter;
            Setter = setter;
        }
    }

    public class OperatorInfo
    {
        public string Extern { get; }
        public string ReturnType { get; }
        public string LeftType { get; }
        public string RightType { get; }

        public OperatorInfo(string externSig, string returnType, string leftType = null, string rightType = null)
        {
            Extern = externSig;
            ReturnType = returnType;
            LeftType = leftType;
            RightType = rightType;
        }
    }

    public class EnumTypeInfo
    {
        public string UdonType { get; }
        public string UnderlyingType { get; }
        public Dictionary<string, int> Values { get; }

        public EnumTypeInfo(string udonType, string underlyingType, Dictionary<string, int> values)
        {
            UdonType = udonType;
            UnderlyingType = underlyingType;
            Values = values;
        }
    }

    public class CatalogTypeInfo
    {
        public string UdonType { get; }
        public string DotNetType { get; }
        public string BaseType { get; }
        public bool IsEnum { get; }

        public CatalogTypeInfo(string udonType, string dotNetType, string baseType = null, bool isEnum = false)
        {
            UdonType = udonType;
            DotNetType = dotNetType;
            BaseType = baseType;
            IsEnum = isEnum;
        }
    }

    public class ImplicitConversion
    {
        public string FromType { get; }
        public string ToType { get; }
        public string ConversionExtern { get; }

        public ImplicitConversion(string fromType, string toType, string conversionExtern)
        {
            FromType = fromType;
            ToType = toType;
            ConversionExtern = conversionExtern;
        }

        /// <summary>
        /// Static table of built-in implicit conversions (always available).
        /// </summary>
        public static ImplicitConversion Lookup(string fromType, string toType)
        {
            if (fromType == "SystemInt32" && toType == "SystemSingle")
                return new ImplicitConversion(fromType, toType,
                    "SystemConvert.__ToSingle__SystemObject__SystemSingle");
            if (fromType == "SystemInt32" && toType == "SystemDouble")
                return new ImplicitConversion(fromType, toType,
                    "SystemConvert.__ToDouble__SystemObject__SystemDouble");
            if (fromType == "SystemSingle" && toType == "SystemDouble")
                return new ImplicitConversion(fromType, toType,
                    "SystemConvert.__ToDouble__SystemObject__SystemDouble");
            return null;
        }
    }
}
