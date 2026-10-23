#nullable enable
using UnityEditor;
using YuzeToolkit.Json.Unity;

namespace YuzeToolkit.Json.Editor
{
    [InitializeOnLoad]
    internal static class UnityJsonEditorRegistration
    {
        static UnityJsonEditorRegistration() => UnityJsonConverters.RegisterDefaults();
    }
}
