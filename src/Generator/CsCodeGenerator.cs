// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

using CppAst;

namespace Generator;

public static partial class CsCodeGenerator
{
    private static readonly HashSet<string> s_keywords = new()
    {
        "object",
        "event",
    };

    private static readonly Dictionary<string, string> s_csNameMappings = new()
    {
        { "bool", "bool" },
        { "Sint8", "sbyte" },
        { "uint8_t", "byte" },
        { "Uint8", "byte" },
        { "Uint16", "ushort" },
        { "uint16_t", "ushort" },
        { "uint32_t", "uint" },
        { "Uint32", "uint" },
        { "uint64_t", "ulong" },
        { "int8_t", "sbyte" },
        { "Sint32", "int" },
        { "int32_t", "int" },
        { "int16_t", "short" },
        { "Sint16", "short" },
        { "int64_t", "long" },
        { "int64_t*", "long*" },
        { "Sint64", "long" },
        { "Uint64", "ulong" },
        { "char", "byte" },
        { "size_t", "nuint" },
        { "intptr_t", "nint" },
        { "uintptr_t", "nuint" },

        { "SDL_FunctionPointer", "delegate* unmanaged<void>" },
        { "SDL_GUID", "Guid" },
        { "SDL_Point", "Point" },
        { "SDL_FPoint", "PointF" },
        { "SDL_Rect", "Rectangle" },
        { "SDL_FRect", "RectangleF" },
        { "SDL_Keycode", "int" },
        { "VkInstance", "nint" },
        { "VkSurfaceKHR", "ulong" },
        { "SDL_EGLDisplay", "nint" },
        { "SDL_EGLConfig", "nint" },
        { "SDL_EGLSurface", "nint" },
        { "SDL_MetalView", "nint" },
        { "HWND", "nint" },
        { "HDC", "nint" },
        { "HINSTANCE", "nint" },
        { "UINT", "uint" },
        { "WPARAM", "nuint" },
        { "LPARAM", "nint" },
        // Until we understand how to treat this
        { "SDL_BlitMap", "nint" },
        { "SDL_Time", "long" },
        { "SDL_eventaction", "SDL_EventAction" },
    };

    private static readonly HashSet<string> s_knownTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SDL_Rect",
        "SDL_Joystick",
        "SDL_Gamepad",
        "SDL_Window",
        "SDL_Renderer",
        "SDL_Sensor",
        "SDL_AudioStream",
        "SDL_Haptic",
        "SDL_IOStream",
    };

    private static CsCodeGeneratorOptions s_options = new();

    private static readonly List<CppEnum> s_collectedEnums = [];
    private static readonly Dictionary<string, CppFunctionType> s_collectedCallbackTypedes = [];
    private static readonly Dictionary<string, string> s_collectedHandles = [];
    private static readonly List<CppClass> s_collectedStructAndUnions = [];
    private static readonly List<CppFunction> s_collectedFunctions = [];

    public static void Collect(CppCompilation compilation)
    {
        CollectConstants(compilation);
        CollectEnums(compilation);
        CollectHandles(compilation);
        CollectStructAndUnions(compilation);
        CollectCommands(compilation);
    }

    public static void Generate(CsCodeGeneratorOptions options)
    {
        s_options = options;

        GenerateConstants();
        GenerateEnums();
        GenerateHandles();
        GenerateStructAndUnions();
        GenerateCommands();
    }

    public static void AddCsMapping(string typeName, string csTypeName)
    {
        s_csNameMappings[typeName] = csTypeName;
    }

    private static string NormalizeFieldName(string name)
    {
        if (s_keywords.Contains(name))
            return "@" + name;

        return name;
    }

    private static string GetCsCleanName(string name)
    {
        if (s_csNameMappings.TryGetValue(name, out string? mappedName))
        {
            return GetCsCleanName(mappedName);
        }
        else if (name.StartsWith("PFN"))
        {
            return "IntPtr";
        }

        return name;
    }

    private static string GetCsTypeName(CppType? type, bool isPointer = false)
    {
        if (type is CppPrimitiveType primitiveType)
        {
            return GetCsTypeName(primitiveType, isPointer);
        }

        if (type is CppQualifiedType qualifiedType)
        {
            return GetCsTypeName(qualifiedType.ElementType, isPointer);
        }

        if (type is CppEnum enumType)
        {
            string enumCsName = GetCsCleanName(enumType.Name);
            if (isPointer)
                return enumCsName + "*";

            return enumCsName;
        }

        if (type is CppTypedef typedef)
        {
            if (typedef.ElementType is CppClass classElementType)
            {
                return GetCsTypeName(classElementType, isPointer);
            }

            string typeDefCsName = GetCsCleanName(typedef.Name);
            if (isPointer && !s_knownTypes.Contains(typeDefCsName))
                return typeDefCsName + "*";

            return typeDefCsName;
        }

        if (type is CppClass @class)
        {
            string className = GetCsCleanName(@class.Name);
            if (className == "SDL_RWops"
                || className == "IDirect3DDevice9"
                || className == "ID3D11Device"
                || className == "ID3D12Device")
                return "nint";

            if (isPointer && !s_knownTypes.Contains(className))
                return className + "*";

            return className;
        }

        if (type is CppPointerType pointerType)
        {
            string className = GetCsTypeName(pointerType);
            if (isPointer && !s_knownTypes.Contains(className))
                return className + "*";

            return className;
        }

        if (type is CppArrayType arrayType)
        {
            return GetCsTypeName(arrayType.ElementType, true);
        }

        return string.Empty;
    }

    private static string GetCsTypeName(CppPrimitiveType primitiveType, bool isPointer)
    {
        switch (primitiveType.Kind)
        {
            case CppPrimitiveKind.Void:
                return isPointer ? "nint" : "void";

            case CppPrimitiveKind.Char:
                return isPointer ? "byte*" : "byte";

            case CppPrimitiveKind.Bool:
                return "bool";

            case CppPrimitiveKind.WChar:
                return isPointer ? "ushort*" : "ushort";

            case CppPrimitiveKind.Short:
                return isPointer ? "short*" : "short";
            case CppPrimitiveKind.Int:
                return isPointer ? "int*" : "int";

            case CppPrimitiveKind.LongLong:
                break;
            case CppPrimitiveKind.UnsignedChar:
                break;
            case CppPrimitiveKind.UnsignedShort:
                return isPointer ? "ushort*" : "ushort";
            case CppPrimitiveKind.UnsignedInt:
                return isPointer ? "uint*" : "uint";

            case CppPrimitiveKind.UnsignedLongLong:
                break;
            case CppPrimitiveKind.Float:
                return isPointer ? "float*" : "float";
            case CppPrimitiveKind.Double:
                return isPointer ? "double*" : "double";
            case CppPrimitiveKind.LongDouble:
                break;


            default:
                return string.Empty;
        }

        return string.Empty;
    }

    private static string GetCsTypeName(CppPointerType pointerType)
    {
        if (pointerType.ElementType is CppQualifiedType qualifiedType)
        {
            if (qualifiedType.ElementType is CppPrimitiveType primitiveType)
            {
                if (primitiveType.Kind == CppPrimitiveKind.Void && qualifiedType.Qualifier == CppTypeQualifier.Const)
                {
                    // const void*
                    return "void*";
                }

                return GetCsTypeName(primitiveType, true);
            }
            else if (qualifiedType.ElementType is CppClass @classType)
            {
                return GetCsTypeName(@classType, true);
            }
            else if (qualifiedType.ElementType is CppPointerType subPointerType)
            {
                return GetCsTypeName(subPointerType, true) + "*";
            }
            else if (qualifiedType.ElementType is CppTypedef typedef)
            {
                return GetCsTypeName(typedef, true);
            }
            else if (qualifiedType.ElementType is CppEnum @enum)
            {
                return GetCsTypeName(@enum, true);
            }

            return GetCsTypeName(qualifiedType.ElementType, true);
        }

        return GetCsTypeName(pointerType.ElementType, true);
    }
}
