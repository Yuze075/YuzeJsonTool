#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;

namespace YuzeToolkit.Json
{
    internal sealed class ReflectionObjectJsonConverter : JsonConverter
    {
        private sealed class MemberContract
        {
            public string JsonName = string.Empty;
            public Type Type = null!;
            public FieldInfo? Field;
            public PropertyInfo? Property;
            public JsonConverter? Converter;
            public object? DefaultValue;
            public bool Required;
            public bool IsField;
            public bool CanRead;
            public bool CanWrite;

            public object? GetValue(object instance) => IsField
                ? Field!.GetValue(instance)
                : Property!.GetValue(instance, null);

            public void SetValue(object instance, object? value)
            {
                if (IsField) Field!.SetValue(instance, value);
                else Property!.SetValue(instance, value, null);
            }
        }

        private readonly Type _type;
        private readonly ConstructorInfo? _constructor;
        private readonly MemberContract[] _members;
        private readonly Dictionary<string, MemberContract> _membersByName;
        private readonly Dictionary<string, MemberContract> _membersByNameIgnoreCase;

        public ReflectionObjectJsonConverter(Type type)
        {
            _type = type;
            _constructor = type.IsValueType ? null : type.GetConstructor(Type.EmptyTypes);
            if (!type.IsValueType && _constructor == null)
                throw new NotSupportedException($"Type {type} requires a public parameterless constructor or a custom converter.");

            var members = new List<MemberContract>();
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.IsStatic || field.IsDefined(typeof(JsonIgnoreAttribute), inherit: true)) continue;
                members.Add(CreateField(field));
            }
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length != 0 ||
                    property.IsDefined(typeof(JsonIgnoreAttribute), inherit: true)) continue;
                var getter = property.GetGetMethod(nonPublic: false);
                var setter = property.GetSetMethod(nonPublic: false);
                if (getter == null && setter == null) continue;
                members.Add(CreateProperty(property, getter != null, setter != null));
            }

            _members = members.ToArray();
            _membersByName = new Dictionary<string, MemberContract>(StringComparer.Ordinal);
            _membersByNameIgnoreCase = new Dictionary<string, MemberContract>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in _members)
            {
                if (member.Required && !member.CanWrite)
                    throw new JsonException($"Required JSON member '{member.JsonName}' on {_type} is not writable.");
                if (!_membersByName.TryAdd(member.JsonName, member))
                    throw new JsonException($"Type {_type} contains multiple JSON members named '{member.JsonName}'.");
                if (!_membersByNameIgnoreCase.TryAdd(member.JsonName, member))
                    throw new JsonException($"Type {_type} contains JSON members that differ only by case: '{member.JsonName}'.");
            }
        }

        public override Type TargetType => _type;

        internal override object? ReadObject(ref JsonReader reader, JsonSerializationContext context)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                if (_type.IsValueType) throw new JsonException($"JSON null cannot be assigned to {_type}.");
                return null;
            }
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException($"Expected JSON object for {_type}, found {reader.TokenType}.");

            var instance = _type.IsValueType ? Activator.CreateInstance(_type)! : _constructor!.Invoke(null);
            HashSet<MemberContract>? required = null;
            foreach (var member in _members)
            {
                if (!member.Required) continue;
                required ??= new HashSet<MemberContract>();
                required.Add(member);
            }
            HashSet<string>? names = context.Options.DuplicatePropertyHandling == JsonDuplicatePropertyHandling.Error
                ? new HashSet<string>(StringComparer.Ordinal) : null;
            var map = context.Options.PropertyNameCaseInsensitive ? _membersByNameIgnoreCase : _membersByName;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException($"Expected a property name while reading {_type}.");
                var name = reader.GetString();
                if (names != null && !names.Add(name)) throw new JsonException($"Duplicate JSON property '{name}'.");
                if (!reader.Read()) throw new JsonException($"Property '{name}' is missing its value.");

                if (!map.TryGetValue(name, out var member) || !member.CanWrite ||
                    (member.IsField && !context.Options.IncludeFields) ||
                    (!member.IsField && !context.Options.IncludeProperties))
                {
                    if (context.Options.UnknownMemberHandling == JsonUnknownMemberHandling.Error)
                        throw new JsonException($"Type {_type} does not contain a writable JSON member named '{name}'.");
                    reader.SkipCurrentValue();
                    continue;
                }

                var converter = member.Converter ?? context.GetConverter(member.Type);
                member.SetValue(instance, converter.ReadObject(ref reader, context));
                required?.Remove(member);
            }

            if (required is { Count: > 0 })
            {
                var missing = new List<string>();
                foreach (var member in required) missing.Add(member.JsonName);
                throw new JsonException($"Required JSON members are missing for {_type}: {string.Join(", ", missing)}.");
            }
            return instance;
        }

        internal override void WriteObject(JsonWriter writer, object? value, JsonSerializationContext context)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }
            if (!_type.IsInstanceOfType(value))
                throw new JsonException($"Expected {_type}, found {value.GetType()}.");

            writer.WriteStartObject();
            foreach (var member in _members)
            {
                if (!member.CanRead || (member.IsField && !context.Options.IncludeFields) ||
                    (!member.IsField && !context.Options.IncludeProperties)) continue;
                var memberValue = member.GetValue(value);
                if (context.Options.IgnoreNullValues && memberValue == null) continue;
                if (context.Options.IgnoreDefaultValues && Equals(memberValue, member.DefaultValue)) continue;
                writer.WritePropertyName(member.JsonName);
                (member.Converter ?? context.GetConverter(member.Type)).WriteObject(writer, memberValue, context);
            }
            writer.WriteEndObject();
        }

        private static MemberContract CreateField(FieldInfo field)
        {
            return new MemberContract
            {
                JsonName = field.GetCustomAttribute<JsonNameAttribute>(inherit: true)?.Name ?? field.Name,
                Type = field.FieldType,
                Field = field,
                Converter = CreateMemberConverter(field, field.FieldType),
                DefaultValue = field.FieldType.IsValueType ? Activator.CreateInstance(field.FieldType) : null,
                Required = field.IsDefined(typeof(JsonRequiredAttribute), inherit: true),
                IsField = true,
                CanRead = true,
                CanWrite = !field.IsInitOnly && !field.IsLiteral
            };
        }

        private static MemberContract CreateProperty(PropertyInfo property, bool canRead, bool canWrite)
        {
            return new MemberContract
            {
                JsonName = property.GetCustomAttribute<JsonNameAttribute>(inherit: true)?.Name ?? property.Name,
                Type = property.PropertyType,
                Property = property,
                Converter = CreateMemberConverter(property, property.PropertyType),
                DefaultValue = property.PropertyType.IsValueType ? Activator.CreateInstance(property.PropertyType) : null,
                Required = property.IsDefined(typeof(JsonRequiredAttribute), inherit: true),
                CanRead = canRead,
                CanWrite = canWrite
            };
        }

        private static JsonConverter? CreateMemberConverter(MemberInfo member, Type memberType)
        {
            var attribute = member.GetCustomAttribute<JsonConverterAttribute>(inherit: true);
            if (attribute == null) return null;
            var converter = Activator.CreateInstance(attribute.ConverterType) as JsonConverter
                            ?? throw new JsonException($"{attribute.ConverterType} is not a JsonConverter.");
            if (!converter.CanConvert(memberType))
                throw new JsonException($"Converter {attribute.ConverterType} cannot convert member type {memberType}.");
            return converter;
        }
    }
}
