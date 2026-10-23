#nullable enable
using System;

namespace YuzeToolkit.Json
{
    public sealed class JsonOptions
    {
        public static JsonOptions Default { get; } = new();

        public bool AllowComments { get; set; }
        public bool AllowTrailingCommas { get; set; }
        public bool PropertyNameCaseInsensitive { get; set; }
        public bool WriteIndented { get; set; }
        public bool IgnoreNullValues { get; set; }
        public bool IgnoreDefaultValues { get; set; }
        public bool IncludeFields { get; set; } = true;
        public bool IncludeProperties { get; set; } = true;
        public int IndentSize { get; set; } = 2;
        public int MaxDepth { get; set; } = 64;
        public JsonUnknownMemberHandling UnknownMemberHandling { get; set; } = JsonUnknownMemberHandling.Ignore;
        public JsonDuplicatePropertyHandling DuplicatePropertyHandling { get; set; } =
            JsonDuplicatePropertyHandling.Replace;
        public JsonEnumFormat EnumFormat { get; set; } = JsonEnumFormat.Number;
        public IJsonTypeResolver? Resolver { get; set; }

        internal void Validate()
        {
            if (MaxDepth < 1 || MaxDepth > 1024)
                throw new ArgumentOutOfRangeException(nameof(MaxDepth), "Maximum depth must be between 1 and 1024.");
            if (IndentSize < 0 || IndentSize > 16)
                throw new ArgumentOutOfRangeException(nameof(IndentSize), "Indent size must be between 0 and 16.");
        }

        public JsonOptions Clone() => new()
        {
            AllowComments = AllowComments,
            AllowTrailingCommas = AllowTrailingCommas,
            PropertyNameCaseInsensitive = PropertyNameCaseInsensitive,
            WriteIndented = WriteIndented,
            IgnoreNullValues = IgnoreNullValues,
            IgnoreDefaultValues = IgnoreDefaultValues,
            IncludeFields = IncludeFields,
            IncludeProperties = IncludeProperties,
            IndentSize = IndentSize,
            MaxDepth = MaxDepth,
            UnknownMemberHandling = UnknownMemberHandling,
            DuplicatePropertyHandling = DuplicatePropertyHandling,
            EnumFormat = EnumFormat,
            Resolver = Resolver
        };
    }
}
