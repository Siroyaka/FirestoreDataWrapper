using System;
using System.Collections.Generic;
using System.Linq;
using MSJson = System.Text.Json;
using System.Text;
using FS = Google.Cloud.Firestore;
using System.Text.Encodings.Web;

namespace FirestoreDataWrapper
{
    public class Wrapper
    {
        Func<DateTime, string> ParseDate;
        public Wrapper()
        {
            ParseDate = DefaultParseDate;
        }

        string DefaultParseDate(DateTime date) => date.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        public static string JpParseDate(DateTime date) => date.ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:ss+09:00");

        public void SetParseDate(Func<DateTime, string> func) => ParseDate = func;

        string JsonSerialize<T>(T data, bool shaped = true)
        {
            var jsonSerializerOption = new MSJson.JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
                WriteIndented = shaped
            };
            return MSJson.JsonSerializer.Serialize(data, jsonSerializerOption);
        }

        T JsonDeserialize<T>(string jsonText)
        {
            return MSJson.JsonSerializer.Deserialize<T>(jsonText);
        }

        public Dictionary<string, object> ToFirestoreObject(object data, string prefix = "", Dictionary<string, string> correspondings = null)
        {
            if(correspondings == null) correspondings = new Dictionary<string, string>();
            var firestoreObject = new Dictionary<string, object>();

            foreach(var property in data.GetType().GetProperties())
            {
                var nextPrefix = prefix + (prefix.Length > 0 ? "." : "") + property.Name;
                var propertyName = correspondings.ContainsKey(nextPrefix) ? correspondings[nextPrefix] : property.Name;
                object propertyValue = ParseFirestoreObject(property.GetValue(data), nextPrefix, correspondings);
                firestoreObject.Add(propertyName, propertyValue);
            }

            return firestoreObject;
        }

        object ParseFirestoreObject(object data, string prefix, Dictionary<string, string> correspondings)
        {
            return data switch
            {
                IEnumerable<object> x => x.Select(y => ParseFirestoreObject(y, prefix, correspondings)).ToList(),
                IEnumerable<long> x => x.Select(y => ParseFirestoreObject(y, prefix, correspondings)).ToList(),
                Dictionary<string, List<string>> x => x.ToDictionary(
                    pair => pair.Key,
                    pair => ParseFirestoreObject(pair.Value, prefix + (prefix.Length > 0 ? "." : "") + pair.Key, correspondings)
                ),
                Dictionary<string, List<long>> x => x.ToDictionary(
                    pair => pair.Key,
                    pair => ParseFirestoreObject(pair.Value, prefix + (prefix.Length > 0 ? "." : "") + pair.Key, correspondings)
                ),
                Dictionary<string, object> x => x.ToDictionary(
                    pair => pair.Key,
                    pair => ParseFirestoreObject(pair.Value, prefix + (prefix.Length > 0 ? "." : "") + pair.Key, correspondings)
                ),
                null => null,
                string x => x,
                long x => x,
                DateTime x => x.ToUniversalTime(),
                bool x => x,
                _ => ToFirestoreObject(data, prefix, correspondings)
            };
        }

        public string ToJson(Dictionary<string, object> data, string prefix = "", Dictionary<string, string> correspondings = null, bool shaped = false)
        {
            if(correspondings == null) correspondings = new Dictionary<string, string>();
            var sb = new StringBuilder();
            foreach(var items in data)
            {
                if(sb.Length > 0) sb.AppendLine(",");
                var nextPrefix = prefix + (prefix.Length > 0 ? "." : "") + items.Key;
                Func<object, string> objectParse = (object x) => ParseJsonValue(x, nextPrefix, correspondings);
                var propertyName = correspondings.ContainsKey(nextPrefix) ? correspondings[nextPrefix] : items.Key;
                var propertyValue = items.Value switch
                {
                    IEnumerable<object> list => $"[{string.Join(',', list.Select(x => objectParse(x)))}]",
                    IEnumerable<long> list => $"[{string.Join(',', list.Select(x => objectParse(x)))}]",
                    object x => objectParse(x),
                    null => "null"
                };

                sb.Append($"\"{propertyName}\": {propertyValue}");
            }
            var json = "{" + sb.ToString() + "}";

            return shaped ? ShapeJsonText(json) : json;
        }

        public string ShapeJsonText(string jsonText, bool oneLine = false)
        {
            var d = JsonDeserialize<dynamic>(jsonText);
            return JsonSerialize(d, !oneLine);
        }

        string ParseJsonValue(object obj, string prefix, Dictionary<string, string> correspondings)
        {
            return obj switch
            {
                string x => $"\"{x}\"",
                long x => $"{x.ToString()}",
                bool x => $"{(x ? "true" : "false")}",
                FS.Timestamp x => $"\"{ParseDate(x.ToDateTime())}\"",
                Dictionary<string, object> x => ToJson(x, prefix, correspondings),
                _ => ""
            };
        }

        public T ToData<T>(Dictionary<string, object> data) where T: class, new()
        {
            var json = ToJson(data);
            return JsonDeserialize<T>(json);
        }

    }
}
