//   dotnet run -- <client.realm> [--classes A,B,C] [--query "RQL"] [--out file.json]

using Realms;
using Realms.Schema;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length < 1)
{
    Console.Error.WriteLine(
        "usage: realmdump <client.realm> [--classes A,B,C] [--query \"RQL\"] [--out file.json]");
    return 1;
}

string path = args[0];
HashSet<string>? only = null;
string? outPath = null;
string? query = null;
for (int i = 1; i < args.Length; i++)
{
    if (args[i] == "--classes" && i + 1 < args.Length)
        only = new HashSet<string>(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries));
    else if (args[i] == "--out" && i + 1 < args.Length)
        outPath = args[++i];
    else if ((args[i] == "--query" || args[i] == "-q") && i + 1 < args.Length)
        query = args[++i];
}

if (!File.Exists(path))
{
    Console.Error.WriteLine($"no such file: {path}");
    return 1;
}

// Realm resolves a relative path against its own default database directory,
// not the process working directory, so a relative path that File.Exists finds
// still fails to open. Resolve to an absolute path first.
path = Path.GetFullPath(path);

// Read-only + dynamic: open whatever schema the file declares, no models.
var config = new RealmConfiguration(path)
{
    IsReadOnly = true,
    IsDynamic = true,
};

using var realm = Realm.GetInstance(config);

const int MaxDepth = 3;   // cap link recursion to avoid cycles (Set<->Beatmaps)

JsonNode? SerializeValue(RealmValue v, int depth)
{
    switch (v.Type)
    {
        case RealmValueType.Null: return null;
        case RealmValueType.String: return JsonValue.Create(v.AsString());
        case RealmValueType.Int: return JsonValue.Create(v.AsInt64());
        case RealmValueType.Bool: return JsonValue.Create(v.AsBool());
        case RealmValueType.Float: return JsonValue.Create(v.AsFloat());
        case RealmValueType.Double: return JsonValue.Create(v.AsDouble());
        case RealmValueType.Decimal128: return JsonValue.Create(v.AsDecimal().ToString());
        case RealmValueType.Date: return JsonValue.Create(v.AsDate().ToString("o"));
        case RealmValueType.Guid: return JsonValue.Create(v.AsGuid().ToString());
        case RealmValueType.ObjectId: return JsonValue.Create(v.AsObjectId().ToString());
        case RealmValueType.Data: return JsonValue.Create(Convert.ToBase64String(v.AsData()));
        case RealmValueType.Object:
            return depth >= MaxDepth ? JsonValue.Create("<…>")
                                     : SerializeObject(v.AsIRealmObject(), depth + 1);
        default: return JsonValue.Create(v.ToString());
    }
}

JsonObject SerializeObject(IRealmObjectBase obj, int depth)
{
    var o = new JsonObject();
    var schema = obj.ObjectSchema!;
    foreach (var p in schema)
    {
        try
        {
            if (p.Type.HasFlag(PropertyType.Array) || p.Type.HasFlag(PropertyType.Set))
            {
                var ja = new JsonArray();
                foreach (var item in obj.DynamicApi.GetList<RealmValue>(p.Name))
                    ja.Add(SerializeValue(item, depth));
                o[p.Name] = ja;
            }
            else if (p.Type.HasFlag(PropertyType.Dictionary))
            {
                var jo = new JsonObject();
                foreach (var kv in obj.DynamicApi.GetDictionary<RealmValue>(p.Name))
                    jo[kv.Key] = SerializeValue(kv.Value, depth);
                o[p.Name] = jo;
            }
            else
            {
                o[p.Name] = SerializeValue(obj.DynamicApi.Get<RealmValue>(p.Name), depth);
            }
        }
        catch (Exception e)
        {
            o[p.Name] = JsonValue.Create($"<err: {e.Message}>");
        }
    }
    return o;
}

var root = new JsonObject();
foreach (var objSchema in realm.Schema)
{
    if (only != null && !only.Contains(objSchema.Name))
        continue;
    var arr = new JsonArray();
    try
    {
        var results = realm.DynamicApi.All(objSchema.Name);
        if (query != null)
        {
            try
            {
                results = results.Filter(query);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"  {objSchema.Name}: query not applicable, skipped ({e.Message})");
                continue;
            }
        }
        foreach (var obj in results)
            arr.Add(SerializeObject(obj, 0));
    }
    catch (ArgumentException)
    {
        Console.Error.WriteLine($"  {objSchema.Name}: embedded, skipped (nested under its owner)");
        continue;
    }
    root[objSchema.Name] = arr;
    Console.Error.WriteLine($"  {objSchema.Name}: {arr.Count} objects");
}

string json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
if (outPath != null)
{
    File.WriteAllText(outPath, json);
    Console.Error.WriteLine($"wrote {outPath} ({json.Length} bytes)");
}
else
{
    Console.WriteLine(json);
}
return 0;
