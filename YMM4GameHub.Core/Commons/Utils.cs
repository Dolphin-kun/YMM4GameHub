using System.Text.Json;

namespace YMM4GameHub.Core.Commons
{
    public static class Utils
    {
        public static T? DeepClone<T>(T obj)
        {
            if (obj == null) return default;
            var json = JsonSerializer.Serialize(obj);
            return JsonSerializer.Deserialize<T>(json);
        }
    }
}
