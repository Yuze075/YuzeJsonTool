#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace YuzeToolkit.Json.SourceGeneration
{
    [Generator]
    public sealed class JsonSourceGenerator : ISourceGenerator
    {
        private const string SerializableAttributeName = "YuzeToolkit.Json.JsonSerializableAttribute";
        private const string NameAttributeName = "YuzeToolkit.Json.JsonNameAttribute";
        private const string IgnoreAttributeName = "YuzeToolkit.Json.JsonIgnoreAttribute";
        private const string RequiredAttributeName = "YuzeToolkit.Json.JsonRequiredAttribute";
        private const string ConverterAttributeName = "YuzeToolkit.Json.JsonConverterAttribute";
        private const string GenericConverterName = "YuzeToolkit.Json.JsonConverter<T>";

        private static readonly DiagnosticDescriptor ContextMustBePartial = CreateError(
            "YJSG001", "JSON context must be partial",
            "JSON source-generation context '{0}' must be a partial, non-generic class declared at namespace scope");

        private static readonly DiagnosticDescriptor InvalidTargetType = CreateError(
            "YJSG002", "JSON target type is unsupported",
            "Type '{0}' cannot use generated JSON conversion: {1}");

        private static readonly DiagnosticDescriptor MissingConstructor = CreateError(
            "YJSG003", "JSON target requires a constructor",
            "Type '{0}' requires a public or internal parameterless constructor for generated deserialization");

        private static readonly DiagnosticDescriptor DuplicateJsonName = CreateError(
            "YJSG004", "Generated JSON member names must be unique",
            "Type '{0}' contains generated JSON members with the duplicate or case-ambiguous name '{1}'");

        private static readonly DiagnosticDescriptor InvalidConverter = CreateError(
            "YJSG005", "JSON converter is incompatible",
            "Converter '{0}' must derive from JsonConverter<{1}> and expose a public or internal parameterless constructor");

        private static readonly DiagnosticDescriptor RequiredMemberCannotBeWritten = CreateError(
            "YJSG006", "Required JSON member cannot be assigned",
            "Required JSON member '{0}' on type '{1}' must have a writable public field or public setter");

        private static readonly DiagnosticDescriptor ReservedContextMember = CreateError(
            "YJSG007", "JSON context member is reserved",
            "JSON context '{0}' already declares generated member '{1}'");

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new Receiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (!(context.SyntaxReceiver is Receiver receiver)) return;
            var serializableAttribute = context.Compilation.GetTypeByMetadataName(SerializableAttributeName);
            if (serializableAttribute == null) return;

            var processed = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var declaration in receiver.Candidates)
            {
                var semanticModel = context.Compilation.GetSemanticModel(declaration.SyntaxTree);
                if (!(semanticModel.GetDeclaredSymbol(declaration) is INamedTypeSymbol contextType) ||
                    !processed.Add(contextType)) continue;

                var attributes = contextType.GetAttributes()
                    .Where(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, serializableAttribute))
                    .ToArray();
                if (attributes.Length == 0) continue;

                if (!IsValidContext(declaration, contextType))
                {
                    Report(context, ContextMustBePartial, declaration.Identifier.GetLocation(),
                        contextType.ToDisplayString());
                    continue;
                }

                if (HasReservedMembers(contextType, context, declaration)) continue;

                var targets = new List<TargetModel>();
                var targetSymbols = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
                var hasError = false;
                foreach (var attribute in attributes)
                {
                    if (attribute.ConstructorArguments.Length == 0 ||
                        !(attribute.ConstructorArguments[0].Value is ITypeSymbol targetType))
                    {
                        hasError = true;
                        Report(context, InvalidTargetType, declaration.Identifier.GetLocation(),
                            "<unknown>", "the attribute does not contain a concrete type");
                        continue;
                    }
                    if (!targetSymbols.Add(targetType)) continue;
                    var target = BuildTarget(context, declaration.Identifier.GetLocation(), targetType);
                    if (target == null) hasError = true;
                    else targets.Add(target);
                }

                if (hasError || targets.Count == 0) continue;
                var source = EmitContext(contextType, targets);
                context.AddSource(GetHintName(contextType), SourceText.From(source, Encoding.UTF8));
            }
        }

        private static bool IsValidContext(ClassDeclarationSyntax declaration, INamedTypeSymbol type)
        {
            return declaration.Modifiers.Any(SyntaxKind.PartialKeyword) &&
                   type.ContainingType == null &&
                   type.Arity == 0 &&
                   type.TypeKind == TypeKind.Class;
        }

        private static bool HasReservedMembers(
            INamedTypeSymbol contextType,
            GeneratorExecutionContext context,
            ClassDeclarationSyntax declaration)
        {
            var hasError = false;
            foreach (var name in new[] { "Resolver", "GetTypeInfo" })
            {
                if (contextType.GetMembers(name).Length == 0) continue;
                Report(context, ReservedContextMember, declaration.Identifier.GetLocation(),
                    contextType.ToDisplayString(), name);
                hasError = true;
            }
            return hasError;
        }

        private static TargetModel? BuildTarget(
            GeneratorExecutionContext context,
            Location location,
            ITypeSymbol targetType)
        {
            if (!(targetType is INamedTypeSymbol namedType))
            {
                Report(context, InvalidTargetType, location, targetType.ToDisplayString(),
                    "it must be an accessible, concrete, closed class or struct");
                return null;
            }
            if (ContainsTypeParameter(namedType) ||
                namedType.TypeKind != TypeKind.Class && namedType.TypeKind != TypeKind.Struct ||
                namedType.IsAbstract ||
                !IsAccessibleFromGeneratedCode(namedType))
            {
                Report(context, InvalidTargetType, location, targetType.ToDisplayString(),
                    "it must be an accessible, concrete, closed class or struct");
                return null;
            }

            var typeConverterAttribute = GetAttribute(namedType, ConverterAttributeName);
            var typeConverter = GetConverterType(typeConverterAttribute);
            if (typeConverterAttribute != null)
            {
                if (typeConverter == null || !IsValidConverter(typeConverter, namedType))
                {
                    Report(context, InvalidConverter, location, typeConverter?.ToDisplayString() ?? "<invalid>",
                        namedType.ToDisplayString());
                    return null;
                }
                return new TargetModel(namedType, typeConverter, Array.Empty<MemberModel>());
            }

            if (namedType.TypeKind == TypeKind.Class && !HasUsableParameterlessConstructor(namedType))
            {
                Report(context, MissingConstructor, location, namedType.ToDisplayString());
                return null;
            }

            var members = BuildMembers(context, location, namedType, out var hasError);
            return hasError ? null : new TargetModel(namedType, null, members);
        }

        private static bool ContainsTypeParameter(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.TypeParameter) return true;
            if (type is IArrayTypeSymbol arrayType) return ContainsTypeParameter(arrayType.ElementType);
            if (!(type is INamedTypeSymbol namedType)) return false;
            foreach (var argument in namedType.TypeArguments)
            {
                if (ContainsTypeParameter(argument)) return true;
            }
            return false;
        }

        private static IReadOnlyList<MemberModel> BuildMembers(
            GeneratorExecutionContext context,
            Location fallbackLocation,
            INamedTypeSymbol type,
            out bool hasError)
        {
            hasError = false;
            var members = new List<MemberModel>();
            var propertyNames = new HashSet<string>(StringComparer.Ordinal);

            for (var current = type; current != null && current.SpecialType != SpecialType.System_Object;
                 current = current.BaseType)
            {
                foreach (var symbol in current.GetMembers())
                {
                    if (HasAttribute(symbol, IgnoreAttributeName)) continue;
                    if (symbol is IFieldSymbol field)
                    {
                        if (field.IsStatic || field.IsConst || field.DeclaredAccessibility != Accessibility.Public)
                            continue;
                        var member = CreateMember(field, field.Type, canRead: true, canWrite: !field.IsReadOnly,
                            isField: true);
                        if (!ValidateMember(context, fallbackLocation, type, member)) hasError = true;
                        else members.Add(member);
                    }
                    else if (symbol is IPropertySymbol property)
                    {
                        if (property.IsStatic || property.IsIndexer ||
                            property.DeclaredAccessibility != Accessibility.Public ||
                            !propertyNames.Add(property.Name)) continue;
                        var canRead = property.GetMethod?.DeclaredAccessibility == Accessibility.Public;
                        var canWrite = property.SetMethod?.DeclaredAccessibility == Accessibility.Public &&
                                       !property.SetMethod.IsInitOnly;
                        if (!canRead && !canWrite) continue;
                        var member = CreateMember(property, property.Type, canRead, canWrite, isField: false);
                        if (!ValidateMember(context, fallbackLocation, type, member)) hasError = true;
                        else members.Add(member);
                    }
                }
            }

            var names = new Dictionary<string, MemberModel>(StringComparer.Ordinal);
            var namesIgnoreCase = new Dictionary<string, MemberModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in members)
            {
                if (names.ContainsKey(member.JsonName) || namesIgnoreCase.ContainsKey(member.JsonName))
                {
                    Report(context, DuplicateJsonName, member.Symbol.Locations.FirstOrDefault() ?? fallbackLocation,
                        type.ToDisplayString(), member.JsonName);
                    hasError = true;
                }
                else
                {
                    names.Add(member.JsonName, member);
                    namesIgnoreCase.Add(member.JsonName, member);
                }
            }
            return members;
        }

        private static MemberModel CreateMember(
            ISymbol symbol,
            ITypeSymbol type,
            bool canRead,
            bool canWrite,
            bool isField)
        {
            var nameAttribute = GetAttribute(symbol, NameAttributeName);
            var converterAttribute = GetAttribute(symbol, ConverterAttributeName);
            var jsonName = nameAttribute != null && nameAttribute.ConstructorArguments.Length > 0
                ? nameAttribute.ConstructorArguments[0].Value as string ?? symbol.Name
                : symbol.Name;
            return new MemberModel(symbol, type, jsonName, canRead, canWrite, isField,
                HasAttribute(symbol, RequiredAttributeName), converterAttribute != null,
                GetConverterType(converterAttribute));
        }

        private static bool ValidateMember(
            GeneratorExecutionContext context,
            Location fallbackLocation,
            INamedTypeSymbol declaringType,
            MemberModel member)
        {
            var location = member.Symbol.Locations.FirstOrDefault() ?? fallbackLocation;
            if (member.Required && !member.CanWrite)
            {
                Report(context, RequiredMemberCannotBeWritten, location,
                    member.Symbol.Name, declaringType.ToDisplayString());
                return false;
            }
            if (member.HasConverterAttribute &&
                (member.ConverterType == null || !IsValidConverter(member.ConverterType, member.Type)))
            {
                Report(context, InvalidConverter, location,
                    member.ConverterType?.ToDisplayString() ?? "<invalid>",
                    member.Type.ToDisplayString());
                return false;
            }
            return true;
        }

        private static INamedTypeSymbol? GetConverterType(AttributeData? attribute)
        {
            if (attribute == null || attribute.ConstructorArguments.Length == 0) return null;
            return attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
        }

        private static bool IsValidConverter(INamedTypeSymbol converterType, ITypeSymbol targetType)
        {
            if (converterType.IsAbstract || !IsAccessibleFromGeneratedCode(converterType) ||
                !HasUsableParameterlessConstructor(converterType))
                return false;
            for (var current = converterType; current != null; current = current.BaseType)
            {
                if (!current.IsGenericType ||
                    current.ConstructedFrom.ToDisplayString() != GenericConverterName) continue;
                return SymbolEqualityComparer.Default.Equals(current.TypeArguments[0], targetType);
            }
            return false;
        }

        private static bool HasUsableParameterlessConstructor(INamedTypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Struct) return true;
            foreach (var constructor in type.InstanceConstructors)
            {
                if (constructor.Parameters.Length != 0) continue;
                if (constructor.DeclaredAccessibility == Accessibility.Public ||
                    constructor.DeclaredAccessibility == Accessibility.Internal ||
                    constructor.DeclaredAccessibility == Accessibility.ProtectedOrInternal)
                    return true;
            }
            return false;
        }

        private static bool IsAccessibleFromGeneratedCode(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol? current = type; current != null; current = current.ContainingType)
            {
                if (current.DeclaredAccessibility != Accessibility.Public &&
                    current.DeclaredAccessibility != Accessibility.Internal &&
                    current.DeclaredAccessibility != Accessibility.ProtectedOrInternal)
                    return false;
            }
            return true;
        }

        private static AttributeData? GetAttribute(ISymbol symbol, string metadataName)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == metadataName) return attribute;
            }
            return null;
        }

        private static bool HasAttribute(ISymbol symbol, string metadataName) =>
            GetAttribute(symbol, metadataName) != null;

        private static string EmitContext(INamedTypeSymbol contextType, IReadOnlyList<TargetModel> targets)
        {
            var source = new StringBuilder(32768);
            source.AppendLine("// <auto-generated />");
            source.AppendLine("#nullable enable");
            var namespaceName = contextType.ContainingNamespace.IsGlobalNamespace
                ? null
                : contextType.ContainingNamespace.ToDisplayString();
            if (namespaceName != null)
            {
                source.Append("namespace ").Append(namespaceName).AppendLine();
                source.AppendLine("{");
            }

            source.Append("    ").Append(GetAccessibility(contextType)).Append(' ');
            if (contextType.IsStatic) source.Append("static ");
            source.Append("partial class ").Append(EscapeIdentifier(contextType.Name)).AppendLine();
            source.AppendLine("    {");
            source.AppendLine("        private static readonly __YuzeJsonGeneratedResolver s_yuzeJsonResolver = new __YuzeJsonGeneratedResolver();");
            source.AppendLine();
            source.AppendLine("        public static global::YuzeToolkit.Json.IJsonTypeResolver Resolver => s_yuzeJsonResolver;");
            source.AppendLine();
            source.AppendLine("        public static global::YuzeToolkit.Json.JsonTypeInfo<T> GetTypeInfo<T>()");
            source.AppendLine("        {");
            source.AppendLine("            if (s_yuzeJsonResolver.TryGetTypeInfo(typeof(T), out var typeInfo) &&");
            source.AppendLine("                typeInfo is global::YuzeToolkit.Json.JsonTypeInfo<T> typedTypeInfo)");
            source.AppendLine("                return typedTypeInfo;");
            source.AppendLine("            throw new global::System.NotSupportedException($\"Type {typeof(T)} is not part of this generated JSON context.\");");
            source.AppendLine("        }");
            source.AppendLine();
            EmitResolver(source, targets);

            for (var index = 0; index < targets.Count; index++)
            {
                if (targets[index].ConverterType == null)
                    EmitConverter(source, targets[index], index, targets);
            }

            source.AppendLine("    }");
            if (namespaceName != null) source.AppendLine("}");
            return source.ToString();
        }

        private static void EmitResolver(StringBuilder source, IReadOnlyList<TargetModel> targets)
        {
            source.AppendLine("        private sealed class __YuzeJsonGeneratedResolver : global::YuzeToolkit.Json.IJsonTypeResolver");
            source.AppendLine("        {");
            for (var index = 0; index < targets.Count; index++)
            {
                source.Append("            internal readonly global::YuzeToolkit.Json.JsonTypeInfo<")
                    .Append(TypeName(targets[index].Type)).Append("> TypeInfo").Append(index).AppendLine(";");
            }
            source.AppendLine();
            source.AppendLine("            internal __YuzeJsonGeneratedResolver()");
            source.AppendLine("            {");
            for (var index = 0; index < targets.Count; index++)
            {
                var target = targets[index];
                source.Append("                TypeInfo").Append(index)
                    .Append(" = global::YuzeToolkit.Json.JsonTypeInfo<")
                    .Append(TypeName(target.Type)).Append(">.From(");
                if (target.ConverterType != null)
                    source.Append("new ").Append(TypeName(target.ConverterType)).Append("()");
                else source.Append("__YuzeJsonConverter").Append(index).Append(".Instance");
                source.AppendLine(", this);");
            }
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            public bool TryGetTypeInfo(global::System.Type type, out global::YuzeToolkit.Json.JsonTypeInfo typeInfo)");
            source.AppendLine("            {");
            for (var index = 0; index < targets.Count; index++)
            {
                source.Append("                if (type == typeof(").Append(TypeName(targets[index].Type)).AppendLine("))");
                source.Append("                { typeInfo = TypeInfo").Append(index).AppendLine("; return true; }");
            }
            source.AppendLine("                typeInfo = null!;");
            source.AppendLine("                return false;");
            source.AppendLine("            }");
            source.AppendLine("        }");
            source.AppendLine();
        }

        private static void EmitConverter(
            StringBuilder source,
            TargetModel target,
            int converterIndex,
            IReadOnlyList<TargetModel> targets)
        {
            var typeName = TypeName(target.Type);
            var readTypeName = target.Type.IsReferenceType ? typeName + "?" : typeName;
            source.Append("        private sealed class __YuzeJsonConverter").Append(converterIndex)
                .Append(" : global::YuzeToolkit.Json.JsonConverter<").Append(typeName).AppendLine(">");
            source.AppendLine("        {");
            source.Append("            internal static readonly __YuzeJsonConverter").Append(converterIndex)
                .Append(" Instance = new __YuzeJsonConverter").Append(converterIndex).AppendLine("();");
            for (var index = 0; index < target.Members.Count; index++)
            {
                var member = target.Members[index];
                if (member.ConverterType == null) continue;
                source.Append("            private static readonly ").Append(TypeName(member.ConverterType))
                    .Append(" s_memberConverter").Append(index).Append(" = new ")
                    .Append(TypeName(member.ConverterType)).AppendLine("();");
            }
            source.AppendLine();
            EmitRead(source, target, readTypeName, targets);
            source.AppendLine();
            EmitWrite(source, target, typeName, readTypeName, targets);
            source.AppendLine("        }");
            source.AppendLine();
        }

        private static void EmitRead(
            StringBuilder source,
            TargetModel target,
            string readTypeName,
            IReadOnlyList<TargetModel> targets)
        {
            var typeName = TypeName(target.Type);
            source.Append("            public override ").Append(readTypeName)
                .AppendLine(" Read(ref global::YuzeToolkit.Json.JsonReader reader, global::YuzeToolkit.Json.JsonSerializationContext context)");
            source.AppendLine("            {");
            source.AppendLine("                if (reader.TokenType == global::YuzeToolkit.Json.JsonTokenType.Null)");
            if (target.Type.IsReferenceType) source.AppendLine("                    return null;");
            else source.Append("                    throw new global::YuzeToolkit.Json.JsonException(\"JSON null cannot be assigned to ")
                    .Append(EscapeForString(typeName)).AppendLine(".\");");
            source.AppendLine("                if (reader.TokenType != global::YuzeToolkit.Json.JsonTokenType.StartObject)");
            source.Append("                    throw new global::YuzeToolkit.Json.JsonException(\"Expected JSON object for ")
                .Append(EscapeForString(typeName)).AppendLine(".\");");
            source.Append("                var value = new ").Append(typeName).AppendLine("();");
            source.AppendLine("                global::System.Collections.Generic.HashSet<string>? names =");
            source.AppendLine("                    context.Options.DuplicatePropertyHandling == global::YuzeToolkit.Json.JsonDuplicatePropertyHandling.Error");
            source.AppendLine("                        ? new global::System.Collections.Generic.HashSet<string>(global::System.StringComparer.Ordinal)");
            source.AppendLine("                        : null;");
            for (var index = 0; index < target.Members.Count; index++)
            {
                if (target.Members[index].Required)
                    source.Append("                var required").Append(index).AppendLine(" = false;");
            }
            source.AppendLine("                while (reader.Read() && reader.TokenType != global::YuzeToolkit.Json.JsonTokenType.EndObject)");
            source.AppendLine("                {");
            source.AppendLine("                    if (reader.TokenType != global::YuzeToolkit.Json.JsonTokenType.PropertyName)");
            source.AppendLine("                        throw new global::YuzeToolkit.Json.JsonException(\"Expected a JSON property name.\");");
            source.AppendLine("                    string? propertyName = null;");
            source.AppendLine("                    int propertyIndex;");
            source.AppendLine("                    if (!context.Options.PropertyNameCaseInsensitive)");
            source.AppendLine("                    {");
            EmitPropertyIndex(source, target.Members, "reader.ValueTextEquals", 24);
            source.AppendLine("                    }");
            source.AppendLine("                    else");
            source.AppendLine("                    {");
            source.AppendLine("                        propertyName = reader.GetString();");
            EmitPropertyIndex(source, target.Members, "global::System.String.Equals(propertyName,", 24, caseInsensitive: true);
            source.AppendLine("                    }");
            source.AppendLine("                    if (names != null)");
            source.AppendLine("                    {");
            source.AppendLine("                        if (propertyName == null) propertyName = reader.GetString();");
            source.AppendLine("                        if (!names.Add(propertyName))");
            source.AppendLine("                            throw new global::YuzeToolkit.Json.JsonException($\"Duplicate JSON property '{propertyName}'.\");");
            source.AppendLine("                    }");
            source.AppendLine("                    if (propertyIndex < 0 &&");
            source.AppendLine("                        context.Options.UnknownMemberHandling == global::YuzeToolkit.Json.JsonUnknownMemberHandling.Error &&");
            source.AppendLine("                        propertyName == null)");
            source.AppendLine("                        propertyName = reader.GetString();");
            source.AppendLine("                    if (!reader.Read())");
            source.AppendLine("                        throw new global::YuzeToolkit.Json.JsonException(\"JSON property is missing its value.\");");
            source.AppendLine("                    switch (propertyIndex)");
            source.AppendLine("                    {");
            for (var index = 0; index < target.Members.Count; index++)
            {
                var member = target.Members[index];
                source.Append("                        case ").Append(index).AppendLine(":");
                if (!member.CanWrite)
                {
                    EmitUnknownMember(source, member, 28);
                    source.AppendLine("                            break;");
                    continue;
                }
                source.Append("                            if (!context.Options.Include")
                    .Append(member.IsField ? "Fields" : "Properties").AppendLine(")");
                source.AppendLine("                            {");
                EmitUnknownMember(source, member, 32);
                source.AppendLine("                                break;");
                source.AppendLine("                            }");
                EmitReadAssignment(source, member, index, targets);
                if (member.Required)
                    source.Append("                            required").Append(index).AppendLine(" = true;");
                source.AppendLine("                            break;");
            }
            source.AppendLine("                        default:");
            source.AppendLine("                            if (context.Options.UnknownMemberHandling == global::YuzeToolkit.Json.JsonUnknownMemberHandling.Error)");
            source.AppendLine("                            {");
            source.AppendLine("                                if (propertyName == null) propertyName = \"<unknown>\";");
            source.Append("                                throw new global::YuzeToolkit.Json.JsonException($\"Type ")
                .Append(EscapeForString(typeName)).AppendLine(" does not contain a writable JSON member named '{propertyName}'.\");");
            source.AppendLine("                            }");
            source.AppendLine("                            reader.SkipCurrentValue();");
            source.AppendLine("                            break;");
            source.AppendLine("                    }");
            source.AppendLine("                }");
            for (var index = 0; index < target.Members.Count; index++)
            {
                var member = target.Members[index];
                if (!member.Required) continue;
                source.Append("                if (!required").Append(index).AppendLine(")");
                source.Append("                    throw new global::YuzeToolkit.Json.JsonException(\"Required JSON member '")
                    .Append(EscapeForString(member.JsonName)).Append("' is missing for ")
                    .Append(EscapeForString(typeName)).AppendLine(".\");");
            }
            source.AppendLine("                return value;");
            source.AppendLine("            }");
        }

        private static void EmitPropertyIndex(
            StringBuilder source,
            IReadOnlyList<MemberModel> members,
            string comparison,
            int indent,
            bool caseInsensitive = false)
        {
            var spaces = new string(' ', indent);
            if (members.Count == 0)
            {
                source.Append(spaces).AppendLine("propertyIndex = -1;");
                return;
            }
            for (var index = 0; index < members.Count; index++)
            {
                source.Append(spaces).Append(index == 0 ? "propertyIndex = " : "                ");
                if (caseInsensitive)
                {
                    source.Append(comparison).Append(' ').Append(StringLiteral(members[index].JsonName))
                        .Append(", global::System.StringComparison.OrdinalIgnoreCase)");
                }
                else
                {
                    source.Append(comparison).Append('(').Append(StringLiteral(members[index].JsonName)).Append(')');
                }
                source.Append(" ? ").Append(index).AppendLine(" :");
            }
            source.Append(spaces).Append("                -1;").AppendLine();
        }

        private static void EmitUnknownMember(StringBuilder source, MemberModel member, int indent)
        {
            var spaces = new string(' ', indent);
            source.Append(spaces).AppendLine("if (context.Options.UnknownMemberHandling == global::YuzeToolkit.Json.JsonUnknownMemberHandling.Error)");
            source.Append(spaces).Append("    throw new global::YuzeToolkit.Json.JsonException(\"JSON member '")
                .Append(EscapeForString(member.JsonName)).AppendLine("' is not writable with the current options.\");");
            source.Append(spaces).AppendLine("reader.SkipCurrentValue();");
        }

        private static string ReadExpression(
            ITypeSymbol type,
            INamedTypeSymbol? converterType,
            int converterIndex,
            IReadOnlyList<TargetModel> targets)
        {
            if (converterType != null)
                return "s_memberConverter" + converterIndex + ".Read(ref reader, context)!";
            var targetIndex = FindTargetIndex(type, targets);
            if (targetIndex >= 0)
                return "s_yuzeJsonResolver.TypeInfo" + targetIndex + ".Converter.Read(ref reader, context)!";
            var specialType = type.SpecialType;
            switch (specialType)
            {
                case SpecialType.System_Boolean: return "reader.GetBoolean()";
                case SpecialType.System_Byte: return "checked((byte)reader.GetUInt64())";
                case SpecialType.System_SByte: return "checked((sbyte)reader.GetInt64())";
                case SpecialType.System_Int16: return "checked((short)reader.GetInt64())";
                case SpecialType.System_UInt16: return "checked((ushort)reader.GetUInt64())";
                case SpecialType.System_Int32: return "checked((int)reader.GetInt64())";
                case SpecialType.System_UInt32: return "checked((uint)reader.GetUInt64())";
                case SpecialType.System_Int64: return "reader.GetInt64()";
                case SpecialType.System_UInt64: return "reader.GetUInt64()";
                case SpecialType.System_Double: return "reader.GetDouble()";
                case SpecialType.System_Decimal: return "reader.GetDecimal()";
                default:
                    return "context.ReadValue<" + TypeName(type) + ">(ref reader)!";
            }
        }

        private static void EmitReadAssignment(
            StringBuilder source,
            MemberModel member,
            int memberIndex,
            IReadOnlyList<TargetModel> targets)
        {
            var destination = "value." + EscapeIdentifier(member.Symbol.Name);
            if (member.ConverterType != null)
            {
                source.Append("                            ").Append(destination).Append(" = ")
                    .Append(ReadExpression(member.Type, member.ConverterType, memberIndex, targets)).AppendLine(";");
                return;
            }

            if (member.Type is IArrayTypeSymbol arrayType && arrayType.Rank == 1)
            {
                EmitReadArrayOrList(source, destination, arrayType.ElementType, memberIndex, targets, isArray: true);
                return;
            }
            if (TryGetListElement(member.Type, out var listElement))
            {
                EmitReadArrayOrList(source, destination, listElement, memberIndex, targets, isArray: false);
                return;
            }
            if (TryGetDictionaryValue(member.Type, out var dictionaryValue))
            {
                EmitReadDictionary(source, destination, dictionaryValue, memberIndex, targets);
                return;
            }

            source.Append("                            ").Append(destination).Append(" = ")
                .Append(ReadExpression(member.Type, null, memberIndex, targets)).AppendLine(";");
        }

        private static void EmitReadArrayOrList(
            StringBuilder source,
            string destination,
            ITypeSymbol elementType,
            int memberIndex,
            IReadOnlyList<TargetModel> targets,
            bool isArray)
        {
            var elementName = TypeName(elementType);
            source.AppendLine("                            if (reader.TokenType == global::YuzeToolkit.Json.JsonTokenType.Null)");
            source.Append("                                ").Append(destination).AppendLine(" = null!;");
            source.AppendLine("                            else");
            source.AppendLine("                            {");
            source.AppendLine("                                if (reader.TokenType != global::YuzeToolkit.Json.JsonTokenType.StartArray)");
            source.AppendLine("                                    throw new global::YuzeToolkit.Json.JsonException(\"Expected a JSON array.\");");
            source.Append("                                var collection").Append(memberIndex)
                .Append(" = new global::System.Collections.Generic.List<").Append(elementName).AppendLine(">();");
            source.AppendLine("                                while (reader.Read() && reader.TokenType != global::YuzeToolkit.Json.JsonTokenType.EndArray)");
            source.Append("                                    collection").Append(memberIndex).Append(".Add(")
                .Append(ReadExpression(elementType, null, memberIndex, targets)).AppendLine(");");
            source.Append("                                ").Append(destination).Append(" = collection")
                .Append(memberIndex).Append(isArray ? ".ToArray();" : ";").AppendLine();
            source.AppendLine("                            }");
        }

        private static void EmitReadDictionary(
            StringBuilder source,
            string destination,
            ITypeSymbol valueType,
            int memberIndex,
            IReadOnlyList<TargetModel> targets)
        {
            var valueName = TypeName(valueType);
            source.AppendLine("                            if (reader.TokenType == global::YuzeToolkit.Json.JsonTokenType.Null)");
            source.Append("                                ").Append(destination).AppendLine(" = null!;");
            source.AppendLine("                            else");
            source.AppendLine("                            {");
            source.AppendLine("                                if (reader.TokenType != global::YuzeToolkit.Json.JsonTokenType.StartObject)");
            source.AppendLine("                                    throw new global::YuzeToolkit.Json.JsonException(\"Expected a JSON object for a dictionary.\");");
            source.Append("                                var dictionary").Append(memberIndex)
                .Append(" = new global::System.Collections.Generic.Dictionary<string, ").Append(valueName)
                .AppendLine(">();");
            source.AppendLine("                                while (reader.Read() && reader.TokenType != global::YuzeToolkit.Json.JsonTokenType.EndObject)");
            source.AppendLine("                                {");
            source.AppendLine("                                    if (reader.TokenType != global::YuzeToolkit.Json.JsonTokenType.PropertyName)");
            source.AppendLine("                                        throw new global::YuzeToolkit.Json.JsonException(\"Expected a dictionary property name.\");");
            source.Append("                                    var dictionaryKey").Append(memberIndex).AppendLine(" = reader.GetString();");
            source.Append("                                    if (context.Options.DuplicatePropertyHandling == global::YuzeToolkit.Json.JsonDuplicatePropertyHandling.Error && dictionary")
                .Append(memberIndex).Append(".ContainsKey(dictionaryKey").Append(memberIndex).AppendLine("))");
            source.Append("                                        throw new global::YuzeToolkit.Json.JsonException($\"Duplicate JSON property '{dictionaryKey")
                .Append(memberIndex).AppendLine("}'.\");");
            source.AppendLine("                                    if (!reader.Read())");
            source.AppendLine("                                        throw new global::YuzeToolkit.Json.JsonException(\"Expected a dictionary value.\");");
            source.Append("                                    dictionary").Append(memberIndex).Append("[dictionaryKey")
                .Append(memberIndex).Append("] = ").Append(ReadExpression(valueType, null, memberIndex, targets))
                .AppendLine(";");
            source.AppendLine("                                }");
            source.Append("                                ").Append(destination).Append(" = dictionary")
                .Append(memberIndex).AppendLine(";");
            source.AppendLine("                            }");
        }

        private static void EmitWrite(
            StringBuilder source,
            TargetModel target,
            string typeName,
            string writeTypeName,
            IReadOnlyList<TargetModel> targets)
        {
            source.Append("            public override void Write(global::YuzeToolkit.Json.JsonWriter writer, ")
                .Append(writeTypeName)
                .AppendLine(" value, global::YuzeToolkit.Json.JsonSerializationContext context)");
            source.AppendLine("            {");
            if (target.Type.IsReferenceType)
            {
                source.AppendLine("                if (value == null)");
                source.AppendLine("                {");
                source.AppendLine("                    writer.WriteNullValue();");
                source.AppendLine("                    return;");
                source.AppendLine("                }");
            }
            source.AppendLine("                writer.WriteStartObject();");
            for (var index = 0; index < target.Members.Count; index++)
            {
                var member = target.Members[index];
                if (!member.CanRead) continue;
                var memberType = TypeName(member.Type);
                source.Append("                if (context.Options.Include")
                    .Append(member.IsField ? "Fields" : "Properties").AppendLine(")");
                source.AppendLine("                {");
                source.Append("                    var memberValue").Append(index).Append(" = value.")
                    .Append(EscapeIdentifier(member.Symbol.Name)).AppendLine(";");
                source.Append("                    if (");
                if (CanBeNull(member.Type))
                {
                    source.Append("(!context.Options.IgnoreNullValues || !(memberValue")
                        .Append(index).AppendLine(" is null)) &&");
                    source.Append("                        ");
                }
                source.Append("(!context.Options.IgnoreDefaultValues || !global::System.Collections.Generic.EqualityComparer<")
                    .Append(memberType).Append(">.Default.Equals(memberValue").Append(index)
                    .Append(", default!)))").AppendLine();
                source.AppendLine("                    {");
                source.Append("                        writer.WritePropertyName(")
                    .Append(StringLiteral(member.JsonName)).AppendLine(");");
                EmitWriteValue(source, member.Type, member.ConverterType, index,
                    "memberValue" + index, targets, 24);
                source.AppendLine("                    }");
                source.AppendLine("                }");
            }
            source.AppendLine("                writer.WriteEndObject();");
            source.AppendLine("            }");
        }

        private static string WriteStatement(
            ITypeSymbol type,
            INamedTypeSymbol? converterType,
            int converterIndex,
            string value,
            IReadOnlyList<TargetModel> targets)
        {
            if (converterType != null)
                return "s_memberConverter" + converterIndex + ".Write(writer, " + value + ", context);";
            var targetIndex = FindTargetIndex(type, targets);
            if (targetIndex >= 0)
                return "s_yuzeJsonResolver.TypeInfo" + targetIndex + ".Converter.Write(writer, " + value + ", context);";
            switch (type.SpecialType)
            {
                case SpecialType.System_String: return "writer.WriteStringValue(" + value + ");";
                case SpecialType.System_Boolean: return "writer.WriteBooleanValue(" + value + ");";
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                    return "writer.WriteNumberValue(" + value + ");";
                default:
                    return "context.WriteValue(writer, " + value + ");";
            }
        }

        private static void EmitWriteValue(
            StringBuilder source,
            ITypeSymbol type,
            INamedTypeSymbol? converterType,
            int memberIndex,
            string value,
            IReadOnlyList<TargetModel> targets,
            int indent)
        {
            var spaces = new string(' ', indent);
            if (converterType != null)
            {
                source.Append(spaces).AppendLine(WriteStatement(
                    type, converterType, memberIndex, value, targets));
                return;
            }

            ITypeSymbol? elementType = null;
            if (type is IArrayTypeSymbol arrayType && arrayType.Rank == 1) elementType = arrayType.ElementType;
            else if (TryGetListElement(type, out var listElement)) elementType = listElement;
            if (elementType != null)
            {
                source.Append(spaces).Append("if (").Append(value).AppendLine(" == null) writer.WriteNullValue();");
                source.Append(spaces).AppendLine("else");
                source.Append(spaces).AppendLine("{");
                source.Append(spaces).AppendLine("    writer.WriteStartArray();");
                source.Append(spaces).Append("    foreach (var item").Append(memberIndex).Append(" in ")
                    .Append(value).AppendLine(")");
                source.Append(spaces).Append("        ").AppendLine(WriteStatement(
                    elementType, null, memberIndex, "item" + memberIndex, targets));
                source.Append(spaces).AppendLine("    writer.WriteEndArray();");
                source.Append(spaces).AppendLine("}");
                return;
            }

            if (TryGetDictionaryValue(type, out var dictionaryValue))
            {
                source.Append(spaces).Append("if (").Append(value).AppendLine(" == null) writer.WriteNullValue();");
                source.Append(spaces).AppendLine("else");
                source.Append(spaces).AppendLine("{");
                source.Append(spaces).AppendLine("    writer.WriteStartObject();");
                source.Append(spaces).Append("    foreach (var entry").Append(memberIndex).Append(" in ")
                    .Append(value).AppendLine(")");
                source.Append(spaces).AppendLine("    {");
                if (CanBeNull(dictionaryValue))
                {
                    source.Append(spaces).Append("        if (context.Options.IgnoreNullValues && entry")
                        .Append(memberIndex).AppendLine(".Value is null) continue;");
                }
                source.Append(spaces).Append("        writer.WritePropertyName(entry").Append(memberIndex)
                    .AppendLine(".Key);");
                source.Append(spaces).Append("        ").AppendLine(WriteStatement(
                    dictionaryValue, null, memberIndex, "entry" + memberIndex + ".Value", targets));
                source.Append(spaces).AppendLine("    }");
                source.Append(spaces).AppendLine("    writer.WriteEndObject();");
                source.Append(spaces).AppendLine("}");
                return;
            }

            source.Append(spaces).AppendLine(WriteStatement(type, null, memberIndex, value, targets));
        }

        private static int FindTargetIndex(ITypeSymbol type, IReadOnlyList<TargetModel> targets)
        {
            for (var index = 0; index < targets.Count; index++)
            {
                if (SymbolEqualityComparer.Default.Equals(type, targets[index].Type)) return index;
            }
            return -1;
        }

        private static bool TryGetListElement(ITypeSymbol type, out ITypeSymbol elementType)
        {
            if (type is INamedTypeSymbol namedType && namedType.IsGenericType &&
                namedType.Name == "List" && namedType.Arity == 1 &&
                namedType.ContainingNamespace.ToDisplayString() == "System.Collections.Generic")
            {
                elementType = namedType.TypeArguments[0];
                return true;
            }
            elementType = null!;
            return false;
        }

        private static bool TryGetDictionaryValue(ITypeSymbol type, out ITypeSymbol valueType)
        {
            if (type is INamedTypeSymbol namedType && namedType.IsGenericType &&
                namedType.Name == "Dictionary" && namedType.Arity == 2 &&
                namedType.ContainingNamespace.ToDisplayString() == "System.Collections.Generic" &&
                namedType.TypeArguments[0].SpecialType == SpecialType.System_String)
            {
                valueType = namedType.TypeArguments[1];
                return true;
            }
            valueType = null!;
            return false;
        }

        private static bool CanBeNull(ITypeSymbol type)
        {
            if (type.IsReferenceType) return true;
            return type is INamedTypeSymbol namedType &&
                   namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        }

        private static string TypeName(ITypeSymbol type) =>
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        private static string GetAccessibility(INamedTypeSymbol type)
        {
            return type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
        }

        private static string EscapeIdentifier(string name) =>
            SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

        private static string GetHintName(INamedTypeSymbol type)
        {
            var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty)
                .Replace('.', '_')
                .Replace('+', '_');
            return name + ".YuzeJson.g.cs";
        }

        private static string StringLiteral(string value) => "\"" + EscapeForString(value) + "\"";

        private static string EscapeForString(string value)
        {
            var result = new StringBuilder(value.Length + 8);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\': result.Append("\\\\"); break;
                    case '"': result.Append("\\\""); break;
                    case '\r': result.Append("\\r"); break;
                    case '\n': result.Append("\\n"); break;
                    case '\t': result.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                            result.Append("\\u").Append(((int)character).ToString("X4"));
                        else result.Append(character);
                        break;
                }
            }
            return result.ToString();
        }

        private static DiagnosticDescriptor CreateError(string id, string title, string message) =>
            new DiagnosticDescriptor(id, title, message, "YuzeJsonTool", DiagnosticSeverity.Error, true);

        private static void Report(
            GeneratorExecutionContext context,
            DiagnosticDescriptor descriptor,
            Location location,
            params object[] arguments) =>
            context.ReportDiagnostic(Diagnostic.Create(descriptor, location, arguments));

        private sealed class Receiver : ISyntaxReceiver
        {
            internal List<ClassDeclarationSyntax> Candidates { get; } = new List<ClassDeclarationSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (syntaxNode is ClassDeclarationSyntax declaration && declaration.AttributeLists.Count > 0)
                    Candidates.Add(declaration);
            }
        }

        private sealed class TargetModel
        {
            internal INamedTypeSymbol Type { get; }
            internal INamedTypeSymbol? ConverterType { get; }
            internal IReadOnlyList<MemberModel> Members { get; }

            internal TargetModel(
                INamedTypeSymbol type,
                INamedTypeSymbol? converterType,
                IReadOnlyList<MemberModel> members)
            {
                Type = type;
                ConverterType = converterType;
                Members = members;
            }
        }

        private sealed class MemberModel
        {
            internal ISymbol Symbol { get; }
            internal ITypeSymbol Type { get; }
            internal string JsonName { get; }
            internal bool CanRead { get; }
            internal bool CanWrite { get; }
            internal bool IsField { get; }
            internal bool Required { get; }
            internal bool HasConverterAttribute { get; }
            internal INamedTypeSymbol? ConverterType { get; }

            internal MemberModel(
                ISymbol symbol,
                ITypeSymbol type,
                string jsonName,
                bool canRead,
                bool canWrite,
                bool isField,
                bool required,
                bool hasConverterAttribute,
                INamedTypeSymbol? converterType)
            {
                Symbol = symbol;
                Type = type;
                JsonName = jsonName;
                CanRead = canRead;
                CanWrite = canWrite;
                IsField = isField;
                Required = required;
                HasConverterAttribute = hasConverterAttribute;
                ConverterType = converterType;
            }
        }
    }
}
